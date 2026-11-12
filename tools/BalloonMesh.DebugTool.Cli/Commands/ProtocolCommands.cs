using BalloonMesh.DebugTool.Core;

namespace BalloonMesh.DebugTool.Cli.Commands;

/// <summary>Offline Protocol B encode/decode commands.</summary>
public static class ProtocolCommands
{
    private static readonly string[] EncodeOptions =
    [
        "op", "payload", "at", "cutoff", "color", "r", "g", "b", "anchor",
        "distance", "batch", "timestamp", "kind", "count", "index", "neighbor"
    ];

    public static Task<int> OpsAsync(CliContext context, CliArguments arguments,
        CancellationToken cancellationToken)
    {
        context.ValidateOptions();
        if (context.Json)
        {
            context.WriteJson(ProtocolInspector.KnownOps);
            return Task.FromResult(CliApplication.ExitSuccess);
        }
        foreach (var op in ProtocolInspector.KnownOps)
        {
            context.Out.WriteLine(
                $"{op.Name,-20} 0x{op.Op:X2}  payload={op.PayloadLength}  {op.Description}");
        }
        return Task.FromResult(CliApplication.ExitSuccess);
    }

    public static Task<int> DecodeAsync(CliContext context, CliArguments arguments,
        CancellationToken cancellationToken)
    {
        context.ValidateOptions("opcode", "payload");
        var opcode = ValueParsers.ParseUInt32(arguments.RequireValue("opcode"), "opcode");
        var decoded = ProtocolInspector.Decode(opcode, arguments.Value("payload"));
        if (context.Json)
        {
            context.WriteJson(decoded);
            return Task.FromResult(CliApplication.ExitSuccess);
        }
        context.Out.WriteLine($"{decoded.Name}: {decoded.Summary}");
        foreach (var field in decoded.Fields)
        {
            context.Out.WriteLine($"  {field.Name,-18} {field.Value}");
        }
        foreach (var warning in decoded.Warnings)
        {
            context.Error.WriteLine($"warning: {warning}");
        }
        return Task.FromResult(CliApplication.ExitSuccess);
    }

    public static Task<int> EncodeAsync(CliContext context, CliArguments arguments,
        CancellationToken cancellationToken)
    {
        context.ValidateOptions(EncodeOptions);
        var encoded = ProtocolCommandBuilder.Build(arguments.RequireValue("op"), arguments);
        if (context.Json)
        {
            context.WriteJson(new
            {
                op = encoded.Op,
                encoded.Name,
                opcode = encoded.OpcodeHex,
                payload = encoded.PayloadHex
            });
            return Task.FromResult(CliApplication.ExitSuccess);
        }
        context.Out.WriteLine($"op    {encoded.Name} (0x{encoded.Op:X2})");
        context.Out.WriteLine($"opcode  {encoded.OpcodeHex}");
        context.Out.WriteLine($"payload {encoded.PayloadHex}");
        return Task.FromResult(CliApplication.ExitSuccess);
    }
}
