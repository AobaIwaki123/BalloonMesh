using SvelteSharp.Models;

namespace BalloonMesh.Server.Models;

[SvelteModel(Name = "HomePage")]
public sealed record HomePageModel(string Title, string Version, string Subtitle);

public sealed record BalloonDto(
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

public sealed record AnchorDto(
    byte AnchorId,
    string Name,
    double Lat,
    double Lon,
    double AltM,
    bool TimeValid,
    bool PositionValid);

public sealed record GatewayDto(
    string Id,
    bool WebSocket,
    string? Remote,
    string? Firmware,
    DateTimeOffset LastSeen,
    int PendingCommands);

public sealed record GroupDto(int Id, string Name, ushort MeshGroupAddress, int BalloonCount);

public sealed record StateDto(
    DateTimeOffset ServerTime,
    string Version,
    bool SimulationRunning,
    IReadOnlyList<BalloonDto> Balloons,
    IReadOnlyList<AnchorDto> Anchors,
    IReadOnlyList<GatewayDto> Gateways,
    IReadOnlyList<GroupDto> Groups);

public sealed record CommandResultDto(
    bool Accepted,
    string Message,
    int Gateways,
    int Balloons,
    string? Opcode,
    string? Payload);

public sealed record LedSetRequest(
    string TargetKind,
    string Target,
    DateTimeOffset ExecuteAt,
    byte R,
    byte G,
    byte B,
    string? Name = null);

public sealed record LedNowRequest(
    string TargetKind,
    string Target,
    byte R,
    byte G,
    byte B);

public sealed record LedCancelRequest(
    string TargetKind,
    string Target,
    DateTimeOffset ExecuteAt);

public sealed record LedResetRequest(
    string TargetKind,
    string Target,
    DateTimeOffset? CutoffAt,
    bool SetNow,
    byte R = 0,
    byte G = 0,
    byte B = 0);

public sealed record InitialPositionRequest(double Lat, double Lon);

public sealed record CreateGroupRequest(string Name);

public sealed record AnchorUpdateRequest(
    string? Name,
    double Lat,
    double Lon,
    double AltM,
    bool TimeValid = true,
    bool PositionValid = true);

public sealed record AssignGroupRequest(int GroupId);

public sealed record GatewayEventDto(
    ushort Src,
    ushort Dst,
    uint Opcode,
    string Payload,
    int Rssi,
    int Ttl,
    long? ReceivedMs);

public sealed record GatewayEventsRequest(
    string GatewayId,
    string? Firmware,
    IReadOnlyList<GatewayEventDto> Events);

public sealed record GatewayCommandDto(ushort Dst, uint Opcode, string Payload, int Ttl);

public sealed record GatewayCommandsRequest(IReadOnlyList<GatewayCommandDto> Commands);

public sealed record DownlinkCommand(ushort Dst, uint Opcode, byte[] Payload, int Ttl)
{
    public GatewayCommandDto ToDto() => new(Dst, Opcode, Convert.ToHexString(Payload), Ttl);
}

public sealed record MeshEnvelope(
    string GatewayId,
    ushort Src,
    ushort Dst,
    uint Opcode,
    byte[] Payload,
    int Rssi,
    int Ttl,
    DateTimeOffset ReceivedAt);
