using Xunit;
using BalloonMesh.DebugTool.Cli;
using BalloonMesh.DebugTool.Core;
using BalloonMesh.Protocol;

namespace BalloonMesh.DebugTool.Cli.Tests;

public sealed class ProtocolCommandBuilderTests
{
    [Fact]
    public void LedSetUsesAtAndColor()
    {
        var arguments = CliArguments.Parse(
            ["protocol", "encode", "--op", "LED_SET", "--at", "2026-01-01T00:00:10Z",
             "--color", "#102030"]);

        var encoded = ProtocolCommandBuilder.Build("LED_SET", arguments);
        var expected = BalloonProtocol.EncodeLedSet(
            BalloonProtocol.Tick40FromUnixMs(
                DateTimeOffset.Parse("2026-01-01T00:00:10Z").ToUnixTimeMilliseconds()),
            0x10, 0x20, 0x30);

        Assert.Equal(Convert.ToHexString(expected), encoded.PayloadHex);
        Assert.Equal("0xC102E5", encoded.OpcodeHex);
    }

    [Fact]
    public void LedSetDefaultsToTenSecondsFromNow()
    {
        var arguments = CliArguments.Parse(
            ["protocol", "encode", "--op", "LED_SET", "--color", "1,2,3"]);

        var encoded = ProtocolCommandBuilder.Build("LED_SET", arguments);

        Assert.Equal(8, ProtocolInspector.ParseHex(encoded.PayloadHex).Length);
        var decoded = ProtocolInspector.Decode(encoded.Opcode, encoded.PayloadHex);
        Assert.Contains(decoded.Fields, field => field.Name == "RGB");
    }

    [Fact]
    public void QueueStatusRequiresCount()
    {
        var arguments = CliArguments.Parse(
            ["protocol", "encode", "--op", "QUEUE_STATUS", "--count", "401"]);

        var encoded = ProtocolCommandBuilder.Build("QUEUE_STATUS", arguments);

        Assert.Equal("019101", encoded.PayloadHex);
        Assert.Equal("0xC602E5", encoded.OpcodeHex);
    }

    [Fact]
    public void NeighborReportKeepsEntries()
    {
        var arguments = CliArguments.Parse(
            ["protocol", "encode", "--op", "NEIGHBOR_REPORT",
             "--neighbor", "0x0102@-45", "--neighbor", "0x0103@-60"]);

        var encoded = ProtocolCommandBuilder.Build("NEIGHBOR_REPORT", arguments);

        Assert.Equal(8, ProtocolInspector.ParseHex(encoded.PayloadHex).Length);
        Assert.Equal("0xCA02E5", encoded.OpcodeHex);
    }

    [Fact]
    public void RawOpcodeUsesSuppliedPayload()
    {
        var arguments = CliArguments.Parse(
            ["protocol", "encode", "--op", "0x0A", "--payload", "0102030405060708"]);

        var encoded = ProtocolCommandBuilder.Build("0x0A", arguments);

        Assert.Equal((byte)0x0A, encoded.Op);
        Assert.Equal("0102030405060708", encoded.PayloadHex);
    }

    [Fact]
    public void TimeReportEncodesEightBytes()
    {
        var arguments = CliArguments.Parse(
            ["protocol", "encode", "--op", "TIME_REPORT", "--at", "2026-01-01T00:00:00Z"]);

        var encoded = ProtocolCommandBuilder.Build("TIME_REPORT", arguments);

        Assert.Equal(8, ProtocolInspector.ParseHex(encoded.PayloadHex).Length);
        Assert.Equal(0x0C, encoded.Op);
    }

    [Fact]
    public void UnknownOpIsRejected()
    {
        var arguments = CliArguments.Parse(["protocol", "encode", "--op", "NOPE"]);

        Assert.Throws<CliUsageException>(() => ProtocolCommandBuilder.Build("NOPE", arguments));
    }

    [Fact]
    public void MissingColorIsRejected()
    {
        var arguments = CliArguments.Parse(["protocol", "encode", "--op", "LED_SET"]);

        Assert.Throws<CliUsageException>(() => ProtocolCommandBuilder.Build("LED_SET", arguments));
    }
}
