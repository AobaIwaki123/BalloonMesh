using System.Globalization;
using Avalonia.Media;
using BalloonMesh.DebugTool.Core;
using CommunityToolkit.Mvvm.Input;

namespace BalloonMesh.DebugTool.ViewModels;

public sealed class BalloonRowViewModel
{
    public BalloonRowViewModel(BalloonInfo info)
    {
        Info = info;
        ColorBrush = new SolidColorBrush(Color.FromRgb(info.R, info.G, info.B));
    }

    public BalloonInfo Info { get; }

    public string Name => Info.Name;
    public string AddressText => $"0x{Info.MeshAddress:X4}";
    public string TargetHex => $"0x{Info.MeshAddress:X4}";
    public string GroupName => Info.GroupName;
    public string LatText => Info.Lat?.ToString("F6", CultureInfo.InvariantCulture) ?? "-";
    public string LonText => Info.Lon?.ToString("F6", CultureInfo.InvariantCulture) ?? "-";
    public string ConfidenceText => Info.Confidence?.ToString("F2", CultureInfo.InvariantCulture) ?? "-";
    public string ColorHex => $"#{Info.R:X2}{Info.G:X2}{Info.B:X2}";
    public IBrush ColorBrush { get; }
    public string QueueText => $"{Info.QueueCount}/{Info.EstimatedQueueCount}";
    public string OnlineText => Info.Online ? "online" : "offline";
}

public sealed class GatewayRowViewModel
{
    public GatewayRowViewModel(GatewayInfo info)
    {
        Info = info;
    }

    public GatewayInfo Info { get; }
    public string Id => Info.Id;
    public string TransportText => Info.WebSocket ? "WS" : "HTTP";
    public string Remote => Info.Remote ?? "-";
    public string Firmware => Info.Firmware ?? "-";
    public string LastSeenText => Info.LastSeen.ToLocalTime()
        .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    public string PendingText => Info.PendingCommands.ToString(CultureInfo.InvariantCulture);
    public string ProvisionedText => Info.Provisioned ? "yes" : "no";
    public string SpoolText => Info.SpoolPending.ToString(CultureInfo.InvariantCulture);
}

public sealed class ShowStepRowViewModel
{
    public ShowStepRowViewModel(ShowStepInfo info, Func<Task> cancel)
    {
        Info = info;
        ColorBrush = new SolidColorBrush(Color.FromRgb(info.R, info.G, info.B));
        CancelCommand = new AsyncRelayCommand(cancel);
    }

    public ShowStepInfo Info { get; }
    public long Id => Info.Id;
    public string Name => Info.Name;
    public string Target => $"{Info.TargetKind}:{Info.Target}";
    public string ExecuteAtText => Info.ExecuteAt.ToLocalTime()
        .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    public string ColorHex => $"#{Info.R:X2}{Info.G:X2}{Info.B:X2}";
    public IBrush ColorBrush { get; }
    public string Status => Info.Status;
    public IAsyncRelayCommand CancelCommand { get; }
}

public sealed class ReceivedCommandRowViewModel
{
    public ReceivedCommandRowViewModel(ReceivedGatewayCommand received,
        Action<GatewayCommandInfo> openInProtocol)
    {
        TimeText = received.ReceivedAt.ToLocalTime()
            .ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
        DstText = $"0x{received.Command.Dst:X4}";
        OpText = received.Decoded.Name;
        Summary = received.Decoded.Summary;
        Payload = received.Command.Payload;
        RawJson = received.RawJson;
        OpenInProtocolCommand = new RelayCommand(() => openInProtocol(received.Command));
    }

    public string TimeText { get; }
    public string DstText { get; }
    public string OpText { get; }
    public string Summary { get; }
    public string Payload { get; }
    public string RawJson { get; }
    public IRelayCommand OpenInProtocolCommand { get; }
}

public sealed class DecodedLogEntryViewModel
{
    public DecodedLogEntryViewModel(DateTimeOffset timestamp, string direction,
        GatewayTransport? transport, DecodedMessage decoded, string rawJson)
    {
        TimeText = timestamp.ToLocalTime()
            .ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
        Direction = direction;
        Transport = ProtocolInspector.TransportDisplayName(transport);
        RawJsonLabel = ProtocolInspector.RawJsonLabel(transport);
        Name = decoded.Name;
        OpcodeText = $"0x{decoded.Opcode:X6}";
        Fields = ProtocolInspector.FormatDecoded(decoded);
        RawJson = rawJson;
    }

    public string TimeText { get; }
    public string Direction { get; }
    public string Transport { get; }
    public string RawJsonLabel { get; }
    public string Name { get; }
    public string OpcodeText { get; }
    public string Fields { get; }
    public string RawJson { get; }
    public string Text => $"{TimeText} [{Direction}/{Transport}] {OpcodeText} {Fields}";
    public string SearchText => $"{Direction} {Transport} {OpcodeText} {Name} {Fields} {RawJson}";
}

public sealed class LogLineViewModel
{
    public LogLineViewModel(DateTimeOffset timestamp, string level, string message)
    {
        TimeText = timestamp.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
        Level = level;
        Message = message;
    }

    public string TimeText { get; }
    public string Level { get; }
    public string Message { get; }
    public string Text => $"{TimeText} [{Level.ToUpperInvariant()}] {Message}";
}

public sealed record ProtocolFieldRow(string Name, string Value);
