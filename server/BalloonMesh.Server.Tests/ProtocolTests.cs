using BalloonMesh.Server.Protocol;

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
}
