using BalloonMesh.Server.Data;
using BalloonMesh.Server.Hubs;
using BalloonMesh.Server.Models;
using BalloonMesh.Protocol;

namespace BalloonMesh.Server.Services;

/// <summary>
/// Rolling downlink scheduler.  The server estimates each balloon's reservation
/// queue (target 350 of the device hard limit 512), sends reservations to all
/// gateways, and supplements capacity as it becomes available.
/// </summary>
public sealed class LedDispatchService
{
    private const int TargetQueueDepth = 350;
    private const int HardQueueDepth = 512;

    private readonly object _gate = new();
    private readonly List<PendingReservation> _pending = [];
    private readonly Dictionary<ushort, QueueEstimate> _estimates = [];
    private readonly Dictionary<ushort, ulong> _lastIssued = [];
    private readonly Dictionary<ushort, ulong> _lastCutoff = [];
    private readonly GatewayRegistry _gateways;
    private readonly FleetService _fleet;
    private readonly DataStore _store;
    private readonly IMonitorEvents _notifier;
    private readonly TimeProvider _time;
    private readonly ILogger<LedDispatchService> _logger;

    public LedDispatchService(
        GatewayRegistry gateways,
        FleetService fleet,
        DataStore store,
        IMonitorEvents notifier,
        TimeProvider time,
        ILogger<LedDispatchService> logger)
    {
        _gateways = gateways;
        _fleet = fleet;
        _store = store;
        _notifier = notifier;
        _time = time;
        _logger = logger;
    }

    public int PendingCount
    {
        get
        {
            lock (_gate)
            {
                return _pending.Count;
            }
        }
    }

    public bool EnqueueScheduled(TargetSpec target, ulong executeTick, DateTimeOffset executeAt,
        byte r, byte g, byte b, long? showStepId = null)
    {
        var nowTick = BalloonProtocol.NowTick40(_time);
        if (executeTick <= nowTick)
        {
            return false;
        }

        lock (_gate)
        {
            InsertPending(new PendingReservation(
                target,
                BalloonProtocol.OpLedSet,
                BalloonProtocol.EncodeLedSet(executeTick, r, g, b),
                executeTick,
                executeAt,
                r, g, b,
                showStepId));
        }
        return true;
    }

    public int SendImmediate(TargetSpec target, byte opcode, byte[] payload)
    {
        var command = new DownlinkCommand(target.Destination, BalloonProtocol.VendorOpcode(opcode), payload, 7);
        return _gateways.Broadcast(command);
    }

    public ulong NextIssuedTick(IEnumerable<ushort> addresses)
    {
        var now = BalloonProtocol.NowTick40(_time);
        lock (_gate)
        {
            ulong value = now;
            foreach (var address in addresses)
            {
                if (_lastIssued.TryGetValue(address, out var last) && last >= value)
                {
                    value = last + 1;
                }
            }
            foreach (var address in addresses)
            {
                _lastIssued[address] = value;
            }
            return value;
        }
    }

    /// <summary>
    /// Returns the monotonic cutoff that would be used, without committing it.
    /// NoteCutoff commits only after a reset was actually delivered.
    /// </summary>
    public ulong NextCutoffTick(IEnumerable<ushort> addresses, ulong requested)
    {
        lock (_gate)
        {
            ulong value = requested;
            foreach (var address in addresses)
            {
                if (_lastCutoff.TryGetValue(address, out var last) && last >= value)
                {
                    value = last + 1;
                }
            }
            return value;
        }
    }

    public IReadOnlyList<long> NoteCutoff(IEnumerable<ushort> addresses, ulong cutoffTick)
    {
        var targets = addresses.ToHashSet();
        var removed = new List<long>();
        lock (_gate)
        {
            // A RESET that reached a gateway also applies to reservations that
            // are still pending locally; dispatching them afterwards would
            // resurrect commands the user already cancelled.
            removed.AddRange(RemovePending(item =>
                item.ExecuteTick <= cutoffTick && CoveredBy(item.Target, targets)));
            foreach (var address in targets)
            {
                var estimate = GetEstimate(address);
                estimate.SentTicks.RemoveAll(tick => tick <= cutoffTick);
                estimate.LastCutoff = Math.Max(estimate.LastCutoff, cutoffTick);
                estimate.ReportedCount = 0;
                estimate.ReportedAtTick = 0;
                estimate.LastDecayTick = 0;
                _lastCutoff[address] = Math.Max(
                    _lastCutoff.GetValueOrDefault(address), cutoffTick);
                WriteEstimate(address, estimate);
            }
        }
        return removed;
    }

    public IReadOnlyList<long> NoteCancel(IEnumerable<ushort> addresses, ulong executeTick)
    {
        var targets = addresses.ToHashSet();
        var removed = new List<long>();
        lock (_gate)
        {
            removed.AddRange(RemovePending(item =>
                item.ExecuteTick == executeTick && CoveredBy(item.Target, targets)));
            foreach (var address in targets)
            {
                var estimate = GetEstimate(address);
                var removedTicks = estimate.SentTicks.RemoveAll(tick => tick == executeTick);
                if (removedTicks > 0 && estimate.ReportedCount > 0)
                {
                    // The device tombstone removes the reservation, so the
                    // reported depth no longer includes it.
                    estimate.ReportedCount--;
                }
                WriteEstimate(address, estimate);
            }
        }
        return removed;
    }

    /// <summary>
    /// Rebuilds the estimate from a reservation that was already marked sent
    /// before a server restart.  Without this the dispatcher assumes an empty
    /// device queue and can overfill it after recovery.
    /// </summary>
    public void SeedSentReservations(
        IReadOnlyList<(TargetSpec Target, ulong ExecuteTick)> reservations)
    {
        lock (_gate)
        {
            var estimates = new Dictionary<ushort, QueueEstimate>();
            foreach (var (target, executeTick) in reservations)
            {
                foreach (var balloon in target.Balloons)
                {
                    if (!estimates.TryGetValue(balloon.MeshAddress, out var estimate))
                    {
                        estimate = GetEstimate(balloon.MeshAddress);
                        estimates[balloon.MeshAddress] = estimate;
                    }
                    if (!estimate.SentTicks.Contains(executeTick))
                    {
                        estimate.SentTicks.Add(executeTick);
                    }
                }
            }
            foreach (var pair in estimates)
            {
                WriteEstimate(pair.Key, pair.Value);
            }
        }
    }

    public void NoteQueueStatus(ushort address, ushort count)
    {
        lock (_gate)
        {
            var estimate = GetEstimate(address);
            // Keep the reported depth as-is.  Firmware sends it periodically,
            // and EffectiveCount decays it as known reservations execute so it
            // cannot pin the estimate at a stale value.
            estimate.ReportedCount = count;
            estimate.ReportedAtTick = BalloonProtocol.NowTick40(_time);
            estimate.LastDecayTick = estimate.ReportedAtTick;
            WriteEstimate(address, estimate);
        }
    }

    public int GetEstimatedCount(ushort address)
    {
        lock (_gate)
        {
            return GetEstimate(address).EffectiveCount(BalloonProtocol.NowTick40(_time));
        }
    }

    public async Task<int> TickAsync(CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow();
        var nowTick = BalloonProtocol.NowTick40(_time);
        var sent = 0;
        List<PendingReservation> sendList = [];
        List<PendingReservation> expired = [];

        lock (_gate)
        {
            var selectedCounts = new Dictionary<ushort, int>();
            foreach (var item in _pending)
            {
                if (item.ExecuteAt <= now)
                {
                    expired.Add(item);
                    continue;
                }

                var addresses = item.Target.Balloons.Select(balloon => balloon.MeshAddress).ToArray();
                if (addresses.Length == 0)
                {
                    sendList.Add(item);
                    continue;
                }

                if (addresses.All(address =>
                        GetEstimate(address).EffectiveCount(nowTick) + selectedCounts.GetValueOrDefault(address) < TargetQueueDepth))
                {
                    sendList.Add(item);
                    foreach (var address in addresses)
                    {
                        selectedCounts[address] = selectedCounts.GetValueOrDefault(address) + 1;
                    }
                }
            }

            foreach (var item in sendList)
            {
                _pending.Remove(item);
            }
            foreach (var item in expired)
            {
                _pending.Remove(item);
            }
        }

        foreach (var item in expired)
        {
            if (item.ShowStepId is { } showStepId)
            {
                _store.UpdateShowStepStatus(showStepId, "expired");
            }
            _logger.LogWarning("reservation for {Target} at {ExecuteAt:O} expired before a gateway was available",
                item.Target.Description, item.ExecuteAt);
        }

        foreach (var item in sendList)
        {
            var command = new DownlinkCommand(
                item.Target.Destination,
                BalloonProtocol.VendorOpcode(item.Opcode),
                item.Payload,
                7);
            var gateways = _gateways.Broadcast(command);
            if (gateways == 0)
            {
                lock (_gate)
                {
                    InsertPending(item);
                }
                continue;
            }

            lock (_gate)
            {
                foreach (var balloon in item.Target.Balloons)
                {
                    var estimate = GetEstimate(balloon.MeshAddress);
                    if (!estimate.SentTicks.Contains(item.ExecuteTick))
                    {
                        estimate.SentTicks.Add(item.ExecuteTick);
                    }
                    WriteEstimate(balloon.MeshAddress, estimate);
                }
            }
            sent++;
            if (item.ShowStepId is { } showStepId)
            {
                _store.UpdateShowStepStatus(showStepId, "sent");
            }
            _logger.LogInformation("sent {Kind} for {Target} tick={Tick} gateways={Gateways}",
                item.Opcode == BalloonProtocol.OpLedSet ? "LED_SET" : "command",
                item.Target.Description, item.ExecuteTick, gateways);
        }

        if (sent > 0)
        {
            await _notifier.CommandSentAsync(new CommandResultDto(
                true,
                $"Dispatched {sent} reservation(s).",
                _gateways.Snapshot().Count,
                sendList.Sum(item => item.Target.Balloons.Count),
                null,
                null));
        }
        return sent;
    }

    /// <summary>
    /// True only when the cancel/reset addresses cover the reservation's whole
    /// target.  A unicast cancel must not delete an all-balloon reservation.
    /// </summary>
    private static bool CoveredBy(TargetSpec target, HashSet<ushort> addresses) =>
        target.Balloons.Count > 0 &&
        target.Balloons.All(balloon => addresses.Contains(balloon.MeshAddress));

    private List<long> RemovePending(Func<PendingReservation, bool> predicate)
    {
        var removed = new List<long>();
        for (var i = _pending.Count - 1; i >= 0; i--)
        {
            var item = _pending[i];
            if (!predicate(item))
            {
                continue;
            }
            _pending.RemoveAt(i);
            if (item.ShowStepId is { } showStepId)
            {
                removed.Add(showStepId);
            }
        }
        return removed;
    }

    private QueueEstimate GetEstimate(ushort address) =>
        _estimates.TryGetValue(address, out var estimate) ? estimate : new QueueEstimate();

    private void InsertPending(PendingReservation reservation)
    {
        var index = _pending.Count;
        while (index > 0 && _pending[index - 1].ExecuteAt > reservation.ExecuteAt) index--;
        _pending.Insert(index, reservation);
    }

    private void WriteEstimate(ushort address, QueueEstimate estimate)
    {
        _estimates[address] = estimate;
        _fleet.UpdateEstimatedQueue(address, estimate.EffectiveCount(BalloonProtocol.NowTick40(_time)));
    }

    private sealed class QueueEstimate
    {
        public List<ulong> SentTicks { get; } = [];
        public int ReportedCount { get; set; }
        public ulong ReportedAtTick { get; set; }
        public ulong LastDecayTick { get; set; }
        public ulong LastCutoff { get; set; }

        public int EffectiveCount(ulong nowTick)
        {
            if (ReportedCount > 0 && ReportedAtTick > 0)
            {
                var from = Math.Max(LastDecayTick, ReportedAtTick);
                if (nowTick > from)
                {
                    var executed = SentTicks.Count(tick => tick > from && tick <= nowTick);
                    ReportedCount = Math.Max(0, ReportedCount - executed);
                    LastDecayTick = nowTick;
                }
            }
            SentTicks.RemoveAll(tick => tick + 300 <= nowTick); // 3 s grace
            // Executed ticks are kept briefly so the decay above can see them,
            // but they are no longer part of the device's future queue.
            var pending = SentTicks.Count(tick => tick > nowTick);
            return Math.Clamp(Math.Max(ReportedCount, pending), 0, HardQueueDepth);
        }
    }

    private sealed record PendingReservation(
        TargetSpec Target,
        byte Opcode,
        byte[] Payload,
        ulong ExecuteTick,
        DateTimeOffset ExecuteAt,
        byte R,
        byte G,
        byte B,
        long? ShowStepId);
}

public sealed class LedDispatchWorker : BackgroundService
{
    private readonly LedDispatchService _dispatch;
    private readonly ILogger<LedDispatchWorker> _logger;

    public LedDispatchWorker(LedDispatchService dispatch, ILogger<LedDispatchWorker> logger)
    {
        _dispatch = dispatch;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await _dispatch.TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "LED dispatch tick failed");
            }
        }
    }
}
