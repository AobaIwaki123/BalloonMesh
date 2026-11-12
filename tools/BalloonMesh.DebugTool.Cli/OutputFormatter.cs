using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using BalloonMesh.DebugTool.Core;

namespace BalloonMesh.DebugTool.Cli;

/// <summary>Human-readable tables and JSON-lines frames for the CLI output.</summary>
public static class OutputFormatter
{
    private static readonly JsonSerializerOptions LineJson = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static void WriteCommandResult(CliContext context, CommandResult result)
    {
        if (context.Json)
        {
            context.WriteJson(result);
            return;
        }
        context.Out.WriteLine(result.Accepted ? "accepted" : "rejected");
        context.Out.WriteLine($"message   {result.Message}");
        context.Out.WriteLine($"gateways  {result.Gateways}");
        context.Out.WriteLine($"balloons  {result.Balloons}");
        if (!string.IsNullOrWhiteSpace(result.Opcode))
        {
            context.Out.WriteLine($"opcode    {result.Opcode}");
        }
        if (!string.IsNullOrWhiteSpace(result.Payload))
        {
            context.Out.WriteLine($"payload   {result.Payload}");
        }
    }

    public static void WriteFleetState(CliContext context, FleetState state)
    {
        if (context.Json)
        {
            context.WriteJson(state);
            return;
        }
        var writer = context.Out;
        writer.WriteLine($"server     {state.Version}  time={state.ServerTime:O}");
        writer.WriteLine($"simulation {(state.SimulationRunning ? "running" : "stopped")}");
        writer.WriteLine();

        writer.WriteLine($"Balloons ({state.Balloons.Count})");
        foreach (var balloon in state.Balloons)
        {
            writer.WriteLine(
                $"  0x{balloon.MeshAddress:X4}  {balloon.Name,-12} group={balloon.GroupId} {balloon.GroupName}");
            writer.WriteLine(
                $"           rgb=#{balloon.R:X2}{balloon.G:X2}{balloon.B:X2}  queue={balloon.QueueCount}" +
                $" (est {balloon.EstimatedQueueCount})  {(balloon.Online ? "online" : "offline")}");
        }

        writer.WriteLine();
        writer.WriteLine($"Anchors ({state.Anchors.Count})");
        foreach (var anchor in state.Anchors)
        {
            writer.WriteLine(
                $"  {anchor.AnchorId}  {anchor.Name,-12} lat={anchor.Lat.ToString("F6", CultureInfo.InvariantCulture)}" +
                $" lon={anchor.Lon.ToString("F6", CultureInfo.InvariantCulture)}" +
                $" alt={anchor.AltM.ToString("F1", CultureInfo.InvariantCulture)}m" +
                $" time={(anchor.TimeValid ? "valid" : "invalid")}" +
                $" pos={(anchor.PositionValid ? "valid" : "invalid")}");
        }

        writer.WriteLine();
        writer.WriteLine($"Gateways ({state.Gateways.Count})");
        foreach (var gateway in state.Gateways)
        {
            writer.WriteLine(
                $"  {gateway.Id,-18} {(gateway.WebSocket ? "ws" : "http"),-4}" +
                $" firmware={gateway.Firmware ?? "-"}  lastSeen={gateway.LastSeen:O}");
            writer.WriteLine(
                $"                     pending={gateway.PendingCommands}" +
                $" provisioned={gateway.Provisioned} spool={gateway.SpoolPending}");
        }

        writer.WriteLine();
        writer.WriteLine($"Groups ({state.Groups.Count})");
        foreach (var group in state.Groups)
        {
            writer.WriteLine(
                $"  {group.Id}  {group.Name,-16} mesh=0x{group.MeshGroupAddress:X4} balloons={group.BalloonCount}");
        }
    }

    public static void WriteShowSteps(CliContext context, IReadOnlyList<ShowStepInfo> steps)
    {
        if (context.Json)
        {
            context.WriteJson(steps);
            return;
        }
        context.Out.WriteLine($"Show steps ({steps.Count})");
        foreach (var step in steps)
        {
            context.Out.WriteLine(
                $"  {step.Id,4}  {step.ExecuteAt:O}  {step.Status,-10} {step.TargetKind}:{step.Target}");
            context.Out.WriteLine(
                $"        {step.Name}  rgb=#{step.R:X2}{step.G:X2}{step.B:X2}");
        }
    }

    public static void WriteGatewayLog(CliContext context, string message)
    {
        if (context.Json)
        {
            context.Out.WriteLine(JsonSerializer.Serialize(new
            {
                type = "log",
                at = DateTimeOffset.UtcNow,
                message
            }, LineJson));
            return;
        }
        context.Out.WriteLine($"[{DateTimeOffset.Now:HH:mm:ss}] {message}");
    }

    public static void WriteGatewayConnection(CliContext context, GatewayConnectionState state)
    {
        if (context.Json)
        {
            context.Out.WriteLine(JsonSerializer.Serialize(new
            {
                type = "connection",
                at = DateTimeOffset.UtcNow,
                state.Connected,
                state.Description
            }, LineJson));
            return;
        }
        context.Out.WriteLine(
            $"[{DateTimeOffset.Now:HH:mm:ss}] {(state.Connected ? "connected" : "disconnected")}: {state.Description}");
    }

    public static void WriteGatewayCommand(CliContext context, ReceivedGatewayCommand received)
    {
        if (context.Json)
        {
            context.Out.WriteLine(JsonSerializer.Serialize(new
            {
                type = "command",
                at = received.ReceivedAt,
                dst = $"0x{received.Command.Dst:X4}",
                opcode = $"0x{received.Command.Opcode:X6}",
                payload = received.Command.Payload,
                ttl = received.Command.Ttl,
                decoded = ProtocolInspector.FormatDecoded(received.Decoded),
                raw = received.RawJson
            }, LineJson));
            return;
        }
        context.Out.WriteLine(
            $"[{received.ReceivedAt:HH:mm:ss}] RX dst=0x{received.Command.Dst:X4}" +
            $" opcode=0x{received.Command.Opcode:X6} {ProtocolInspector.FormatDecoded(received.Decoded)}");
        if (context.Verbose)
        {
            context.Out.WriteLine($"           raw {received.RawJson}");
        }
    }

    public static void WriteGatewayEvent(CliContext context, SentGatewayEvent sent)
    {
        if (context.Json)
        {
            context.Out.WriteLine(JsonSerializer.Serialize(new
            {
                type = "event",
                at = sent.SentAt,
                src = $"0x{sent.Message.Src:X4}",
                dst = $"0x{sent.Message.Dst:X4}",
                opcode = $"0x{sent.Message.Opcode:X6}",
                payload = sent.Message.Payload,
                rssi = sent.Message.Rssi,
                ttl = sent.Message.Ttl,
                decoded = ProtocolInspector.FormatDecoded(sent.Decoded),
                raw = sent.RawJson
            }, LineJson));
            return;
        }
        context.Out.WriteLine(
            $"[{sent.SentAt:HH:mm:ss}] TX src=0x{sent.Message.Src:X4}" +
            $" opcode=0x{sent.Message.Opcode:X6} {ProtocolInspector.FormatDecoded(sent.Decoded)}");
        if (context.Verbose)
        {
            context.Out.WriteLine($"           raw {sent.RawJson}");
        }
    }
}
