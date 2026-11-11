using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Threading;
using BalloonMesh.DebugTool.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BalloonMesh.DebugTool.ViewModels;

public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private const int MaxLogLines = 500;
    private const int MaxReceivedCommands = 200;

    private readonly DebugToolSettings _settings;
    private DebugApiClient? _client;
    private bool _warnedInsecureCredentials;
    private bool _disposed;

    public MainWindowViewModel(DebugToolSettings? settings = null)
    {
        _settings = settings ?? new DebugToolSettings();

        ServerUrl = _settings.ServerUrl;
        MockRole = _settings.MockRole;
        GatewayId = _settings.GatewayId;
        GatewayTransportMode = _settings.GatewayTransport;
        GatewayFirmware = _settings.GatewayFirmware;
        Provisioned = _settings.Provisioned;
        SpoolPending = _settings.SpoolPending.ToString(CultureInfo.InvariantCulture);
        PollWaitMs = _settings.PollWaitMs.ToString(CultureInfo.InvariantCulture);
        TargetKind = _settings.TargetKind;
        Target = _settings.Target;
        ResetCutoff = _settings.ResetCutoff;
        InjectSource = _settings.InjectSource;
        InjectDst = _settings.InjectDst;
        FtmAnchor = _settings.FtmAnchor;
        FtmDistanceCm = _settings.FtmDistanceCm;
        FtmBatchId = _settings.FtmBatchId;
        QueueKind = _settings.QueueKind;
        QueueCount = _settings.QueueCount;
        NeighborBatchId = _settings.NeighborBatchId;
        NeighborPacketIndex = _settings.NeighborPacketIndex;
        NeighborA = _settings.NeighborA;
        NeighborARssi = _settings.NeighborARssi;
        NeighborB = _settings.NeighborB;
        NeighborBRssi = _settings.NeighborBRssi;
        UpdateCommandColor();

        AddLog("info", "BalloonMesh debug tool started. Enter the server URL and press Connect.");
    }

    public ObservableCollection<BalloonRowViewModel> Balloons { get; } = [];

    public ObservableCollection<GatewayRowViewModel> Gateways { get; } = [];

    public ObservableCollection<ShowStepRowViewModel> ShowSteps { get; } = [];

    public ObservableCollection<ReceivedCommandRowViewModel> ReceivedCommands { get; } = [];

    public ObservableCollection<LogLineViewModel> Log { get; } = [];

    public IReadOnlyList<string> MockRoles { get; } = ["Admin", "Operator", "Viewer"];

    [ObservableProperty]
    private string _serverUrl = "http://localhost:5080";

    [ObservableProperty]
    private string _gatewayToken = string.Empty;

    [ObservableProperty]
    private string _authCookie = string.Empty;

    [ObservableProperty]
    private string _mockRole = "Admin";

    [ObservableProperty]
    private string _statusText = "Not connected.";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _serverVersion = "-";

    [ObservableProperty]
    private string _simulationStatusText = "Demo: unknown";

    [ObservableProperty]
    private string _balloonSummary = "No data. Connect and refresh.";

    [ObservableProperty]
    private string _gatewaySummary = "No gateways.";

    [ObservableProperty]
    private string _showStepSummary = "No show steps.";

    [ObservableProperty]
    private BalloonRowViewModel? _selectedBalloon;

    [ObservableProperty]
    private int _selectedTabIndex;

    [RelayCommand]
    private Task ConnectAsync() => RunAsync("Connect", async () =>
    {
        var client = GetClient();
        var health = await client.GetHealthAsync().ConfigureAwait(true);
        ServerVersion = health.Version;
        StatusText = $"Connected to {client.BaseUri} (server {health.Version}).";
        AddLog("info", StatusText);

        try
        {
            await RefreshCoreAsync(client).ConfigureAwait(true);
        }
        catch (DebugApiException exception) when (exception.StatusCode is
            System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
        {
            AddLog("warn", exception.Message);
            StatusText = "Connected. Sign in with Mock login or a session cookie to load data.";
        }

        SaveSettings();
    });

    [RelayCommand]
    private Task RefreshAsync() => RunAsync("Refresh", async () =>
    {
        var client = GetClient();
        await RefreshCoreAsync(client).ConfigureAwait(true);
        StatusText = $"Refreshed at {DateTimeOffset.Now:T}.";
        AddLog("info", StatusText);
    });

    [RelayCommand]
    private Task LoginMockAsync() => RunAsync("Mock login", async () =>
    {
        var client = GetClient();
        await client.LoginMockAsync(MockRole).ConfigureAwait(true);
        AuthCookie = string.Empty;
        StatusText = $"Signed in through the mock endpoint as {MockRole}.";
        AddLog("info", StatusText);
        if (client.BaseUri.Scheme == "http" && !client.BaseUri.IsLoopback)
        {
            AddLog("warn",
                "The mock-login cookie will be sent over plain HTTP to a non-loopback host. Use HTTPS.");
        }
        await RefreshCoreAsync(client).ConfigureAwait(true);
        SaveSettings();
    });

    [RelayCommand]
    private void ClearLog() => Log.Clear();

    [RelayCommand]
    private Task StartDemoAsync() => RunAsync("Start demo", async () =>
    {
        var client = GetClient();
        await client.SetDemoRunningAsync(true).ConfigureAwait(true);
        SimulationStatusText = "Demo: running";
        AddLog("info", "Demo simulation started.");
    });

    [RelayCommand]
    private Task StopDemoAsync() => RunAsync("Stop demo", async () =>
    {
        var client = GetClient();
        await client.SetDemoRunningAsync(false).ConfigureAwait(true);
        SimulationStatusText = "Demo: stopped";
        AddLog("info", "Demo simulation stopped.");
    });

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        SaveSettings();

        if (_emulator is not null)
        {
            DetachEmulator(_emulator);
            _emulator.Dispose();
            _emulator = null;
        }
        _client?.Dispose();
        _client = null;
    }

    partial void OnSelectedBalloonChanged(BalloonRowViewModel? value)
    {
        if (value is null)
        {
            return;
        }
        TargetKind = "balloon";
        Target = value.TargetHex;
        InjectSource = value.TargetHex;
    }

    private async Task RefreshCoreAsync(DebugApiClient client)
    {
        var stateTask = client.GetStateAsync();
        var showTask = client.GetShowStepsAsync(50);
        await Task.WhenAll(stateTask, showTask).ConfigureAwait(true);
        var state = await stateTask.ConfigureAwait(true);
        var show = await showTask.ConfigureAwait(true);

        ServerVersion = state.Version;
        SimulationStatusText = state.SimulationRunning ? "Demo: running" : "Demo: stopped";

        var selectedAddress = SelectedBalloon?.Info.MeshAddress;
        ReplaceCollection(Balloons, state.Balloons
            .OrderBy(balloon => balloon.MeshAddress)
            .Select(balloon => new BalloonRowViewModel(balloon)));
        ReplaceCollection(Gateways, state.Gateways
            .Select(gateway => new GatewayRowViewModel(gateway)));
        ReplaceCollection(ShowSteps, show
            .Select(step => new ShowStepRowViewModel(step, () => CancelShowStepAsync(step.Id))));

        BalloonSummary = $"{Balloons.Count} balloons, {state.Anchors.Count} anchors, " +
                         $"{state.Groups.Count} groups. Server time {state.ServerTime.ToLocalTime():T}.";
        GatewaySummary = Gateways.Count == 0
            ? "No gateways connected."
            : $"{Gateways.Count} gateway(s).";
        ShowStepSummary = ShowSteps.Count == 0
            ? "No queued show steps."
            : $"{ShowSteps.Count} show step(s).";

        if (selectedAddress is { } address)
        {
            SelectedBalloon = Balloons.FirstOrDefault(balloon =>
                balloon.Info.MeshAddress == address);
        }
    }

    private Task CancelShowStepAsync(long id) => RunAsync("Cancel show step", async () =>
    {
        var client = GetClient();
        var result = await client.CancelShowStepAsync(id).ConfigureAwait(true);
        if (result is null)
        {
            AddLog("warn", $"Show step {id} no longer exists.");
        }
        else
        {
            CommandResult = FormatCommandResult(result);
            AddLog(result.Accepted ? "info" : "warn", CommandResult);
        }
        await RefreshCoreAsync(client).ConfigureAwait(true);
    });

    private DebugApiClient GetClient()
    {
        var baseUri = ParseServerUri(ServerUrl);
        if (_client is null || _client.BaseUri != baseUri)
        {
            _client?.Dispose();
            _client = new DebugApiClient(baseUri);
        }
        _client.GatewayToken = NullIfBlank(GatewayToken);
        _client.AuthCookie = NullIfBlank(AuthCookie);
        WarnIfInsecureCredentials(baseUri);
        return _client;
    }

    private void WarnIfInsecureCredentials(Uri baseUri)
    {
        if (_warnedInsecureCredentials || baseUri.Scheme != "http" || baseUri.IsLoopback)
        {
            return;
        }
        if (string.IsNullOrWhiteSpace(GatewayToken) && string.IsNullOrWhiteSpace(AuthCookie))
        {
            return;
        }
        _warnedInsecureCredentials = true;
        AddLog("warn",
            "Credentials will be sent over plain HTTP to a non-loopback host. Use HTTPS.");
    }

    partial void OnServerUrlChanged(string value) => _warnedInsecureCredentials = false;

    private static Uri ParseServerUri(string text)
    {
        if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri))
        {
            throw new ArgumentException("Server URL must be an absolute http(s) URL.");
        }
        return DebugApiClient.NormalizeBaseUri(uri);
    }

    private async Task RunAsync(string operation, Func<Task> action)
    {
        if (IsBusy)
        {
            AddLog("warn", $"{operation} ignored: another operation is still running.");
            return;
        }
        IsBusy = true;
        try
        {
            await action().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            AddLog("warn", $"{operation} was cancelled.");
        }
        catch (Exception exception)
        {
            StatusText = $"{operation} failed: {exception.Message}";
            AddLog("error", StatusText);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SaveSettings()
    {
        _settings.ServerUrl = ServerUrl.Trim();
        _settings.MockRole = MockRole;
        _settings.GatewayId = GatewayId.Trim();
        _settings.GatewayTransport = GatewayTransportMode;
        _settings.GatewayFirmware = GatewayFirmware.Trim();
        _settings.Provisioned = Provisioned;
        _settings.SpoolPending = TryParseInt(SpoolPending) ?? _settings.SpoolPending;
        _settings.PollWaitMs = TryParseInt(PollWaitMs) ?? _settings.PollWaitMs;
        _settings.TargetKind = TargetKind;
        _settings.Target = Target;
        _settings.ResetCutoff = ResetCutoff;
        _settings.InjectSource = InjectSource;
        _settings.InjectDst = InjectDst;
        _settings.FtmAnchor = FtmAnchor;
        _settings.FtmDistanceCm = FtmDistanceCm;
        _settings.FtmBatchId = FtmBatchId;
        _settings.QueueKind = QueueKind;
        _settings.QueueCount = QueueCount;
        _settings.NeighborBatchId = NeighborBatchId;
        _settings.NeighborPacketIndex = NeighborPacketIndex;
        _settings.NeighborA = NeighborA;
        _settings.NeighborARssi = NeighborARssi;
        _settings.NeighborB = NeighborB;
        _settings.NeighborBRssi = NeighborBRssi;

        try
        {
            _settings.Save();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AddLog("warn", $"Could not save settings: {exception.Message}");
        }
    }

    private void AddLog(string level, string message)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => AddLog(level, message));
            return;
        }
        Log.Add(new LogLineViewModel(DateTimeOffset.Now, level, message));
        while (Log.Count > MaxLogLines)
        {
            Log.RemoveAt(0);
        }
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }

    private static string FormatCommandResult(CommandResult result) =>
        $"{(result.Accepted ? "OK" : "REJECTED")}: {result.Message} " +
        $"(opcode {result.Opcode ?? "-"}, payload {result.Payload ?? "-"}, " +
        $"gateways {result.Gateways}, balloons {result.Balloons})";

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int? TryParseInt(string text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static byte ParseByteValue(string text, string name) =>
        (byte)ParseIntValue(text, name, byte.MinValue, byte.MaxValue);

    private static sbyte ParseRssi(string text, string name) =>
        (sbyte)ParseIntValue(text, name, sbyte.MinValue, sbyte.MaxValue);

    private static int ParseIntValue(string text, string name, int minimum, int maximum)
    {
        if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var value) || value < minimum || value > maximum)
        {
            throw new ArgumentException($"{name} must be an integer between {minimum} and {maximum}.");
        }
        return value;
    }

    private static ushort ParseUShortValue(string text, string name)
    {
        var trimmed = text.Trim();
        var parsed = trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? ushort.TryParse(trimmed[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                out var value)
            : ushort.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out value);
        if (!parsed)
        {
            throw new ArgumentException($"{name} must be a 16-bit integer.");
        }
        return value;
    }

    private static ushort ParseAddress(string text, string name, bool allowZero = true)
    {
        var value = ParseUShortValue(text, name);
        if (!allowZero && value == 0)
        {
            throw new ArgumentException($"{name} must not be zero.");
        }
        return value;
    }

    private static uint ParseUInt32Value(string text, string name)
    {
        var trimmed = text.Trim();
        var parsed = trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? uint.TryParse(trimmed[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                out var value)
            : uint.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out value);
        if (!parsed)
        {
            throw new ArgumentException($"{name} must be a 32-bit integer.");
        }
        return value;
    }

    private uint ParseOpcode(string text)
    {
        var value = ParseUInt32Value(text, "Opcode");
        return ExpandShortOpcode && value <= byte.MaxValue
            ? ProtocolInspector.ToVendorOpcode((byte)value)
            : value;
    }

    private static DateTimeOffset ParseUtc(string text, string name)
    {
        if (!DateTimeOffset.TryParse(text.Trim(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var value))
        {
            throw new ArgumentException(
                $"{name} must be an ISO 8601 date/time, for example 2026-01-01T00:00:00Z.");
        }
        return value;
    }

    private static DateTimeOffset ParseUtcOr(string text, string name,
        DateTimeOffset fallback) =>
        string.IsNullOrWhiteSpace(text) ? fallback : ParseUtc(text, name);
}
