namespace BalloonMesh.DebugTool.Core;

public enum GatewayTransport
{
    WebSocket,
    Http
}

public sealed record GatewayEmulatorOptions(
    Uri ServerUri,
    string GatewayId,
    string? Token = null,
    GatewayTransport Transport = GatewayTransport.WebSocket,
    string? Firmware = "debugtool/1.0",
    bool Provisioned = true,
    int SpoolPending = 0,
    int PollWaitMs = 10_000)
{
    public const int MaxPollWaitMs = 30_000;
    public const int MinHttpPollWaitMs = 100;

    public void Validate()
    {
        _ = DebugApiClient.NormalizeBaseUri(ServerUri);
        if (string.IsNullOrWhiteSpace(GatewayId))
        {
            throw new ArgumentException("Gateway id is required.", nameof(GatewayId));
        }
        if (GatewayId.Length > 128)
        {
            throw new ArgumentException("Gateway id must be 128 characters or fewer.", nameof(GatewayId));
        }
        if (!Enum.IsDefined(Transport))
        {
            throw new ArgumentException("Unknown gateway transport.", nameof(Transport));
        }
        if (SpoolPending < 0)
        {
            throw new ArgumentException("Spool pending must not be negative.", nameof(SpoolPending));
        }
        if (PollWaitMs is < 0 or > MaxPollWaitMs)
        {
            throw new ArgumentException($"Poll wait must be between 0 and {MaxPollWaitMs} ms.",
                nameof(PollWaitMs));
        }
        if (Transport == GatewayTransport.Http && PollWaitMs < MinHttpPollWaitMs)
        {
            throw new ArgumentException(
                $"HTTP polling needs at least {MinHttpPollWaitMs} ms to avoid a busy loop.",
                nameof(PollWaitMs));
        }
    }
}
