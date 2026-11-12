using BalloonMesh.DebugTool.Core;
using BalloonMesh.Protocol;

namespace BalloonMesh.DebugTool.Cli;

/// <summary>
/// Builds Protocol B vendor messages for <c>protocol encode</c>. The wire
/// layouts live in <see cref="BalloonProtocol"/> and are shared with the
/// server, so this class only maps CLI options onto the typed encoders.
/// </summary>
public static class ProtocolCommandBuilder
{
    public static EncodedMessage Build(string op, CliArguments arguments)
    {
        var known = ProtocolInspector.KnownOps.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, op, StringComparison.OrdinalIgnoreCase));
        if (known is null)
        {
            if (!TryParseRawOp(op, out var rawOp))
            {
                throw new CliUsageException(
                    $"Unknown op '{op}'. Known ops: " +
                    string.Join(", ", ProtocolInspector.KnownOps.Select(candidate => candidate.Name)) +
                    ". A raw vendor opcode (0x00..0x3F) is also accepted.");
            }
            return ProtocolInspector.EncodeRaw(rawOp, arguments.Value("payload"));
        }

        switch (known.Name)
        {
            case "LED_SET":
            {
                var color = Color(arguments);
                return ProtocolInspector.EncodeLedSet(
                    Time(arguments.Value("at"), DateTimeOffset.UtcNow.AddSeconds(10), "at"),
                    color.R, color.G, color.B);
            }
            case "LED_CANCEL":
                return ProtocolInspector.EncodeLedCancel(
                    ValueParsers.ParseTime(arguments.RequireValue("at"), "at"));
            case "LED_RESET_ALL":
                return ProtocolInspector.EncodeLedReset(
                    Time(arguments.Value("cutoff") ?? arguments.Value("at"),
                        DateTimeOffset.UtcNow, "cutoff"));
            case "LED_SET_NOW":
            {
                var color = Color(arguments);
                return ProtocolInspector.EncodeLedSetNow(
                    Time(arguments.Value("at"), DateTimeOffset.UtcNow, "at"),
                    color.R, color.G, color.B);
            }
            case "LED_RESET_AND_SET":
            {
                var color = Color(arguments);
                return ProtocolInspector.EncodeLedResetAndSet(
                    Time(arguments.Value("cutoff") ?? arguments.Value("at"),
                        DateTimeOffset.UtcNow, "cutoff"),
                    color.R, color.G, color.B);
            }
            case "FTM_REPORT":
                return ProtocolInspector.EncodeFtmReport(
                    ValueParsers.ParseByte(arguments.Value("anchor") ?? "1", "anchor"),
                    ValueParsers.ParseUShort(arguments.RequireValue("distance"), "distance"),
                    arguments.Value("timestamp") is { } timestamp
                        ? ValueParsers.ParseUInt32(timestamp, "timestamp")
                        : ProtocolInspector.NowTimestamp32(),
                    ValueParsers.ParseByte(arguments.Value("batch") ?? "1", "batch"));
            case "QUEUE_STATUS":
                return ProtocolInspector.EncodeQueueStatus(
                    ParseQueueKind(arguments.Value("kind") ?? "nearfull"),
                    ValueParsers.ParseUShort(arguments.RequireValue("count"), "count"));
            case "NEIGHBOR_REPORT":
                return ProtocolInspector.EncodeNeighborReport(
                    ValueParsers.ParseByte(arguments.Value("batch") ?? "1", "batch"),
                    ValueParsers.ParseByte(arguments.Value("index") ?? "0", "index"),
                    ValueParsers.ParseNeighbors(arguments.All("neighbor")));
            case "NEIGHBOR_REQUEST":
                return ProtocolInspector.EncodeRaw(BalloonProtocol.OpNeighborRequest, string.Empty);
            case "NEIGHBOR_PROBE":
                return ProtocolInspector.EncodeRaw(BalloonProtocol.OpNeighborProbe, string.Empty);
            case "TIME_REQUEST":
                return ProtocolInspector.EncodeRaw(BalloonProtocol.OpTimeRequest, string.Empty);
            case "TIME_REPORT":
                return ProtocolInspector.EncodeTimeReport(
                    Time(arguments.Value("at"), DateTimeOffset.UtcNow, "at"));
            default:
                throw new CliUsageException($"Encoding {known.Name} is not supported by the CLI.");
        }
    }

    private static DateTimeOffset Time(string? text, DateTimeOffset defaultValue, string label) =>
        ValueParsers.ParseTimeOr(text, defaultValue, label);

    private static (byte R, byte G, byte B) Color(CliArguments arguments)
    {
        if (arguments.Value("color") is { } color)
        {
            return ValueParsers.ParseColor(color);
        }
        if (arguments.Value("r") is null && arguments.Value("g") is null && arguments.Value("b") is null)
        {
            throw new CliUsageException("Option '--color' is required (use #RRGGBB or r,g,b).");
        }
        return (
            ValueParsers.ParseByte(arguments.RequireValue("r"), "r"),
            ValueParsers.ParseByte(arguments.RequireValue("g"), "g"),
            ValueParsers.ParseByte(arguments.RequireValue("b"), "b"));
    }

    private static bool TryParseRawOp(string text, out byte op)
    {
        op = 0;
        var trimmed = text.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return byte.TryParse(trimmed[2..], System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out op) && op <= 0x3F;
        }
        return byte.TryParse(trimmed, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out op) && op <= 0x3F;
    }

    private static byte ParseQueueKind(string text) => text.Trim().ToLowerInvariant() switch
    {
        "nearfull" or "near-full" or "near_full" => ProtocolInspector.QueueStatusNearFull,
        "recovered" => ProtocolInspector.QueueStatusRecovered,
        _ => throw new CliUsageException($"Unknown queue kind '{text}'. Use nearfull or recovered.")
    };
}
