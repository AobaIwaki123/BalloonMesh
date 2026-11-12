namespace BalloonMesh.DebugTool.Core;

public sealed record ServerHealth(string Status, string Version, DateTimeOffset Time);

public sealed record BalloonInfo(
    ushort MeshAddress,
    string Name,
    int GroupId,
    string GroupName,
    double? Lat,
    double? Lon,
    double? Confidence,
    byte R,
    byte G,
    byte B,
    int QueueCount,
    int EstimatedQueueCount,
    string? LastMethod,
    bool Online);

public sealed record AnchorInfo(
    byte AnchorId,
    string Name,
    double Lat,
    double Lon,
    double AltM,
    bool TimeValid,
    bool PositionValid);

public sealed record GatewayInfo(
    string Id,
    bool WebSocket,
    string? Remote,
    string? Firmware,
    DateTimeOffset LastSeen,
    int PendingCommands,
    bool Provisioned,
    int SpoolPending);

public sealed record GroupInfo(int Id, string Name, ushort MeshGroupAddress, int BalloonCount);

public sealed record FleetState(
    DateTimeOffset ServerTime,
    string Version,
    bool SimulationRunning,
    IReadOnlyList<BalloonInfo> Balloons,
    IReadOnlyList<AnchorInfo> Anchors,
    IReadOnlyList<GatewayInfo> Gateways,
    IReadOnlyList<GroupInfo> Groups);

public sealed record ShowStepInfo(
    long Id,
    string Name,
    string TargetKind,
    string Target,
    DateTimeOffset ExecuteAt,
    byte R,
    byte G,
    byte B,
    string Status);

public sealed record CommandResult(
    bool Accepted,
    string Message,
    int Gateways,
    int Balloons,
    string? Opcode,
    string? Payload);

public sealed record AcceptedEvents(int Accepted);

public sealed record LedSetCommand(
    string TargetKind,
    string Target,
    DateTimeOffset ExecuteAt,
    byte R,
    byte G,
    byte B,
    string? Name = null);

public sealed record LedNowCommand(
    string TargetKind,
    string Target,
    byte R,
    byte G,
    byte B);

public sealed record LedCancelCommand(
    string TargetKind,
    string Target,
    DateTimeOffset ExecuteAt);

public sealed record LedResetCommand(
    string TargetKind,
    string Target,
    DateTimeOffset? CutoffAt,
    bool SetNow,
    byte R = 0,
    byte G = 0,
    byte B = 0);

/// <summary>One uplink mesh message as the server's gateway endpoints expect it.</summary>
public sealed record GatewayEventInfo(
    ushort Src,
    ushort Dst,
    uint Opcode,
    string Payload,
    int Rssi,
    int Ttl,
    long? ReceivedMs = null,
    long? AgeMs = null);

/// <summary>One downlink mesh command received from the server.</summary>
public sealed record GatewayCommandInfo(ushort Dst, uint Opcode, string Payload, int Ttl);
