using Avalonia.Threading;
using BalloonMesh.DebugTool.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BalloonMesh.DebugTool.ViewModels;

public partial class MainWindowViewModel
{
    private GatewayEmulator? _emulator;
    private Action<string>? _emulatorLogHandler;
    private Action<GatewayConnectionState>? _emulatorConnectionHandler;
    private Action<ReceivedGatewayCommand>? _emulatorCommandHandler;
    private Action<SentGatewayEvent>? _emulatorEventHandler;

    public IReadOnlyList<string> GatewayTransports { get; } = ["WebSocket", "HTTP"];

    public IReadOnlyList<string> QueueKinds { get; } = ["NearFull", "Recovered"];

    [ObservableProperty]
    private string _gatewayId = "debug-gateway";

    [ObservableProperty]
    private string _gatewayTransportMode = "WebSocket";

    [ObservableProperty]
    private string _gatewayFirmware = "debugtool/1.0";

    [ObservableProperty]
    private bool _provisioned = true;

    [ObservableProperty]
    private string _spoolPending = "0";

    [ObservableProperty]
    private string _pollWaitMs = "10000";

    [ObservableProperty]
    private bool _emulatorRunning;

    [ObservableProperty]
    private string _emulatorStatus = "Stopped.";

    [ObservableProperty]
    private string _injectSource = "0x0101";

    [ObservableProperty]
    private string _injectDst = "0xC011";

    [ObservableProperty]
    private string _ftmAnchor = "1";

    [ObservableProperty]
    private string _ftmDistanceCm = "3000";

    [ObservableProperty]
    private string _ftmTimestamp = string.Empty;

    [ObservableProperty]
    private string _ftmBatchId = "1";

    [ObservableProperty]
    private string _queueKind = "NearFull";

    [ObservableProperty]
    private string _queueCount = "401";

    [ObservableProperty]
    private string _neighborBatchId = "1";

    [ObservableProperty]
    private string _neighborPacketIndex = "0";

    [ObservableProperty]
    private string _neighborA = "0x0102";

    [ObservableProperty]
    private string _neighborARssi = "-45";

    [ObservableProperty]
    private string _neighborB = "0x0103";

    [ObservableProperty]
    private string _neighborBRssi = "-60";

    [RelayCommand]
    private Task StartEmulatorAsync() => RunAsync("Start gateway emulator", async () =>
    {
        if (_emulator is { IsRunning: true })
        {
            throw new InvalidOperationException("The gateway emulator is already running.");
        }
        if (_emulator is not null)
        {
            var stale = _emulator;
            _emulator = null;
            DetachEmulator(stale);
            await stale.DisposeAsync().ConfigureAwait(true);
        }
        EmulatorStatus = "Starting...";
        var options = new GatewayEmulatorOptions(
            ParseServerUri(ServerUrl),
            GatewayId.Trim(),
            NullIfBlank(GatewayToken),
            ParseTransport(),
            NullIfBlank(GatewayFirmware),
            Provisioned,
            ParseIntValue(SpoolPending, "Spool pending", 0, int.MaxValue),
            ParseIntValue(PollWaitMs, "Poll wait", 0, GatewayEmulatorOptions.MaxPollWaitMs));
        if (options.Token is not null && options.ServerUri.Scheme == "http" &&
            !options.ServerUri.IsLoopback)
        {
            AddLog("warn",
                "The gateway token will be sent over plain HTTP to a non-loopback host. Use HTTPS/WSS.");
        }
        var emulator = new GatewayEmulator(options);
        _emulator = emulator;
        AttachEmulator(emulator);
        try
        {
            await emulator.StartAsync().ConfigureAwait(true);
            EmulatorRunning = emulator.IsRunning;
            EmulatorStatus = $"{GatewayTransportMode} gateway '{options.GatewayId}' started.";
            AddLog("info", EmulatorStatus);
            SaveSettings();
        }
        catch (Exception exception)
        {
            DetachEmulator(emulator);
            emulator.Dispose();
            _emulator = null;
            EmulatorRunning = false;
            EmulatorStatus = $"Start failed: {exception.Message}";
            throw;
        }
    });

    [RelayCommand]
    private Task StopEmulatorAsync() => RunAsync("Stop gateway emulator", async () =>
    {
        var emulator = _emulator;
        if (emulator is null)
        {
            EmulatorRunning = false;
            EmulatorStatus = "Not running.";
            return;
        }
        _emulator = null;
        DetachEmulator(emulator);
        await emulator.StopAsync().ConfigureAwait(true);
        await emulator.DisposeAsync().ConfigureAwait(true);
        EmulatorRunning = false;
        EmulatorStatus = "Stopped.";
        AddLog("info", EmulatorStatus);
    });

    [RelayCommand]
    private Task SendFtmAsync() => RunAsync("Send FTM report", async () =>
    {
        var emulator = RequireEmulator();
        var message = BuildFtmMessage(ParseUShortValue(FtmDistanceCm, "Distance cm"),
            ParseByteValue(FtmBatchId, "Batch id"), ParseByteValue(FtmAnchor, "Anchor id"));
        await emulator.SendEventAsync(message).ConfigureAwait(true);
    });

    [RelayCommand]
    private Task SendFtmBatchAsync() => RunAsync("Send FTM batch", async () =>
    {
        var emulator = RequireEmulator();
        var baseDistance = ParseUShortValue(FtmDistanceCm, "Distance cm");
        var batch = ParseByteValue(FtmBatchId, "Batch id");
        var messages = new List<GatewayEventInfo>(4);
        for (byte anchor = 1; anchor <= 4; anchor++)
        {
            var distance = (ushort)Math.Min(ushort.MaxValue, baseDistance + (anchor - 1) * 500);
            messages.Add(BuildFtmMessage(distance, batch, anchor));
        }
        await emulator.SendEventsAsync(messages).ConfigureAwait(true);
        // A batch id is closed after 30 s and ignored for another two minutes;
        // advancing the field keeps repeated batch sends useful.
        if (byte.TryParse(FtmBatchId, out var next))
        {
            FtmBatchId = ((next + 1) % 256).ToString();
            SaveSettings();
        }
    });

    [RelayCommand]
    private Task SendQueueAsync() => RunAsync("Send queue status", async () =>
    {
        var emulator = RequireEmulator();
        var kind = QueueKind switch
        {
            "NearFull" => ProtocolInspector.QueueStatusNearFull,
            "Recovered" => ProtocolInspector.QueueStatusRecovered,
            _ => throw new ArgumentException($"Unknown queue kind '{QueueKind}'.")
        };
        var count = ParseUShortValue(QueueCount, "Queue count");
        var encoded = ProtocolInspector.EncodeQueueStatus(kind, count);
        var message = BuildMessage(encoded);
        await emulator.SendEventAsync(message).ConfigureAwait(true);
    });

    [RelayCommand]
    private Task SendNeighborAsync() => RunAsync("Send neighbor report", async () =>
    {
        var emulator = RequireEmulator();
        var batch = ParseByteValue(NeighborBatchId, "Neighbor batch id");
        var index = ParseByteValue(NeighborPacketIndex, "Neighbor packet index");
        var encoded = ProtocolInspector.EncodeNeighborReport(batch, index,
            BuildNeighborSamples());
        var message = BuildMessage(encoded);
        await emulator.SendEventAsync(message).ConfigureAwait(true);
    });

    private GatewayEmulator RequireEmulator() =>
        _emulator ?? throw new InvalidOperationException("Start the gateway emulator first.");

    private GatewayTransport ParseTransport() =>
        GatewayTransportMode.Equals("HTTP", StringComparison.OrdinalIgnoreCase)
            ? GatewayTransport.Http
            : GatewayTransport.WebSocket;

    private GatewayEventInfo BuildFtmMessage(ushort distanceCm, byte batchId, byte anchorId)
    {
        var timestamp = string.IsNullOrWhiteSpace(FtmTimestamp)
            ? ProtocolInspector.NowTimestamp32()
            : ParseUInt32Value(FtmTimestamp, "FTM timestamp");
        var encoded = ProtocolInspector.EncodeFtmReport(anchorId, distanceCm, timestamp, batchId);
        return BuildMessage(encoded);
    }

    private GatewayEventInfo BuildMessage(EncodedMessage encoded)
    {
        var source = ParseAddress(InjectSource, "Source balloon", allowZero: false);
        var destination = ParseAddress(InjectDst, "Destination group", allowZero: false);
        return new GatewayEventInfo(source, destination, encoded.Opcode, encoded.PayloadHex, -55, 7);
    }

    private IReadOnlyList<NeighborSample> BuildNeighborSamples()
    {
        var samples = new List<NeighborSample>(2)
        {
            new(ParseAddress(NeighborA, "Neighbor A", allowZero: false),
                ParseRssi(NeighborARssi, "Neighbor A RSSI"))
        };
        if (!string.IsNullOrWhiteSpace(NeighborB))
        {
            samples.Add(new NeighborSample(
                ParseAddress(NeighborB, "Neighbor B", allowZero: false),
                ParseRssi(NeighborBRssi, "Neighbor B RSSI")));
        }
        return samples;
    }

    private void AttachEmulator(GatewayEmulator emulator)
    {
        _emulatorLogHandler = message =>
        {
            if (ReferenceEquals(_emulator, emulator))
            {
                AddLog("gateway", message);
            }
        };
        _emulatorConnectionHandler = state => Dispatcher.UIThread.Post(() =>
        {
            // A callback queued before Stop/Start belongs to the old emulator
            // and must not overwrite the current status.
            if (!ReferenceEquals(_emulator, emulator) ||
                (state.Connected && !emulator.IsRunning))
            {
                return;
            }
            EmulatorRunning = state.Connected;
            EmulatorStatus = state.Description;
            AddLog(state.Connected ? "info" : "warn", state.Description);
        });
        _emulatorCommandHandler = received => Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(_emulator, emulator))
            {
                return;
            }
            ReceivedCommands.Add(new ReceivedCommandRowViewModel(received, OpenInProtocol));
            while (ReceivedCommands.Count > MaxReceivedCommands)
            {
                ReceivedCommands.RemoveAt(0);
            }
            AddLog("recv",
                $"RX {received.Command.Dst:X4} {ProtocolInspector.FormatDecoded(received.Decoded)}");
            AddDecodedEntry(received.ReceivedAt, "RX", received.Transport,
                received.Decoded, received.RawJson);
        });
        _emulatorEventHandler = sent => Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(_emulator, emulator))
            {
                return;
            }
            AddLog("send",
                $"TX {sent.Message.Src:X4}->{sent.Message.Dst:X4} " +
                ProtocolInspector.FormatDecoded(sent.Decoded));
            AddDecodedEntry(sent.SentAt, "TX", sent.Transport, sent.Decoded, sent.RawJson);
        });

        emulator.Log += _emulatorLogHandler;
        emulator.ConnectionChanged += _emulatorConnectionHandler;
        emulator.CommandReceived += _emulatorCommandHandler;
        emulator.EventSent += _emulatorEventHandler;
    }

    private void DetachEmulator(GatewayEmulator emulator)
    {
        if (_emulatorLogHandler is { } log) emulator.Log -= log;
        if (_emulatorConnectionHandler is { } connection) emulator.ConnectionChanged -= connection;
        if (_emulatorCommandHandler is { } command) emulator.CommandReceived -= command;
        if (_emulatorEventHandler is { } sent) emulator.EventSent -= sent;
        _emulatorLogHandler = null;
        _emulatorConnectionHandler = null;
        _emulatorCommandHandler = null;
        _emulatorEventHandler = null;
    }
}
