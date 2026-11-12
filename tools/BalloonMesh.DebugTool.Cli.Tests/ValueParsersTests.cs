using Xunit;
using BalloonMesh.DebugTool.Cli;

namespace BalloonMesh.DebugTool.Cli.Tests;

public sealed class ValueParsersTests
{
    [Theory]
    [InlineData("#FF8000", 255, 128, 0)]
    [InlineData("ff8000", 255, 128, 0)]
    [InlineData("1, 2, 3", 1, 2, 3)]
    public void ParseColorAcceptsHexAndTriplets(string text, byte r, byte g, byte b)
    {
        var color = ValueParsers.ParseColor(text);

        Assert.Equal((r, g, b), color);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#FF80")]
    [InlineData("1,2")]
    [InlineData("256,0,0")]
    public void ParseColorRejectsInvalidInput(string text)
    {
        Assert.Throws<FormatException>(() => ValueParsers.ParseColor(text));
    }

    [Fact]
    public void ParseTimeAcceptsIso8601()
    {
        var value = ValueParsers.ParseTime("2026-01-01T00:00:10Z", "at");

        Assert.Equal(DateTimeOffset.Parse("2026-01-01T00:00:10Z"), value);
    }

    [Fact]
    public void ParseTimeAcceptsRelativeOffsets()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(9);
        var value = ValueParsers.ParseTime("+10s", "at");
        var after = DateTimeOffset.UtcNow.AddSeconds(11);

        Assert.InRange(value, before, after);
    }

    [Fact]
    public void ParseTimeRejectsGarbage()
    {
        Assert.Throws<FormatException>(() => ValueParsers.ParseTime("tomorrow", "at"));
    }

    [Fact]
    public void ParseIntegerAcceptsHexAndChecksRange()
    {
        Assert.Equal((byte)0x2E, ValueParsers.ParseByte("0x2E", "op"));

        Assert.Throws<FormatException>(() => ValueParsers.ParseByte("256", "op"));
        Assert.Throws<FormatException>(() => ValueParsers.ParseUShort("-1", "address"));
    }

    [Fact]
    public void ParseNeighborReadsAddressAndRssi()
    {
        var neighbor = ValueParsers.ParseNeighbor("0x0102@-45");

        Assert.Equal((ushort)0x0102, neighbor.Address);
        Assert.Equal((sbyte)-45, neighbor.RssiDbm);
    }

    [Fact]
    public void ParseNeighborsRejectsTooManyEntries()
    {
        Assert.Throws<FormatException>(() => ValueParsers.ParseNeighbors(
            ["0x0101@-40", "0x0102@-45", "0x0103@-50"]));
    }

    [Theory]
    [InlineData("500ms", 0.5)]
    [InlineData("2s", 2)]
    [InlineData("1m", 60)]
    [InlineData("1h", 3600)]
    public void ParseDurationConvertsUnits(string text, double expectedSeconds)
    {
        Assert.Equal(expectedSeconds, ValueParsers.ParseDuration(text, "delay").TotalSeconds, 3);
    }
}
