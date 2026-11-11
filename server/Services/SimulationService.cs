using BalloonMesh.Server.Hubs;
using BalloonMesh.Server.Models;
using BalloonMesh.Protocol;

namespace BalloonMesh.Server.Services;

/// <summary>
/// Development/demo data source.  It generates noisy FTM reports for the seeded
/// balloons and feeds the exact same aggregation path as a gateway.  It never
/// writes positions directly.
/// </summary>
public sealed class SimulationService : BackgroundService
{
    private readonly MeshReportRouter _router;
    private readonly FleetService _fleet;
    private readonly IMonitorEvents _notifier;
    private readonly TimeProvider _time;
    private readonly ILogger<SimulationService> _logger;
    private readonly Dictionary<ushort, TrueBalloon> _truth = [];
    private readonly Dictionary<ushort, byte> _batch = [];
    private readonly object _gate = new();
    private volatile bool _running;
    private int _statusPhase;

    public SimulationService(
        MeshReportRouter router,
        FleetService fleet,
        IMonitorEvents notifier,
        TimeProvider time,
        IConfiguration configuration,
        ILogger<SimulationService> logger)
    {
        _router = router;
        _fleet = fleet;
        _notifier = notifier;
        _time = time;
        _logger = logger;
        _running = configuration.GetValue("Simulation:Enabled", true);
    }

    public bool Running => _running;

    public async Task SetRunningAsync(bool running, CancellationToken cancellationToken = default)
    {
        _running = running;
        if (running)
        {
            await InitializeTruthAsync(cancellationToken);
        }
        await _notifier.SimulationChangedAsync(running);
    }

    private Task InitializeTruthAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _truth.Clear();
            foreach (var balloon in _fleet.GetBalloons())
            {
                var lat = balloon.Lat ?? 35.6300;
                var lon = balloon.Lon ?? 139.7750;
                _truth[balloon.MeshAddress] = new TrueBalloon(balloon.MeshAddress, lat, lon,
                    Random.Shared.NextDouble() * Math.PI * 2);
            }
        }
        return Task.CompletedTask;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_running)
        {
            await InitializeTruthAsync(stoppingToken);
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (!_running)
            {
                continue;
            }
            try
            {
                SimulateCycle();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "simulation cycle failed");
            }
        }
    }

    private void SimulateCycle()
    {
        var anchors = _fleet.GetAnchors().Where(anchor => anchor.PositionValid).ToArray();
        if (anchors.Length < 2)
        {
            _logger.LogWarning("simulation requires at least two anchors");
            return;
        }

        var now = _time.GetUtcNow();
        var timestamp = BalloonProtocol.NowTimestamp32(_time);
        var events = new List<(ushort Source, GatewayEventDto Message)>();
        var balloons = _fleet.GetBalloons();

        lock (_gate)
        {
            foreach (var balloon in balloons)
            {
                if (!_truth.TryGetValue(balloon.MeshAddress, out var truth))
                {
                    truth = new TrueBalloon(balloon.MeshAddress,
                        balloon.Lat ?? 35.6300, balloon.Lon ?? 139.7750,
                        Random.Shared.NextDouble() * Math.PI * 2);
                    _truth[balloon.MeshAddress] = truth;
                }

                // Slow wind drift, clamped to the seeded area.
                truth.Phase += 0.03 + Random.Shared.NextDouble() * 0.02;
                truth.Lat += Math.Sin(truth.Phase) * 0.0000015 + (Random.Shared.NextDouble() - 0.5) * 0.000001;
                truth.Lon += Math.Cos(truth.Phase * 0.87) * 0.0000018 + (Random.Shared.NextDouble() - 0.5) * 0.000001;
                truth.Lat = Math.Clamp(truth.Lat, 35.6275, 35.6325);
                truth.Lon = Math.Clamp(truth.Lon, 139.7720, 139.7780);

                var batch = _batch.TryGetValue(balloon.MeshAddress, out var current)
                    ? (byte)(current + 1)
                    : (byte)1;
                _batch[balloon.MeshAddress] = batch;

                var count = anchors.Length >= 3 ? 3 : anchors.Length;
                for (var i = 0; i < count; i++)
                {
                    var anchor = anchors[(balloon.MeshAddress + i) % anchors.Length];
                    var distance = DistanceMeters(truth.Lat, truth.Lon, anchor.Lat, anchor.Lon)
                                   + (Random.Shared.NextDouble() - 0.5) * 0.7;
                    var report = new FtmReport(
                        anchor.AnchorId,
                        (ushort)Math.Clamp(distance * 100.0, 1.0, 65535.0),
                        timestamp,
                        batch);
                    events.Add((balloon.MeshAddress, new GatewayEventDto(
                        balloon.MeshAddress,
                        BalloonProtocol.GroupFtmReport,
                        BalloonProtocol.VendorOpcode(BalloonProtocol.OpFtmReport),
                        Convert.ToHexString(BalloonProtocol.EncodeFtmReport(report)),
                        -55,
                        7,
                        now.ToUnixTimeMilliseconds())));
                }
            }
        }

        // Two gateways deliver every report; server-side duplicate removal must
        // collapse them.
        foreach (var item in events)
        {
            _router.Handle(item.Message, "sim-gateway-a");
            _router.Handle(item.Message, "sim-gateway-b");
        }

        if (++_statusPhase % 4 == 0)
        {
            foreach (var balloon in balloons.Where((_, index) => index % 7 == 0))
            {
                var count = (ushort)Random.Shared.Next(280, 430);
                var status = new QueueStatus(
                    count >= 401 ? BalloonProtocol.OpQueueStatusNearFull : BalloonProtocol.OpQueueStatusRecovered,
                    count);
                _router.Handle(new GatewayEventDto(
                    balloon.MeshAddress,
                    BalloonProtocol.GroupQueueStatus,
                    BalloonProtocol.VendorOpcode(BalloonProtocol.OpQueueStatus),
                    Convert.ToHexString(BalloonProtocol.EncodeQueueStatus(status)),
                    -60,
                    7,
                    now.ToUnixTimeMilliseconds()), "sim-gateway-a");
            }
        }

        _logger.LogDebug("simulation emitted {Count} FTM reports", events.Count);
    }

    public static double DistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        var meanLat = (lat1 + lat2) * 0.5 * Math.PI / 180.0;
        var dy = (lat2 - lat1) * 111_320.0;
        var dx = (lon2 - lon1) * 111_320.0 * Math.Cos(meanLat);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private sealed class TrueBalloon(ushort address, double lat, double lon, double phase)
    {
        public ushort Address { get; } = address;
        public double Lat { get; set; } = lat;
        public double Lon { get; set; } = lon;
        public double Phase { get; set; } = phase;
    }
}
