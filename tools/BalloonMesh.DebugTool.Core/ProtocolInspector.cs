using System.Globalization;
using BalloonMesh.Protocol;

namespace BalloonMesh.DebugTool.Core;

public sealed record ProtocolField(string Name, string Value);

public sealed record ProtocolOpInfo(byte Op, string Name, int PayloadLength, string Description)
{
    public uint Opcode => BalloonProtocol.VendorOpcode(Op);
}

public sealed record DecodedMessage(
    uint Opcode,
    bool IsVendor,
    byte? Op,
    string Name,
    string Summary,
    IReadOnlyList<ProtocolField> Fields,
    IReadOnlyList<string> Warnings)
{
    public bool HasWarnings => Warnings.Count > 0;
}

public sealed record EncodedMessage(byte Op, string Name, uint Opcode, string PayloadHex)
{
    public string OpcodeHex => $"0x{Opcode:X6}";
}

public sealed record NeighborSample(ushort Address, sbyte RssiDbm);

/// <summary>
/// Human-readable encoder/decoder for the Protocol B vendor messages.  The
/// wire layout itself lives in <see cref="BalloonProtocol"/> so the debug tool
/// and the server can never drift apart.
/// </summary>
public static class ProtocolInspector
{
    public const ushort TelemetryGroup = BalloonProtocol.GroupFtmReport;
    public const byte QueueStatusNearFull = BalloonProtocol.OpQueueStatusNearFull;
    public const byte QueueStatusRecovered = BalloonProtocol.OpQueueStatusRecovered;

    public static IReadOnlyList<ProtocolOpInfo> KnownOps { get; } =
    [
        new(BalloonProtocol.OpLedSet, "LED_SET", 8, "Schedule RGB at a 40-bit execute tick."),
        new(BalloonProtocol.OpLedCancel, "LED_CANCEL", 5, "Cancel a reservation by execute tick."),
        new(BalloonProtocol.OpLedResetAll, "LED_RESET_ALL", 5, "Drop reservations at or before the cutoff."),
        new(BalloonProtocol.OpLedSetNow, "LED_SET_NOW", 8, "Apply RGB immediately using an issued tick."),
        new(BalloonProtocol.OpLedResetAndSet, "LED_RESET_AND_SET", 8, "Reset queued reservations and set RGB now."),
        new(BalloonProtocol.OpQueueStatus, "QUEUE_STATUS", 3, "Device LED queue depth notification."),
        new(BalloonProtocol.OpFtmReport, "FTM_REPORT", 8, "Anchor distance measurement."),
        new(BalloonProtocol.OpNeighborRequest, "NEIGHBOR_REQUEST", 0, "Request neighbor probing."),
        new(BalloonProtocol.OpNeighborProbe, "NEIGHBOR_PROBE", 0, "Neighbor probe trigger."),
        new(BalloonProtocol.OpNeighborReport, "NEIGHBOR_REPORT", 8, "Two neighbor RSSI entries per packet."),
        new(BalloonProtocol.OpTimeRequest, "TIME_REQUEST", 0, "Request mesh time fallback."),
        new(BalloonProtocol.OpTimeReport, "TIME_REPORT", 8, "UTC milliseconds, little-endian.")
    ];

    public static DecodedMessage Decode(uint opcode, string? payloadHex)
    {
        var payload = ParseHex(payloadHex);
        var fields = new List<ProtocolField>
        {
            new("Opcode", $"0x{opcode:X6}"),
            new("Payload length", payload.Length.ToString(CultureInfo.InvariantCulture)),
            new("Payload", payload.Length == 0 ? "(empty)" : Convert.ToHexString(payload))
        };
        var warnings = new List<string>();

        if (!BalloonProtocol.TryGetMessageOp(opcode, out var op))
        {
            warnings.Add("Not an Espressif vendor opcode (expected company id 0x02E5).");
            return new DecodedMessage(opcode, false, null, "NON_VENDOR", "Not a vendor message",
                fields, warnings);
        }

        var info = KnownOps.FirstOrDefault(candidate => candidate.Op == op);
        var name = info?.Name ?? $"UNKNOWN_0x{op:X2}";
        fields.Insert(0, new ProtocolField("Op", $"0x{op:X2}"));
        if (info is null)
        {
            warnings.Add("Unknown vendor opcode.");
        }

        var summary = name;
        switch (op)
        {
            case BalloonProtocol.OpLedSet:
                Expect(payload, 8, warnings);
                if (payload.Length >= 5)
                {
                    var tick = BalloonProtocol.DecodeTick40(payload);
                    fields.Add(new ProtocolField("Execute tick", tick.ToString(CultureInfo.InvariantCulture)));
                    fields.Add(new ProtocolField("Execute at (UTC)", FormatTick(tick)));
                    summary = $"{name} at {FormatTick(tick)}";
                }
                if (payload.Length >= 8)
                {
                    fields.Add(new ProtocolField("RGB", FormatRgb(payload[5], payload[6], payload[7])));
                }
                break;

            case BalloonProtocol.OpLedCancel:
            case BalloonProtocol.OpLedResetAll:
                Expect(payload, 5, warnings);
                if (payload.Length >= 5)
                {
                    var tick = BalloonProtocol.DecodeTick40(payload);
                    fields.Add(new ProtocolField("Execute tick", tick.ToString(CultureInfo.InvariantCulture)));
                    fields.Add(new ProtocolField("Execute at (UTC)", FormatTick(tick)));
                    summary = $"{name} at {FormatTick(tick)}";
                }
                break;

            case BalloonProtocol.OpLedSetNow:
            case BalloonProtocol.OpLedResetAndSet:
                Expect(payload, 8, warnings);
                if (payload.Length >= 5)
                {
                    var tick = BalloonProtocol.DecodeTick40(payload);
                    fields.Add(new ProtocolField("Issued/cutoff tick", tick.ToString(CultureInfo.InvariantCulture)));
                    fields.Add(new ProtocolField("Issued/cutoff at (UTC)", FormatTick(tick)));
                    summary = $"{name} at {FormatTick(tick)}";
                }
                if (payload.Length >= 8)
                {
                    fields.Add(new ProtocolField("RGB", FormatRgb(payload[5], payload[6], payload[7])));
                }
                break;

            case BalloonProtocol.OpQueueStatus:
                Expect(payload, 3, warnings);
                if (BalloonProtocol.TryDecodeQueueStatus(payload, out var queue))
                {
                    fields.Add(new ProtocolField("Kind", QueueKindName(queue.Kind)));
                    fields.Add(new ProtocolField("Queue count", queue.QueueCount.ToString(CultureInfo.InvariantCulture)));
                    summary = $"{name} {QueueKindName(queue.Kind)}, count={queue.QueueCount}";
                }
                break;

            case BalloonProtocol.OpFtmReport:
                Expect(payload, 8, warnings);
                if (BalloonProtocol.TryDecodeFtmReport(payload, out var ftm))
                {
                    fields.Add(new ProtocolField("Anchor id", ftm.AnchorId.ToString(CultureInfo.InvariantCulture)));
                    fields.Add(new ProtocolField("Distance", $"{ftm.DistanceCm} cm ({ftm.DistanceCm / 100.0:F2} m)"));
                    fields.Add(new ProtocolField("Timestamp", ftm.Timestamp == BalloonProtocol.TimestampUnknown
                        ? "unknown"
                        : $"0x{ftm.Timestamp:X8} (low 32 bits of tick40)"));
                    fields.Add(new ProtocolField("Batch id", ftm.BatchId.ToString(CultureInfo.InvariantCulture)));
                    summary = $"{name} anchor={ftm.AnchorId} distance={ftm.DistanceCm}cm batch={ftm.BatchId}";
                }
                break;

            case BalloonProtocol.OpNeighborReport:
                Expect(payload, 8, warnings);
                if (BalloonProtocol.TryDecodeNeighborReport(payload, out var neighbor))
                {
                    fields.Add(new ProtocolField("Batch id", neighbor.BatchId.ToString(CultureInfo.InvariantCulture)));
                    fields.Add(new ProtocolField("Packet index", neighbor.PacketIndex.ToString(CultureInfo.InvariantCulture)));
                    foreach (var entry in neighbor.Entries)
                    {
                        fields.Add(new ProtocolField("Neighbor",
                            $"{entry.MeshAddress:X4} rssi={entry.RssiDbm} dBm"));
                    }
                    summary = $"{name} batch={neighbor.BatchId} index={neighbor.PacketIndex} " +
                              $"entries={neighbor.Entries.Count}";
                }
                break;

            case BalloonProtocol.OpNeighborRequest:
            case BalloonProtocol.OpNeighborProbe:
            case BalloonProtocol.OpTimeRequest:
                Expect(payload, 0, warnings);
                break;

            case BalloonProtocol.OpTimeReport:
                Expect(payload, 8, warnings);
                if (BalloonProtocol.TryDecodeTimeReport(payload, out var unixMs))
                {
                    fields.Add(new ProtocolField("Unix ms", unixMs.ToString(CultureInfo.InvariantCulture)));
                    fields.Add(new ProtocolField("UTC", FormatUnixMs(unixMs)));
                    summary = $"{name} {FormatUnixMs(unixMs)}";
                }
                break;
        }

        return new DecodedMessage(opcode, true, op, name, summary, fields, warnings);
    }

    public static EncodedMessage EncodeLedSet(DateTimeOffset executeAt, byte r, byte g, byte b) =>
        FromPayload(BalloonProtocol.OpLedSet,
            BalloonProtocol.EncodeLedSet(TickFrom(executeAt), r, g, b));

    public static EncodedMessage EncodeLedCancel(DateTimeOffset executeAt) =>
        FromPayload(BalloonProtocol.OpLedCancel,
            BalloonProtocol.EncodeLedCancel(TickFrom(executeAt)));

    public static EncodedMessage EncodeLedReset(DateTimeOffset cutoffAt) =>
        FromPayload(BalloonProtocol.OpLedResetAll,
            BalloonProtocol.EncodeLedReset(TickFrom(cutoffAt)));

    public static EncodedMessage EncodeLedSetNow(DateTimeOffset issuedAt, byte r, byte g, byte b) =>
        FromPayload(BalloonProtocol.OpLedSetNow,
            BalloonProtocol.EncodeLedSetNow(TickFrom(issuedAt), r, g, b));

    public static EncodedMessage EncodeLedResetAndSet(DateTimeOffset cutoffAt, byte r, byte g, byte b) =>
        FromPayload(BalloonProtocol.OpLedResetAndSet,
            BalloonProtocol.EncodeLedResetAndSet(TickFrom(cutoffAt), r, g, b));

    public static EncodedMessage EncodeFtmReport(byte anchorId, ushort distanceCm,
        uint timestamp, byte batchId) =>
        FromPayload(BalloonProtocol.OpFtmReport,
            BalloonProtocol.EncodeFtmReport(new FtmReport(anchorId, distanceCm, timestamp, batchId)));

    public static EncodedMessage EncodeQueueStatus(byte kind, ushort queueCount) =>
        FromPayload(BalloonProtocol.OpQueueStatus,
            BalloonProtocol.EncodeQueueStatus(new QueueStatus(kind, queueCount)));

    public static EncodedMessage EncodeNeighborReport(byte batchId, byte packetIndex,
        IReadOnlyList<NeighborSample> entries) =>
        FromPayload(BalloonProtocol.OpNeighborReport,
            BalloonProtocol.EncodeNeighborReport(new NeighborReport(batchId, packetIndex,
                entries.Select(entry => new NeighborEntry(entry.Address, entry.RssiDbm)).ToArray())));

    public static EncodedMessage EncodeTimeReport(DateTimeOffset utc) =>
        FromPayload(BalloonProtocol.OpTimeReport,
            BalloonProtocol.EncodeTimeReport(utc.ToUniversalTime().ToUnixTimeMilliseconds()));

    public static EncodedMessage EncodeRaw(byte op, string? payloadHex)
    {
        if (op > 0x3F)
        {
            throw new ArgumentOutOfRangeException(nameof(op),
                "A vendor opcode uses six bits (0x00..0x3F).");
        }
        var payload = ParseHex(payloadHex);
        return FromPayload(op, payload);
    }

    public static uint NowTimestamp32() => BalloonProtocol.NowTimestamp32();

    public static uint ToVendorOpcode(byte op) => BalloonProtocol.VendorOpcode(op);

    public static string TransportDisplayName(GatewayTransport? transport) => transport switch
    {
        GatewayTransport.WebSocket => "WebSocket",
        GatewayTransport.Http => "HTTP",
        _ => "-"
    };

    public static string RawJsonLabel(GatewayTransport? transport) => transport switch
    {
        GatewayTransport.WebSocket => "Raw WebSocket frame",
        GatewayTransport.Http => "Synthesized mesh frame (HTTP transport)",
        _ => "Decoded input"
    };

    /// <summary>
    /// One-line decoded form for the decoded-log view: all payload fields are
    /// expanded, while the common opcode/payload framing fields are omitted.
    /// </summary>
    public static string FormatDecoded(DecodedMessage message)
    {
        if (!message.IsVendor)
        {
            return $"{message.Name} {message.Summary}";
        }
        var details = message.Fields
            .Where(field => field.Name is not ("Op" or "Opcode" or "Payload length" or "Payload"))
            .Select(field => $"{field.Name}={field.Value}")
            .ToArray();
        return details.Length == 0 ? message.Name : $"{message.Name} {string.Join(", ", details)}";
    }

    public static byte[] ParseHex(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }
        var cleaned = new string(text.Where(character =>
            !char.IsWhiteSpace(character) && character is not ('-' or ':' or ',')).ToArray());
        if (cleaned.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned[2..];
        }
        if (cleaned.Length == 0)
        {
            return [];
        }
        if (cleaned.Length % 2 != 0)
        {
            throw new FormatException("Payload must contain an even number of hexadecimal digits.");
        }
        try
        {
            return Convert.FromHexString(cleaned);
        }
        catch (FormatException exception)
        {
            throw new FormatException("Payload contains non-hexadecimal characters.", exception);
        }
    }

    public static string NormalizePayloadHex(string? text) => Convert.ToHexString(ParseHex(text));

    private static EncodedMessage FromPayload(byte op, byte[] payload)
    {
        var info = KnownOps.FirstOrDefault(candidate => candidate.Op == op);
        return new EncodedMessage(op, info?.Name ?? $"UNKNOWN_0x{op:X2}",
            BalloonProtocol.VendorOpcode(op), Convert.ToHexString(payload));
    }

    private static ulong TickFrom(DateTimeOffset at) =>
        BalloonProtocol.Tick40FromUnixMs(at.ToUniversalTime().ToUnixTimeMilliseconds());

    private static void Expect(byte[] payload, int expected, ICollection<string> warnings)
    {
        if (payload.Length != expected)
        {
            warnings.Add($"Expected {expected} payload bytes, got {payload.Length}.");
        }
    }

    private static string QueueKindName(byte kind) => kind switch
    {
        BalloonProtocol.OpQueueStatusNearFull => "NEAR_FULL",
        BalloonProtocol.OpQueueStatusRecovered => "RECOVERED",
        _ => $"UNKNOWN_{kind}"
    };

    private static string FormatRgb(byte r, byte g, byte b) =>
        $"#{r:X2}{g:X2}{b:X2} ({r}, {g}, {b})";

    private static string FormatTick(ulong tick) =>
        FormatUnixMs(BalloonProtocol.UnixMsFromTick40(tick));

    private static string FormatUnixMs(long unixMs)
    {
        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(unixMs).UtcDateTime
                .ToString("yyyy-MM-dd HH:mm:ss.fff 'UTC'", CultureInfo.InvariantCulture);
        }
        catch (ArgumentOutOfRangeException)
        {
            return $"invalid ({unixMs})";
        }
    }
}
