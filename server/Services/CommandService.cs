using BalloonMesh.Server.Data;
using BalloonMesh.Server.Hubs;
using BalloonMesh.Server.Models;
using BalloonMesh.Server.Protocol;

namespace BalloonMesh.Server.Services;

public sealed class CommandService
{
    private readonly FleetService _fleet;
    private readonly LedDispatchService _dispatch;
    private readonly GatewayRegistry _gateways;
    private readonly DataStore _store;
    private readonly IMonitorEvents _notifier;
    private readonly TimeProvider _time;
    private readonly ILogger<CommandService> _logger;

    public CommandService(
        FleetService fleet,
        LedDispatchService dispatch,
        GatewayRegistry gateways,
        DataStore store,
        IMonitorEvents notifier,
        TimeProvider time,
        ILogger<CommandService> logger)
    {
        _fleet = fleet;
        _dispatch = dispatch;
        _gateways = gateways;
        _store = store;
        _notifier = notifier;
        _time = time;
        _logger = logger;
    }

    public async Task<CommandResultDto> ScheduleAsync(LedSetRequest request, CancellationToken cancellationToken)
    {
        var target = _fleet.ResolveTarget(request.TargetKind, request.Target);
        var now = _time.GetUtcNow();
        var executeAt = request.ExecuteAt.ToUniversalTime();
        if (executeAt <= now)
        {
            return new CommandResultDto(false, "executeAt must be in the future.", 0, target.Balloons.Count, null, null);
        }

        var tick = BalloonProtocol.Tick40FromUnixMs(executeAt.ToUnixTimeMilliseconds());
        var showStepId = _store.AddShowStep(
            string.IsNullOrWhiteSpace(request.Name) ? "LED reservation" : request.Name,
            request.TargetKind,
            request.Target,
            executeAt,
            request.R,
            request.G,
            request.B,
            "queued");
        if (!_dispatch.EnqueueScheduled(target, tick, executeAt, request.R, request.G, request.B, showStepId))
        {
            _store.UpdateShowStepStatus(showStepId, "rejected");
            return new CommandResultDto(false, "The reservation is already in the past.", 0, target.Balloons.Count, null, null);
        }

        var payload = BalloonProtocol.EncodeLedSet(tick, request.R, request.G, request.B);
        var result = new CommandResultDto(
            true,
            $"Reservation accepted for {target.Description}.",
            _gateways.Snapshot().Count,
            target.Balloons.Count,
            $"0x{BalloonProtocol.VendorOpcode(BalloonProtocol.OpLedSet):X6}",
            Convert.ToHexString(payload));
        await _notifier.CommandSentAsync(result);
        return result;
    }

    public async Task<CommandResultDto> SetNowAsync(LedNowRequest request, CancellationToken cancellationToken)
    {
        var target = _fleet.ResolveTarget(request.TargetKind, request.Target);
        var addresses = target.Balloons.Select(balloon => balloon.MeshAddress).ToArray();
        var tick = _dispatch.NextIssuedTick(addresses);
        var payload = BalloonProtocol.EncodeLedSetNow(tick, request.R, request.G, request.B);
        var gateways = _dispatch.SendImmediate(target, BalloonProtocol.OpLedSetNow, payload);

        foreach (var balloon in target.Balloons)
        {
            var updated = _fleet.ApplyColor(balloon.MeshAddress, request.R, request.G, request.B);
            _ = _notifier.BalloonUpdatedAsync(updated);
        }

        var result = new CommandResultDto(
            true,
            $"Immediate colour sent to {target.Description}.",
            gateways,
            target.Balloons.Count,
            $"0x{BalloonProtocol.VendorOpcode(BalloonProtocol.OpLedSetNow):X6}",
            Convert.ToHexString(payload));
        await _notifier.CommandSentAsync(result);
        return result;
    }

    public async Task<CommandResultDto> CancelAsync(LedCancelRequest request, CancellationToken cancellationToken)
    {
        var target = _fleet.ResolveTarget(request.TargetKind, request.Target);
        var executeAt = request.ExecuteAt.ToUniversalTime();
        var tick = BalloonProtocol.Tick40FromUnixMs(executeAt.ToUnixTimeMilliseconds());
        var payload = BalloonProtocol.EncodeLedCancel(tick);
        var gateways = _dispatch.SendImmediate(target, BalloonProtocol.OpLedCancel, payload);
        _dispatch.NoteCancel(target.Balloons.Select(balloon => balloon.MeshAddress), tick);

        var result = new CommandResultDto(
            true,
            $"Cancellation sent for {target.Description}.",
            gateways,
            target.Balloons.Count,
            $"0x{BalloonProtocol.VendorOpcode(BalloonProtocol.OpLedCancel):X6}",
            Convert.ToHexString(payload));
        await _notifier.CommandSentAsync(result);
        return result;
    }

    public async Task<CommandResultDto?> CancelShowStepAsync(long id, CancellationToken cancellationToken)
    {
        var step = _store.GetShowStep(id);
        if (step is null)
        {
            return null;
        }
        var result = await CancelAsync(
            new LedCancelRequest(step.TargetKind, step.Target, step.ExecuteAt), cancellationToken);
        _store.UpdateShowStepStatus(id, "canceled");
        return result with { Message = $"Cancelled show step {id}. {result.Message}" };
    }

    public async Task<CommandResultDto> ResetAsync(LedResetRequest request, CancellationToken cancellationToken)
    {
        var target = _fleet.ResolveTarget(request.TargetKind, request.Target);
        var addresses = target.Balloons.Select(balloon => balloon.MeshAddress).ToArray();
        var requested = request.CutoffAt is { } cutoff
            ? BalloonProtocol.Tick40FromUnixMs(cutoff.ToUniversalTime().ToUnixTimeMilliseconds())
            : BalloonProtocol.NowTick40(_time);
        var cutoffTick = _dispatch.NextCutoffTick(addresses, requested);
        var opcode = request.SetNow
            ? BalloonProtocol.OpLedResetAndSet
            : BalloonProtocol.OpLedResetAll;
        var payload = request.SetNow
            ? BalloonProtocol.EncodeLedResetAndSet(cutoffTick, request.R, request.G, request.B)
            : BalloonProtocol.EncodeLedReset(cutoffTick);
        var gateways = _dispatch.SendImmediate(target, opcode, payload);
        _dispatch.NoteCutoff(addresses, cutoffTick);

        if (request.SetNow)
        {
            foreach (var balloon in target.Balloons)
            {
                var updated = _fleet.ApplyColor(balloon.MeshAddress, request.R, request.G, request.B);
                _ = _notifier.BalloonUpdatedAsync(updated);
            }
        }

        var result = new CommandResultDto(
            true,
            request.SetNow
                ? $"Reset + immediate colour sent for {target.Description}."
                : $"Reset sent for {target.Description}.",
            gateways,
            target.Balloons.Count,
            $"0x{BalloonProtocol.VendorOpcode(opcode):X6}",
            Convert.ToHexString(payload));
        await _notifier.CommandSentAsync(result);
        return result;
    }
}
