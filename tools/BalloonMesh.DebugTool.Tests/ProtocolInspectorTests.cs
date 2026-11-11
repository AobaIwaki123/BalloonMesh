using BalloonMesh.DebugTool.Core;
using Xunit;

namespace BalloonMesh.DebugTool.Tests;

public sealed class ProtocolInspectorTests
{
    [Fact]
    public void EncodeLedSet_DecodesBackToRgb()
    {
        var at = new DateTimeOffset(2026, 1, 2, 3, 4, 5, 670, TimeSpan.Zero);
        var encoded = ProtocolInspector.EncodeLedSet(at, 1, 2, 3);

        Assert.Equal(0xC102E5u, encoded.Opcode);
        Assert.Equal(8, encoded.PayloadHex.Length / 2);

        var decoded = ProtocolInspector.Decode(encoded.Opcode, encoded.PayloadHex);

        Assert.True(decoded.IsVendor);
        Assert.Equal("LED_SET", decoded.Name);
        Assert.Empty(decoded.Warnings);
        Assert.Contains(decoded.Fields,
            field => field.Name == "RGB" && field.Value == "#010203 (1, 2, 3)");
        Assert.Contains(decoded.Fields, field => field.Name == "Execute at (UTC)");
    }

    [Fact]
    public void EncodeFtmReport_DecodesDistanceAndUnknownTimestamp()
    {
        var encoded = ProtocolInspector.EncodeFtmReport(2, 1234, 0xFFFFFFFF, 7);

        var decoded = ProtocolInspector.Decode(encoded.Opcode, encoded.PayloadHex);

        Assert.Equal("FTM_REPORT", decoded.Name);
        Assert.Contains(decoded.Fields,
            field => field.Name == "Distance" && field.Value == "1234 cm (12.34 m)");
        Assert.Contains(decoded.Fields,
            field => field.Name == "Timestamp" && field.Value == "unknown");
    }

    [Fact]
    public void EncodeNeighborReport_DecodesBothEntries()
    {
        var encoded = ProtocolInspector.EncodeNeighborReport(3, 1,
        [
            new NeighborSample(0x0102, -40),
            new NeighborSample(0x0103, -70)
        ]);

        var decoded = ProtocolInspector.Decode(encoded.Opcode, encoded.PayloadHex);

        Assert.Equal("NEIGHBOR_REPORT", decoded.Name);
        Assert.Contains(decoded.Fields,
            field => field.Name == "Neighbor" && field.Value == "0102 rssi=-40 dBm");
        Assert.Contains(decoded.Fields,
            field => field.Name == "Neighbor" && field.Value == "0103 rssi=-70 dBm");
        Assert.Contains(decoded.Fields,
            field => field.Name == "Packet index" && field.Value == "1");
    }

    [Fact]
    public void EncodeNeighborReport_RejectsTooManyOrZeroAddressEntries()
    {
        Assert.Throws<ArgumentException>(() => ProtocolInspector.EncodeNeighborReport(1, 0,
        [
            new NeighborSample(1, -10),
            new NeighborSample(2, -20),
            new NeighborSample(3, -30)
        ]));

        Assert.Throws<ArgumentException>(() => ProtocolInspector.EncodeNeighborReport(1, 0,
            [new NeighborSample(0, -10)]));
    }

    [Fact]
    public void Decode_TruncatedLedSetWarnsButStillShowsTick()
    {
        var encoded = ProtocolInspector.EncodeLedSet(DateTimeOffset.UtcNow, 10, 20, 30);
        var truncated = encoded.PayloadHex[..10];

        var decoded = ProtocolInspector.Decode(encoded.Opcode, truncated);

        Assert.True(decoded.HasWarnings);
        Assert.Contains(decoded.Warnings, warning => warning.Contains("Expected 8"));
        Assert.Contains(decoded.Fields, field => field.Name == "Execute tick");
    }

    [Fact]
    public void Decode_NonVendorOpcodeIsReportedWithoutThrowing()
    {
        var decoded = ProtocolInspector.Decode(0x00000012, "00");

        Assert.False(decoded.IsVendor);
        Assert.Equal("NON_VENDOR", decoded.Name);
        Assert.True(decoded.HasWarnings);
    }

    [Fact]
    public void Decode_InvalidHexThrows()
    {
        Assert.Throws<FormatException>(() => ProtocolInspector.Decode(0xC102E5, "GG"));
        Assert.Throws<FormatException>(() => ProtocolInspector.Decode(0xC102E5, "010"));
    }

    [Fact]
    public void EncodeTimeReport_RoundTripsUtc()
    {
        var at = new DateTimeOffset(2026, 5, 6, 7, 8, 9, 100, TimeSpan.Zero);
        var encoded = ProtocolInspector.EncodeTimeReport(at);

        var decoded = ProtocolInspector.Decode(encoded.Opcode, encoded.PayloadHex);

        Assert.Equal("TIME_REPORT", decoded.Name);
        Assert.Contains(decoded.Fields,
            field => field.Name == "Unix ms" && field.Value == at.ToUnixTimeMilliseconds().ToString());
    }

    [Fact]
    public void NormalizePayloadHex_AcceptsSeparatorsAndPrefix()
    {
        Assert.Equal("010203", ProtocolInspector.NormalizePayloadHex("0x01 02:03"));
        Assert.Equal(string.Empty, ProtocolInspector.NormalizePayloadHex("   "));
    }

    [Fact]
    public void ToVendorOpcode_ExpandsShortOpToTheEspressifEncoding()
    {
        Assert.Equal(0xCA02E5u, ProtocolInspector.ToVendorOpcode(0x0A));
    }

    [Fact]
    public void EncodeRaw_RejectsOpcodeOutsideTheSixBitVendorRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ProtocolInspector.EncodeRaw(0x40, string.Empty));
    }
}
