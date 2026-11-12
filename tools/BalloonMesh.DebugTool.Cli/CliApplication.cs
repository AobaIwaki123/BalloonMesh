using BalloonMesh.DebugTool.Cli.Commands;
using BalloonMesh.DebugTool.Core;

namespace BalloonMesh.DebugTool.Cli;

/// <summary>Entry point shared by the executable and the tests.</summary>
public static class CliApplication
{
    public const int ExitSuccess = 0;
    public const int ExitFailure = 1;
    public const int ExitUsage = 2;
    public const int ExitCanceled = 130;

    public static async Task<int> RunAsync(IReadOnlyList<string> arguments, TextWriter output,
        TextWriter error, CancellationToken cancellationToken = default)
    {
        var parsed = CliArguments.Parse(arguments);
        if (parsed.Flag("help"))
        {
            Usage.Write(output, parsed.Words);
            return ExitSuccess;
        }
        if (parsed.Words.Count == 0)
        {
            Usage.Write(output, []);
            return ExitUsage;
        }

        try
        {
            using var context = new CliContext(parsed, output, error);
            return await DispatchAsync(context, parsed, cancellationToken).ConfigureAwait(false);
        }
        catch (CliUsageException exception)
        {
            error.WriteLine($"error: {exception.Message}");
            error.WriteLine($"Run '{Usage.CommandName} --help' for usage.");
            return ExitUsage;
        }
        catch (DebugApiException exception)
        {
            error.WriteLine($"server error: {exception.Message}");
            return ExitFailure;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            error.WriteLine("canceled");
            return ExitCanceled;
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            error.WriteLine($"error: {exception.Message}");
            error.WriteLine($"Run '{Usage.CommandName} --help' for usage.");
            return ExitUsage;
        }
        catch (Exception exception) when (exception is HttpRequestException or
                                          TaskCanceledException or
                                          System.Net.WebSockets.WebSocketException or
                                          IOException or
                                          TimeoutException or
                                          InvalidOperationException)
        {
            error.WriteLine($"error: {exception.Message}");
            return ExitFailure;
        }
    }

    private static Task<int> DispatchAsync(CliContext context, CliArguments arguments,
        CancellationToken cancellationToken)
    {
        var words = arguments.Words;
        var command = words[0].ToLowerInvariant();
        var nested = command is "led" or "gateway" or "protocol";

        if (nested)
        {
            if (words.Count < 2)
            {
                throw new CliUsageException($"'{command}' needs a subcommand.");
            }
            if (words.Count > 2)
            {
                throw new CliUsageException($"Unexpected argument '{words[2]}'.");
            }
        }
        else if (command != "help" && words.Count > 1)
        {
            throw new CliUsageException($"Unexpected argument '{words[1]}'.");
        }

        return command switch
        {
            "health" => ServerCommands.HealthAsync(context, arguments, cancellationToken),
            "state" => ServerCommands.StateAsync(context, arguments, cancellationToken),
            "show" => ServerCommands.ShowAsync(context, arguments, cancellationToken),
            "led" => LedAsync(context, arguments, words[1], cancellationToken),
            "gateway" => GatewayAsync(context, arguments, words[1], cancellationToken),
            "protocol" => ProtocolAsync(context, arguments, words[1], cancellationToken),
            "version" => Version(context, arguments),
            "help" => Help(context, words),
            _ => throw new CliUsageException($"Unknown command '{words[0]}'.")
        };
    }

    private static Task<int> LedAsync(CliContext context, CliArguments arguments,
        string subcommand, CancellationToken cancellationToken) =>
        subcommand.ToLowerInvariant() switch
        {
            "now" => LedCommands.NowAsync(context, arguments, cancellationToken),
            "set" => LedCommands.SetAsync(context, arguments, cancellationToken),
            "cancel" => LedCommands.CancelAsync(context, arguments, cancellationToken),
            "reset" => LedCommands.ResetAsync(context, arguments, cancellationToken),
            _ => throw new CliUsageException($"Unknown 'led' subcommand '{subcommand}'.")
        };

    private static Task<int> GatewayAsync(CliContext context, CliArguments arguments,
        string subcommand, CancellationToken cancellationToken) =>
        subcommand.ToLowerInvariant() switch
        {
            "run" => GatewayCommands.RunAsync(context, arguments, cancellationToken),
            _ => throw new CliUsageException($"Unknown 'gateway' subcommand '{subcommand}'.")
        };

    private static Task<int> ProtocolAsync(CliContext context, CliArguments arguments,
        string subcommand, CancellationToken cancellationToken) =>
        subcommand.ToLowerInvariant() switch
        {
            "ops" => ProtocolCommands.OpsAsync(context, arguments, cancellationToken),
            "decode" => ProtocolCommands.DecodeAsync(context, arguments, cancellationToken),
            "encode" => ProtocolCommands.EncodeAsync(context, arguments, cancellationToken),
            _ => throw new CliUsageException($"Unknown 'protocol' subcommand '{subcommand}'.")
        };

    private static Task<int> Version(CliContext context, CliArguments arguments)
    {
        context.ValidateOptions();
        context.Out.WriteLine(Usage.Version);
        return Task.FromResult(ExitSuccess);
    }

    private static Task<int> Help(CliContext context, IReadOnlyList<string> words)
    {
        Usage.Write(context.Out, words.Skip(1).ToArray());
        return Task.FromResult(ExitSuccess);
    }
}
