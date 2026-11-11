using BalloonMesh.Protocol;

namespace BalloonMesh.Server.Tests;

public sealed class ProtocolTests
{
    [Fact]
    public void VendorOpcodeMatchesEspIdfEncoding()
    {
        Assert.Equal(0xC702E5u, BalloonProtocol.VendorOpcode(BalloonProtocol.OpFtmReport));
        Assert.Equal(0xC802E5u, BalloonProtocol.VendorOpcode(BalloonProtocol.OpNeighborRequest));
        Assert.True(BalloonProtocol.TryGetMessageOp(0xC702E5u, out var op));
        Assert.Equal(BalloonProtocol.OpFtmReport, op);
        Assert.True(BalloonProtocol.TryGetMessageOp(0xC502E5u, out op));
        Assert.Equal(BalloonProtocol.OpLedResetAndSet, op);
        Assert.False(BalloonProtocol.TryGetMessageOp(0x00000000u, out _));
        Assert.False(BalloonProtocol.TryGetMessageOp(0xC702E4u, out _));
    }

    [Fact]
    public void Tick40IsLittleEndianAndTenMillisecondBased()
    {
        var unixMs = 1_762_560_000_123L;
        var tick = BalloonProtocol.Tick40FromUnixMs(unixMs);
        var encoded = BalloonProtocol.EncodeTick40(tick);
        Assert.Equal(5, encoded.Length);
        Assert.Equal(tick, BalloonProtocol.DecodeTick40(encoded));
        Assert.Equal(1_762_560_000_120L, BalloonProtocol.UnixMsFromTick40(tick));
        Assert.Equal(1UL, BalloonProtocol.Tick40FromUnixMs(BalloonProtocol.TickOriginUnixMs + 10));
    }

    [Fact]
    public void LedAndReportCodecsRoundTrip()
    {
        var tick = 0x0102030405UL;
        var led = BalloonProtocol.EncodeLedSet(tick, 1, 2, 3);
        Assert.Equal(new byte[] { 0x05, 0x04, 0x03, 0x02, 0x01, 1, 2, 3 }, led);

        var report = new FtmReport(7, 1234, 0xDEADBEEF, 9);
        var encoded = BalloonProtocol.EncodeFtmReport(report);
        Assert.True(BalloonProtocol.TryDecodeFtmReport(encoded, out var decoded));
        Assert.Equal(report, decoded);

        var status = new QueueStatus(BalloonProtocol.OpQueueStatusNearFull, 401);
        var statusBytes = BalloonProtocol.EncodeQueueStatus(status);
        Assert.True(BalloonProtocol.TryDecodeQueueStatus(statusBytes, out var decodedStatus));
        Assert.Equal(status, decodedStatus);
    }

    [Fact]
    public void UnknownTimestampOrderingIsWrapAware()
    {
        Assert.True(BalloonProtocol.TimestampNotNewer(10, 11));
        Assert.False(BalloonProtocol.TimestampNotNewer(12, 11));
        Assert.False(BalloonProtocol.TimestampNotNewer(0, uint.MaxValue - 1));
        Assert.True(BalloonProtocol.TimestampNotNewer(uint.MaxValue - 1, 0));
        Assert.False(BalloonProtocol.TimestampNotNewer(1, uint.MaxValue - 1));
    }

    [Fact]
    public void NeighborReportCodecReadsEightBytePackets()
    {
        var payload = new byte[]
        {
            3,                 // batch id
            1,                 // packet index
            0x34, 0x12, 201,   // entry 0: 0x1234, rssi (sbyte)201 = -55
            0xAA, 0x00, 179,   // entry 1: 0x00AA, rssi (sbyte)179 = -77
        };
        Assert.True(BalloonProtocol.TryDecodeNeighborReport(payload, out var report));
        Assert.Equal(3, report.BatchId);
        Assert.Equal(1, report.PacketIndex);
        Assert.Equal(2, report.Entries.Count);
        Assert.Equal((ushort)0x1234, report.Entries[0].MeshAddress);
        Assert.Equal((sbyte)-55, report.Entries[0].RssiDbm);
        Assert.Equal((ushort)0x00AA, report.Entries[1].MeshAddress);
        Assert.Equal((sbyte)-77, report.Entries[1].RssiDbm);

        // An unused slot is zero-filled and yields no entry.
        payload[5] = 0;
        payload[6] = 0;
        payload[7] = 0;
        Assert.True(BalloonProtocol.TryDecodeNeighborReport(payload, out report));
        Assert.Single(report.Entries);
        Assert.Equal((ushort)0x1234, report.Entries[0].MeshAddress);

        Assert.False(BalloonProtocol.TryDecodeNeighborReport(payload.AsSpan(0, 7), out _));
        Assert.False(BalloonProtocol.TryDecodeNeighborReport(ReadOnlySpan<byte>.Empty, out _));
    }
}
