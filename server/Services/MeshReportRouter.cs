using BalloonMesh.Server.Hubs;
using BalloonMesh.Server.Models;
using BalloonMesh.Protocol;

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
        var age = Math.Max(0, message.AgeMs ?? 0);
        return Handle(new MeshEnvelope(
            gatewayId,
            message.Src,
            message.Dst,
            message.Opcode,
            payload,
            message.Rssi,
            message.Ttl,
            DateTimeOffset.UtcNow.AddMilliseconds(-age)));
    }

    public int Handle(MeshEnvelope envelope)
    {
        if (!BalloonProtocol.TryGetMessageOp(envelope.Opcode, out var op))
        {
            _logger.LogDebug("Ignoring non-vendor opcode 0x{Opcode:X6}", envelope.Opcode);
            return 0;
        }

        try
        {
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
                    var changed = _neighbors.Add(envelope.Src, neighbor.BatchId,
                        neighbor.Entries, envelope.ReceivedAt);
                    // A neighbor-only batch still has to be computable: when no
                    // FTM report arrived (all anchors unreachable), the first
                    // report opens the batch instead of being dropped.
                    _aggregator.NoteNeighborReport(envelope.Src, neighbor.BatchId,
                        envelope.ReceivedAt, changed);
                    return 1;

                case BalloonProtocol.OpQueueStatus:
                    if (!BalloonProtocol.TryDecodeQueueStatus(envelope.Payload, out var queue))
                    {
                        return 0;
                    }
                    if (_fleet.GetBalloonRecord(envelope.Src) is null)
                    {
                        _logger.LogWarning(
                            "Ignoring QUEUE_STATUS from unknown balloon {Source:X4}",
                            envelope.Src);
                        return 0;
                    }
                    var balloon = _fleet.ApplyQueueStatus(envelope.Src, queue.QueueCount);
                    _dispatch.NoteQueueStatus(envelope.Src, queue.QueueCount);
                    _ = _notifier.QueueUpdatedAsync(balloon);
                    _logger.LogInformation("queue status {Source:X4}: kind={Kind} count={Count}",
                        envelope.Src, queue.Kind, queue.QueueCount);
                    return 1;

                default:
                    _logger.LogDebug("Ignoring uplink opcode 0x{Opcode:X2} from {Source:X4}", op,
                        envelope.Src);
                    return 0;
            }
        }
        catch (Exception exception)
        {
            // One malformed or unknown message must not abort the rest of the
            // batch (HTTP 500) or close the gateway WebSocket.
            _logger.LogWarning(exception,
                "Ignoring opcode 0x{Opcode:X2} from {Source:X4} after handler failure",
                op, envelope.Src);
            return 0;
        }
    }
}
