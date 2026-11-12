using BalloonMesh.DebugTool.Core;

namespace BalloonMesh.DebugTool.Cli.Commands;

/// <summary>LED commands sent through the server's operator API.</summary>
public static class LedCommands
{
    private static readonly string[] TargetOptions = ["target-kind", "target"];
    private static readonly string[] ColorOptions = ["color", "r", "g", "b"];

    public static async Task<int> NowAsync(CliContext context, CliArguments arguments,
        CancellationToken cancellationToken)
    {
        context.ValidateOptions([.. TargetOptions, .. ColorOptions]);
        var color = Color(arguments);
        var (kind, target) = Target(arguments);
        var client = await context.GetClientAsync(cancellationToken).ConfigureAwait(false);
        var result = await client.SendLedNowAsync(
            new LedNowCommand(kind, target, color.R, color.G, color.B), cancellationToken)
            .ConfigureAwait(false);
        OutputFormatter.WriteCommandResult(context, result);
        return result.Accepted ? CliApplication.ExitSuccess : CliApplication.ExitFailure;
    }

    public static async Task<int> SetAsync(CliContext context, CliArguments arguments,
        CancellationToken cancellationToken)
    {
        context.ValidateOptions([.. TargetOptions, .. ColorOptions, "at"]);
        var color = Color(arguments);
        var executeAt = ValueParsers.ParseTimeOr(arguments.Value("at"),
            DateTimeOffset.UtcNow.AddSeconds(10), "at");
        var (kind, target) = Target(arguments);
        var client = await context.GetClientAsync(cancellationToken).ConfigureAwait(false);
        var result = await client.SendLedSetAsync(
            new LedSetCommand(kind, target, executeAt, color.R, color.G, color.B),
            cancellationToken).ConfigureAwait(false);
        OutputFormatter.WriteCommandResult(context, result);
        return result.Accepted ? CliApplication.ExitSuccess : CliApplication.ExitFailure;
    }

    public static async Task<int> CancelAsync(CliContext context, CliArguments arguments,
        CancellationToken cancellationToken)
    {
        context.ValidateOptions([.. TargetOptions, "at"]);
        var executeAt = ValueParsers.ParseTime(arguments.RequireValue("at"), "at");
        var (kind, target) = Target(arguments);
        var client = await context.GetClientAsync(cancellationToken).ConfigureAwait(false);
        var result = await client.SendLedCancelAsync(
            new LedCancelCommand(kind, target, executeAt), cancellationToken)
            .ConfigureAwait(false);
        OutputFormatter.WriteCommandResult(context, result);
        return result.Accepted ? CliApplication.ExitSuccess : CliApplication.ExitFailure;
    }

    public static async Task<int> ResetAsync(CliContext context, CliArguments arguments,
        CancellationToken cancellationToken)
    {
        context.ValidateOptions([.. TargetOptions, .. ColorOptions, "cutoff", "set-now"]);
        var setNow = arguments.Flag("set-now");
        var (kind, target) = Target(arguments);
        var cutoff = arguments.Value("cutoff") is { } cutoffText
            ? ValueParsers.ParseTime(cutoffText, "cutoff")
            : (DateTimeOffset?)null;
        var color = setNow ? Color(arguments) : (R: (byte)0, G: (byte)0, B: (byte)0);
        var client = await context.GetClientAsync(cancellationToken).ConfigureAwait(false);
        var result = await client.SendLedResetAsync(
            new LedResetCommand(kind, target, cutoff, setNow, color.R, color.G, color.B),
            cancellationToken).ConfigureAwait(false);
        OutputFormatter.WriteCommandResult(context, result);
        return result.Accepted ? CliApplication.ExitSuccess : CliApplication.ExitFailure;
    }

    private static (string Kind, string Target) Target(CliArguments arguments)
    {
        var kind = arguments.Value("target-kind") ?? "all";
        var target = arguments.Value("target") ?? "all";
        if (string.IsNullOrWhiteSpace(target))
        {
            throw new CliUsageException("--target must not be empty.");
        }
        return (kind, target.Trim());
    }

    private static (byte R, byte G, byte B) Color(CliArguments arguments)
    {
        if (arguments.Value("color") is { } color)
        {
            return ValueParsers.ParseColor(color);
        }
        if (arguments.Value("r") is null && arguments.Value("g") is null && arguments.Value("b") is null)
        {
            throw new CliUsageException("Option '--color' is required (use #RRGGBB or r,g,b).");
        }
        return (
            ValueParsers.ParseByte(arguments.RequireValue("r"), "r"),
            ValueParsers.ParseByte(arguments.RequireValue("g"), "g"),
            ValueParsers.ParseByte(arguments.RequireValue("b"), "b"));
    }
}
