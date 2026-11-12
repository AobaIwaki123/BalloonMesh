using Xunit;
using BalloonMesh.DebugTool.Cli;

namespace BalloonMesh.DebugTool.Cli.Tests;

public sealed class CliApplicationTests
{
    [Fact]
    public async Task VersionPrintsTheVersion()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exit = await CliApplication.RunAsync(["version"], output, error);

        Assert.Equal(CliApplication.ExitSuccess, exit);
        Assert.Equal(Usage.Version, output.ToString().Trim());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task HelpPrintsUsage()
    {
        var output = new StringWriter();

        var exit = await CliApplication.RunAsync(["help"], output, new StringWriter());

        Assert.Equal(CliApplication.ExitSuccess, exit);
        Assert.Contains("Usage:", output.ToString());
    }

    [Fact]
    public async Task SubcommandHelpPrintsSubcommandUsage()
    {
        var output = new StringWriter();

        var exit = await CliApplication.RunAsync(["gateway", "--help"], output, new StringWriter());

        Assert.Equal(CliApplication.ExitSuccess, exit);
        Assert.Contains("--transport", output.ToString());
    }

    [Fact]
    public async Task NoArgumentsPrintsUsageAndExitsWithTwo()
    {
        var output = new StringWriter();

        var exit = await CliApplication.RunAsync([], output, new StringWriter());

        Assert.Equal(CliApplication.ExitUsage, exit);
        Assert.Contains("Usage:", output.ToString());
    }

    [Fact]
    public async Task UnknownCommandIsAUsageError()
    {
        var error = new StringWriter();

        var exit = await CliApplication.RunAsync(["bogus"], new StringWriter(), error);

        Assert.Equal(CliApplication.ExitUsage, exit);
        Assert.Contains("Unknown command", error.ToString());
    }

    [Fact]
    public async Task InvalidColorIsAUsageError()
    {
        var error = new StringWriter();

        var exit = await CliApplication.RunAsync(
            ["protocol", "encode", "--op", "LED_SET", "--color", "red"],
            new StringWriter(), error);

        Assert.Equal(CliApplication.ExitUsage, exit);
        Assert.Contains("Hex color", error.ToString());
    }
}
