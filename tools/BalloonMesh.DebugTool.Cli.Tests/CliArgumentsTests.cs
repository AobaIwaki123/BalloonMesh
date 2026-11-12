using Xunit;
using BalloonMesh.DebugTool.Cli;

namespace BalloonMesh.DebugTool.Cli.Tests;

public sealed class CliArgumentsTests
{
    [Fact]
    public void ParseKeepsWordsValuesAndFlags()
    {
        var arguments = CliArguments.Parse(
            ["led", "set", "--target-kind", "balloon", "--color=#FF8000", "--json"]);

        Assert.Equal(new[] { "led", "set" }, arguments.Words);
        Assert.Equal("balloon", arguments.Value("target-kind"));
        Assert.Equal("#FF8000", arguments.Value("color"));
        Assert.True(arguments.Flag("json"));
        Assert.Empty(arguments.Errors);
    }

    [Fact]
    public void RepeatedOptionsAreCollected()
    {
        var arguments = CliArguments.Parse(
            ["gateway", "run", "--event", "ftm:anchor=1", "--event", "queue:count=401"]);

        Assert.Equal(new[] { "ftm:anchor=1", "queue:count=401" }, arguments.All("event"));
    }

    [Fact]
    public void NegativeNumbersAreConsumedAsValues()
    {
        var arguments = CliArguments.Parse(["gateway", "run", "--rssi", "-45"]);

        Assert.Equal("-45", arguments.Value("rssi"));
        Assert.Empty(arguments.Errors);
    }

    [Fact]
    public void UnknownOptionIsRejectedDuringValidation()
    {
        var arguments = CliArguments.Parse(["health", "--nope", "value"]);

        var exception = Assert.Throws<CliUsageException>(
            () => arguments.ValidateOptions("server", "json"));
        Assert.Contains("--nope", exception.Message);
    }

    [Fact]
    public void MissingValueIsReported()
    {
        var arguments = CliArguments.Parse(["protocol", "encode", "--op"]);

        Assert.Contains(arguments.Errors, error => error.Contains("--op"));
    }

    [Fact]
    public void FlagWithInlineValueIsRejected()
    {
        var arguments = CliArguments.Parse(["health", "--json=true"]);

        Assert.Contains(arguments.Errors, error => error.Contains("--json"));
    }

    [Fact]
    public void DoubleDashStopsOptionParsing()
    {
        var arguments = CliArguments.Parse(["protocol", "decode", "--", "--payload"]);

        Assert.Equal(new[] { "protocol", "decode", "--payload" }, arguments.Words);
        Assert.Null(arguments.Value("payload"));
    }

    [Fact]
    public void RequireValueThrowsForEmptyValue()
    {
        var arguments = CliArguments.Parse(["protocol", "decode", "--opcode", ""]);

        Assert.Throws<CliUsageException>(() => arguments.RequireValue("opcode"));
    }
}
