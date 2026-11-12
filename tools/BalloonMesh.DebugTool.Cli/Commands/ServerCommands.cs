using System.Globalization;
using BalloonMesh.DebugTool.Core;

namespace BalloonMesh.DebugTool.Cli.Commands;

/// <summary>Read-only server commands: health, fleet state and show steps.</summary>
public static class ServerCommands
{
    public static async Task<int> HealthAsync(CliContext context, CliArguments arguments,
        CancellationToken cancellationToken)
    {
        context.ValidateOptions();
        var client = await context.GetClientAsync(cancellationToken).ConfigureAwait(false);
        var health = await client.GetHealthAsync(cancellationToken).ConfigureAwait(false);
        if (context.Json)
        {
            context.WriteJson(health);
            return CliApplication.ExitSuccess;
        }
        context.Out.WriteLine($"status     {health.Status}");
        context.Out.WriteLine($"version    {health.Version}");
        context.Out.WriteLine($"serverTime {health.Time:O}");
        return CliApplication.ExitSuccess;
    }

    public static async Task<int> StateAsync(CliContext context, CliArguments arguments,
        CancellationToken cancellationToken)
    {
        context.ValidateOptions();
        var client = await context.GetClientAsync(cancellationToken).ConfigureAwait(false);
        var state = await client.GetStateAsync(cancellationToken).ConfigureAwait(false);
        OutputFormatter.WriteFleetState(context, state);
        return CliApplication.ExitSuccess;
    }

    public static async Task<int> ShowAsync(CliContext context, CliArguments arguments,
        CancellationToken cancellationToken)
    {
        context.ValidateOptions("limit", "cancel");
        var client = await context.GetClientAsync(cancellationToken).ConfigureAwait(false);

        if (arguments.Value("cancel") is { } cancelText)
        {
            if (!long.TryParse(cancelText.Trim(), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var id) || id <= 0)
            {
                throw new CliUsageException($"--cancel '{cancelText}' is not a valid show step id.");
            }
            var result = await client.CancelShowStepAsync(id, cancellationToken).ConfigureAwait(false);
            if (result is null)
            {
                context.Error.WriteLine($"Show step {id} was not found.");
                return CliApplication.ExitFailure;
            }
            OutputFormatter.WriteCommandResult(context, result);
            return result.Accepted ? CliApplication.ExitSuccess : CliApplication.ExitFailure;
        }

        var limit = arguments.Value("limit") is { } limitText
            ? ValueParsers.ParseInt32(limitText, "limit", 1, 200)
            : 50;
        var steps = await client.GetShowStepsAsync(limit, cancellationToken).ConfigureAwait(false);
        OutputFormatter.WriteShowSteps(context, steps);
        return CliApplication.ExitSuccess;
    }
}
