using System.Buffers.Binary;

namespace BalloonMesh.Server.Protocol;

/// <summary>
/// Wire codec for the Bluetooth Mesh vendor messages.  The opcode encoding
/// matches ESP-IDF's ESP_BLE_MESH_MODEL_OP_3(op, companyId).
/// </summary>
public static class BalloonProtocol
{
    public const uint CompanyId = 0x02E5;
    public const uint TimestampUnknown = 0xFFFFFFFF;

    public const byte OpLedSet = 0x01;
    public const byte OpLedCancel = 0x02;
    public const byte OpLedResetAll = 0x03;
    public const byte OpLedSetNow = 0x04;
    public const byte OpLedResetAndSet = 0x05;
    public const byte OpQueueStatus = 0x06;
    public const byte OpFtmReport = 0x07;
    public const byte OpNeighborRequest = 0x08;
    public const byte OpNeighborProbe = 0x09;
    public const byte OpNeighborReport = 0x0A;
    public const byte OpTimeRequest = 0x0B;
    public const byte OpTimeReport = 0x0C;

    public const ushort GroupLedAll = 0xC010;
    public const ushort GroupFtmReport = GroupLedAll;
    public const ushort GroupNeighborReport = GroupLedAll;
    public const ushort GroupQueueStatus = GroupLedAll;
    public const ushort GroupNeighborProbe = GroupLedAll;
    public const ushort GroupTime = GroupLedAll;

    public const byte OpQueueStatusNearFull = 0x01;
    public const byte OpQueueStatusRecovered = 0x02;

    public const long TickOriginUnixMs = 1735689600000; // 2025-01-01T00:00:00Z
    public const int TickMilliseconds = 10;

    public static uint VendorOpcode(byte op) =>
        0xC00000u | ((uint)op << 16) | CompanyId;

    public static bool TryGetMessageOp(uint opcode, out byte op)
    {
        op = (byte)((opcode >> 16) & 0x3F);
        return (opcode & 0xC00000u) == 0xC00000u
            && (opcode & 0xFFFFu) == CompanyId;
    }

    public static ulong Tick40FromUnixMs(long unixMs)
    {
        var delta = unixMs - TickOriginUnixMs;
        return delta <= 0 ? 0 : (ulong)(delta / TickMilliseconds);
    }

    public static long UnixMsFromTick40(ulong tick) =>
        TickOriginUnixMs + (long)tick * TickMilliseconds;

    public static ulong NowTick40(TimeProvider? timeProvider = null) =>
        Tick40FromUnixMs((timeProvider ?? TimeProvider.System).GetUtcNow().ToUnixTimeMilliseconds());

    public static uint NowTimestamp32(TimeProvider? timeProvider = null) =>
        (uint)(NowTick40(timeProvider) & 0xFFFFFFFFu);

    public static byte[] EncodeTick40(ulong tick)
    {
        var result = new byte[5];
        for (var i = 0; i < 5; i++)
        {
            result[i] = (byte)(tick >> (8 * i));
        }
        return result;
    }

    public static ulong DecodeTick40(ReadOnlySpan<byte> data)
    {
        if (data.Length < 5)
        {
            throw new ArgumentException("A 40-bit tick requires 5 bytes.", nameof(data));
        }
        ulong value = 0;
        for (var i = 0; i < 5; i++)
        {
            value |= (ulong)data[i] << (8 * i);
        }
        return value & 0xFFFFFFFFFFUL;
    }

    public static byte[] EncodeLedSet(ulong executeTick, byte r, byte g, byte b)
    {
        var payload = new byte[8];
        var tick = EncodeTick40(executeTick);
        tick.CopyTo(payload, 0);
        payload[5] = r;
        payload[6] = g;
        payload[7] = b;
        return payload;
    }

    public static byte[] EncodeLedCancel(ulong executeTick) => EncodeTick40(executeTick);

    public static byte[] EncodeLedReset(ulong cutoffTick) => EncodeTick40(cutoffTick);

    public static byte[] EncodeLedSetNow(ulong issuedTick, byte r, byte g, byte b) =>
        EncodeLedSet(issuedTick, r, g, b);

    public static byte[] EncodeLedResetAndSet(ulong cutoffTick, byte r, byte g, byte b) =>
        EncodeLedSet(cutoffTick, r, g, b);

    public static bool TryDecodeFtmReport(ReadOnlySpan<byte> payload, out FtmReport report)
    {
        if (payload.Length != 8)
        {
            report = default;
            return false;
        }
        report = new FtmReport(
            payload[0],
            BinaryPrimitives.ReadUInt16LittleEndian(payload[1..3]),
            BinaryPrimitives.ReadUInt32LittleEndian(payload[3..7]),
            payload[7]);
        return true;
    }

    public static byte[] EncodeFtmReport(FtmReport report)
    {
        var payload = new byte[8];
        payload[0] = report.AnchorId;
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(1, 2), report.DistanceCm);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(3, 4), report.Timestamp);
        payload[7] = report.BatchId;
        return payload;
    }

    public static byte[] EncodeQueueStatus(QueueStatus status)
    {
        var payload = new byte[3];
        payload[0] = status.Kind;
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(1, 2), status.QueueCount);
        return payload;
    }

    public static bool TryDecodeQueueStatus(ReadOnlySpan<byte> payload, out QueueStatus status)
    {
        if (payload.Length != 3)
        {
            status = default;
            return false;
        }
        status = new QueueStatus(payload[0], BinaryPrimitives.ReadUInt16LittleEndian(payload[1..3]));
        return true;
    }

    public static bool TryDecodeNeighborReport(ReadOnlySpan<byte> payload, out NeighborReport report)
    {
        if (payload.Length != 8 || payload[2] > 2)
        {
            report = default;
            return false;
        }
        var entries = new List<NeighborEntry>(payload[2]);
        for (var i = 0; i < payload[2]; i++)
        {
            var offset = 3 + i * 3;
            entries.Add(new NeighborEntry(
                BinaryPrimitives.ReadUInt16LittleEndian(payload[offset..(offset + 2)]),
                (sbyte)payload[offset + 2]));
        }
        report = new NeighborReport(payload[0], payload[1], entries);
        return true;
    }

    /// <summary>True when a is not newer than b in the wrapping 32-bit domain.</summary>
    public static bool TimestampNotNewer(uint a, uint b) => (int)(a - b) <= 0;

    public static string ToHex(ReadOnlySpan<byte> data) => Convert.ToHexString(data);

    public static byte[] FromHex(string hex) => Convert.FromHexString(hex);
}

public readonly record struct FtmReport(byte AnchorId, ushort DistanceCm, uint Timestamp, byte BatchId);
public readonly record struct QueueStatus(byte Kind, ushort QueueCount);
public readonly record struct NeighborEntry(ushort MeshAddress, sbyte RssiDbm);
public readonly record struct NeighborReport(byte BatchId, byte PacketIndex, IReadOnlyList<NeighborEntry> Entries);
