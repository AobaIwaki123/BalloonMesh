using BalloonMesh.DebugTool.Core;
using BalloonMesh.Protocol;

namespace BalloonMesh.DebugTool.Cli;

/// <summary>
/// Parses a compact event description for <c>gateway run --event</c>, for
/// example <c>ftm:anchor=1,distance=3000,batch=1</c> or
/// <c>neighbor:batch=1,index=0,neighbor=0x0102@-45</c>.
/// </summary>
public static class EventSpec
{
    public static GatewayEventInfo Parse(string spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
        {
            throw new FormatException("Event spec is empty.");
        }
        var separator = spec.IndexOf(':');
        var kind = (separator < 0 ? spec : spec[..separator]).Trim().ToLowerInvariant();
        var rest = separator < 0 ? string.Empty : spec[(separator + 1)..];
        var values = ParsePairs(rest);

        var src = Get(values, "src") is { } srcText
            ? ValueParsers.ParseUShort(srcText, "src")
            : (ushort)0x0101;
        var dst = Get(values, "dst") is { } dstText
            ? ValueParsers.ParseUShort(dstText, "dst")
            : (ushort)0xC011;
        var rssi = Get(values, "rssi") is { } rssiText
            ? ValueParsers.ParseSByte(rssiText, "rssi")
            : (sbyte)-45;
        var ttl = Get(values, "ttl") is { } ttlText
            ? ValueParsers.ParseInt32(ttlText, "ttl", 0, 31)
            : 3;

        EncodedMessage encoded;
        switch (kind)
        {
            case "raw":
                EnsureOnly(values, "src", "dst", "rssi", "ttl", "opcode", "payload");
                var opcode = ValueParsers.ParseUInt32(Require(values, "opcode"), "opcode");
                return new GatewayEventInfo(src, dst, opcode,
                    ProtocolInspector.NormalizePayloadHex(Get(values, "payload")), rssi, ttl);

            case "ledset":
                EnsureOnly(values, "src", "dst", "rssi", "ttl", "r", "g", "b", "color", "at");
                var scheduleColor = Color(values);
                encoded = ProtocolInspector.EncodeLedSet(
                    ValueParsers.ParseTimeOr(Get(values, "at"),
                        DateTimeOffset.UtcNow.AddSeconds(10), "at"),
                    scheduleColor.R, scheduleColor.G, scheduleColor.B);
                break;

            case "lednow":
                EnsureOnly(values, "src", "dst", "rssi", "ttl", "r", "g", "b", "color", "at");
                var issued = ValueParsers.ParseTimeOr(Get(values, "at"),
                    DateTimeOffset.UtcNow, "at");
                var nowColor = Color(values);
                encoded = ProtocolInspector.EncodeLedSetNow(issued, nowColor.R, nowColor.G, nowColor.B);
                break;

            case "ledcancel":
                EnsureOnly(values, "src", "dst", "rssi", "ttl", "at");
                encoded = ProtocolInspector.EncodeLedCancel(
                    ValueParsers.ParseTime(Require(values, "at"), "at"));
                break;

            case "ledreset":
                EnsureOnly(values, "src", "dst", "rssi", "ttl", "at", "cutoff");
                encoded = ProtocolInspector.EncodeLedReset(
                    ValueParsers.ParseTimeOr(Get(values, "cutoff") ?? Get(values, "at"),
                        DateTimeOffset.UtcNow, "cutoff"));
                break;

            case "ftm":
                EnsureOnly(values, "src", "dst", "rssi", "ttl", "anchor", "distance", "batch", "timestamp");
                encoded = ProtocolInspector.EncodeFtmReport(
                    ValueParsers.ParseByte(Get(values, "anchor") ?? "1", "anchor"),
                    ValueParsers.ParseUShort(Require(values, "distance"), "distance"),
                    Get(values, "timestamp") is { } timestamp
                        ? ValueParsers.ParseUInt32(timestamp, "timestamp")
                        : ProtocolInspector.NowTimestamp32(),
                    ValueParsers.ParseByte(Get(values, "batch") ?? "1", "batch"));
                break;

            case "queue":
                EnsureOnly(values, "src", "dst", "rssi", "ttl", "kind", "count");
                encoded = ProtocolInspector.EncodeQueueStatus(
                    ParseQueueKind(Get(values, "kind") ?? "nearfull"),
                    ValueParsers.ParseUShort(Require(values, "count"), "count"));
                break;

            case "neighbor":
                EnsureOnly(values, "src", "dst", "rssi", "ttl", "batch", "index", "neighbor");
                encoded = ProtocolInspector.EncodeNeighborReport(
                    ValueParsers.ParseByte(Get(values, "batch") ?? "1", "batch"),
                    ValueParsers.ParseByte(Get(values, "index") ?? "0", "index"),
                    ValueParsers.ParseNeighbors(All(values, "neighbor")));
                break;

            case "time":
                EnsureOnly(values, "src", "dst", "rssi", "ttl", "at");
                encoded = ProtocolInspector.EncodeTimeReport(
                    ValueParsers.ParseTimeOr(Get(values, "at"), DateTimeOffset.UtcNow, "at"));
                break;

            case "neighbor_request":
                EnsureOnly(values, "src", "dst", "rssi", "ttl");
                encoded = ProtocolInspector.EncodeRaw(BalloonProtocol.OpNeighborRequest, string.Empty);
                break;

            case "neighbor_probe":
                EnsureOnly(values, "src", "dst", "rssi", "ttl");
                encoded = ProtocolInspector.EncodeRaw(BalloonProtocol.OpNeighborProbe, string.Empty);
                break;

            case "time_request":
                EnsureOnly(values, "src", "dst", "rssi", "ttl");
                encoded = ProtocolInspector.EncodeRaw(BalloonProtocol.OpTimeRequest, string.Empty);
                break;

            default:
                throw new FormatException(
                    $"Unknown event kind '{kind}'. Use raw, ledset, lednow, ledcancel, ledreset, " +
                    "ftm, queue, neighbor, time, neighbor_request, neighbor_probe or time_request.");
        }

        return new GatewayEventInfo(src, dst, encoded.Opcode, encoded.PayloadHex, rssi, ttl);
    }

    private static Dictionary<string, List<string>> ParsePairs(string text)
    {
        var values = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(text))
        {
            return values;
        }
        foreach (var part in text.Split(','))
        {
            var item = part.Trim();
            if (item.Length == 0)
            {
                continue;
            }
            var separator = item.IndexOf('=');
            if (separator <= 0)
            {
                throw new FormatException($"Event field '{item}' must be key=value.");
            }
            var key = item[..separator].Trim().ToLowerInvariant();
            var value = item[(separator + 1)..].Trim();
            if (!values.TryGetValue(key, out var list))
            {
                list = [];
                values[key] = list;
            }
            list.Add(value);
        }
        return values;
    }

    private static void EnsureOnly(Dictionary<string, List<string>> values, params string[] allowed)
    {
        var allowedSet = new HashSet<string>(allowed, StringComparer.Ordinal);
        foreach (var key in values.Keys)
        {
            if (!allowedSet.Contains(key))
            {
                throw new FormatException($"Unknown event field '{key}'.");
            }
        }
    }

    private static string? Get(Dictionary<string, List<string>> values, string key) =>
        values.TryGetValue(key, out var list) ? list[^1] : null;

    private static IReadOnlyList<string> All(Dictionary<string, List<string>> values, string key) =>
        values.TryGetValue(key, out var list) ? list : [];

    private static string Require(Dictionary<string, List<string>> values, string key) =>
        Get(values, key) is { Length: > 0 } value
            ? value
            : throw new FormatException($"Event field '{key}' is required.");

    private static (byte R, byte G, byte B) Color(Dictionary<string, List<string>> values)
    {
        if (Get(values, "color") is { } color)
        {
            return ValueParsers.ParseColor(color);
        }
        if (Get(values, "r") is null && Get(values, "g") is null && Get(values, "b") is null)
        {
            throw new FormatException("Event color requires 'color=#RRGGBB' or 'r', 'g' and 'b'.");
        }
        return (
            ValueParsers.ParseByte(Require(values, "r"), "r"),
            ValueParsers.ParseByte(Require(values, "g"), "g"),
            ValueParsers.ParseByte(Require(values, "b"), "b"));
    }

    private static byte ParseQueueKind(string text) => text.Trim().ToLowerInvariant() switch
    {
        "nearfull" or "near-full" or "near_full" => ProtocolInspector.QueueStatusNearFull,
        "recovered" => ProtocolInspector.QueueStatusRecovered,
        _ => throw new FormatException(
            $"Unknown queue kind '{text}'. Use nearfull or recovered.")
    };
}
