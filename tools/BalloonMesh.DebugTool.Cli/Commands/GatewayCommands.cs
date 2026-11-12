using BalloonMesh.DebugTool.Core;

namespace BalloonMesh.DebugTool.Cli.Commands;

/// <summary>Runs the gateway emulator from the terminal and logs mesh traffic.</summary>
public static class GatewayCommands
{
    public static async Task<int> RunAsync(CliContext context, CliArguments arguments,
        CancellationToken cancellationToken)
    {
        context.ValidateOptions("id", "transport", "poll-ms", "firmware", "spool",
            "event", "event-delay", "duration", "no-provisioned");

        var gatewayId = (arguments.Value("id") ?? "debug-gateway").Trim();
        if (gatewayId.Length == 0)
        {
            throw new CliUsageException("--id must not be empty.");
        }
        var transport = (arguments.Value("transport") ?? "websocket").Trim().ToLowerInvariant() switch
        {
            "ws" or "websocket" => GatewayTransport.WebSocket,
            "http" or "poll" or "polling" => GatewayTransport.Http,
            var other => throw new CliUsageException(
                $"Unknown transport '{other}'. Use websocket or http.")
        };
        var pollMs = arguments.Value("poll-ms") is { } pollText
            ? ValueParsers.ParseInt32(pollText, "poll-ms", 0, GatewayEmulatorOptions.MaxPollWaitMs)
            : 10_000;
        var firmware = arguments.Value("firmware") ?? "debugtool/1.0";
        var spool = arguments.Value("spool") is { } spoolText
            ? ValueParsers.ParseInt32(spoolText, "spool", 0, int.MaxValue)
            : 0;
        var provisioned = !arguments.Flag("no-provisioned");
        var eventDelayMs = arguments.Value("event-delay") is { } delayText
            ? ValueParsers.ParseInt32(delayText, "event-delay", 0, 600_000)
            : 500;
        var durationSeconds = arguments.Value("duration") is { } durationText
            ? ValueParsers.ParseInt32(durationText, "duration", 0, 86_400)
            : 0;

        // Parse every event before connecting so a typo fails fast.
        var events = arguments.All("event").Select(EventSpec.Parse).ToArray();

        var client = await context.GetClientAsync(cancellationToken).ConfigureAwait(false);
        var options = new GatewayEmulatorOptions(
            context.Server, gatewayId, context.GatewayToken, transport, firmware,
            provisioned, spool, pollMs);
        await using var emulator = new GatewayEmulator(options, client);

        emulator.Log += message => OutputFormatter.WriteGatewayLog(context, message);
        emulator.ConnectionChanged += state =>
            OutputFormatter.WriteGatewayConnection(context, state);
        emulator.CommandReceived += received =>
            OutputFormatter.WriteGatewayCommand(context, received);
        emulator.EventSent += sent => OutputFormatter.WriteGatewayEvent(context, sent);

        await emulator.StartAsync(cancellationToken).ConfigureAwait(false);

        foreach (var message in events)
        {
            if (eventDelayMs > 0)
            {
                await Task.Delay(eventDelayMs, cancellationToken).ConfigureAwait(false);
            }
            await emulator.SendEventAsync(message, cancellationToken).ConfigureAwait(false);
        }

        if (durationSeconds > 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(durationSeconds), cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            context.Out.WriteLine("Listening for gateway commands. Press Ctrl+C to stop.");
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        }

        await emulator.StopAsync().ConfigureAwait(false);
        return CliApplication.ExitSuccess;
    }
}
