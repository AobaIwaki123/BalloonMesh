using BalloonMesh.Server.Hubs;
using BalloonMesh.Server.Models;
using BalloonMesh.Server.Protocol;

namespace BalloonMesh.Server.Services;

public sealed class MeshReportRouter
{
    private readonly FleetService _fleet;
    private readonly FtmAggregator _aggregator;
    private readonly NeighborStore _neighbors;
    private readonly LedDispatchService _dispatch;
    private readonly IMonitorEvents _notifier;
    private readonly ILogger<MeshReportRouter> _logger;

    public MeshReportRouter(
        FleetService fleet,
        FtmAggregator aggregator,
        NeighborStore neighbors,
        LedDispatchService dispatch,
        IMonitorEvents notifier,
        ILogger<MeshReportRouter> logger)
    {
        _fleet = fleet;
        _aggregator = aggregator;
        _neighbors = neighbors;
        _dispatch = dispatch;
        _notifier = notifier;
        _logger = logger;
    }

    public int Handle(GatewayEventDto message, string gatewayId)
    {
        if (message.Payload is null)
        {
            _logger.LogWarning("Gateway {Gateway} sent an event without a payload", gatewayId);
            return 0;
        }
        byte[] payload;
        try
        {
            payload = BalloonProtocol.FromHex(message.Payload);
        }
        catch (FormatException)
        {
            _logger.LogWarning("Gateway {Gateway} sent invalid hex payload", gatewayId);
            return 0;
        }
        return Handle(new MeshEnvelope(
            gatewayId,
            message.Src,
            message.Dst,
            message.Opcode,
            payload,
            message.Rssi,
            message.Ttl,
            DateTimeOffset.UtcNow));
    }

    public int Handle(MeshEnvelope envelope)
    {
        if (!BalloonProtocol.TryGetMessageOp(envelope.Opcode, out var op))
        {
            _logger.LogDebug("Ignoring non-vendor opcode 0x{Opcode:X6}", envelope.Opcode);
            return 0;
        }

        switch (op)
        {
            case BalloonProtocol.OpFtmReport:
                if (!BalloonProtocol.TryDecodeFtmReport(envelope.Payload, out var ftm))
                {
                    return 0;
                }
                _aggregator.AddReport(envelope.Src, ftm, envelope.GatewayId, envelope.ReceivedAt);
                return 1;

            case BalloonProtocol.OpNeighborReport:
                if (!BalloonProtocol.TryDecodeNeighborReport(envelope.Payload, out var neighbor))
                {
                    return 0;
                }
                _neighbors.Add(envelope.Src, neighbor.BatchId, neighbor.Entries, envelope.ReceivedAt);
                return 1;

            case BalloonProtocol.OpQueueStatus:
                if (!BalloonProtocol.TryDecodeQueueStatus(envelope.Payload, out var queue))
                {
                    return 0;
                }
                var balloon = _fleet.ApplyQueueStatus(envelope.Src, queue.QueueCount);
                _dispatch.NoteQueueStatus(envelope.Src, queue.QueueCount);
                _ = _notifier.QueueUpdatedAsync(balloon);
                _logger.LogInformation("queue status {Source:X4}: kind={Kind} count={Count}",
                    envelope.Src, queue.Kind, queue.QueueCount);
                return 1;

            default:
                _logger.LogDebug("Ignoring uplink opcode 0x{Opcode:X2} from {Source:X4}", op, envelope.Src);
                return 0;
        }
    }
}
