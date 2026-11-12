using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace BalloonMesh.DebugTool.Core;

public sealed record GatewayConnectionState(bool Connected, string Description);

public sealed record ReceivedGatewayCommand(
    DateTimeOffset ReceivedAt,
    GatewayCommandInfo Command,
    DecodedMessage Decoded,
    string RawJson,
    GatewayTransport Transport);

public sealed record SentGatewayEvent(
    DateTimeOffset SentAt,
    GatewayEventInfo Message,
    DecodedMessage Decoded,
    string RawJson,
    GatewayTransport Transport);

/// <summary>
/// A gateway stand-in for debugging without hardware.  WebSocket mode speaks
/// the same hello/mesh envelopes as the ESP32 gateway; HTTP mode uses the
/// event POST + long-poll command endpoints.
/// </summary>
public sealed class GatewayEmulator : IDisposable, IAsyncDisposable
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);
    private readonly GatewayEmulatorOptions _options;
    private readonly DebugApiClient _api;
    private readonly bool _ownsApi;
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly object _gate = new();
    private CancellationTokenSource? _runCts;
    private Task? _loop;
    private ClientWebSocket? _socket;
    private TaskCompletionSource<bool>? _helloAck;
    private bool _loopAlive;
    private long _runGeneration;
    private bool _disposed;

    public GatewayEmulator(GatewayEmulatorOptions options)
        : this(options, apiClient: null)
    {
    }

    /// <summary>
    /// Test seam: inject a client with a custom <see cref="HttpMessageHandler"/>.
    /// The supplied client is still owned by the caller and is not disposed.
    /// </summary>
    public GatewayEmulator(GatewayEmulatorOptions options, DebugApiClient? apiClient)
    {
        options.Validate();
        _options = options;
        _ownsApi = apiClient is null;
        if (apiClient is null)
        {
            _api = new DebugApiClient(options.ServerUri) { GatewayToken = options.Token };
        }
        else
        {
            _api = apiClient;
            _api.GatewayToken = options.Token;
        }
    }

    public event Action<string>? Log;

    public event Action<GatewayConnectionState>? ConnectionChanged;

    public event Action<ReceivedGatewayCommand>? CommandReceived;

    public event Action<SentGatewayEvent>? EventSent;

    public GatewayEmulatorOptions Options => _options;

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _runCts is not null && _loopAlive;
            }
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            CancellationTokenSource cts;
            CancellationTokenSource? staleCts = null;
            lock (_gate)
            {
                if (_runCts is not null)
                {
                    if (_loopAlive)
                    {
                        throw new InvalidOperationException(
                            "The gateway emulator is already running.");
                    }
                    // The previous loop stopped by itself (server disconnect or
                    // polling error). Reclaim its run state so Start works again.
                    staleCts = _runCts;
                    _runCts = null;
                    _loop = null;
                }
                cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _runCts = cts;
            }
            if (staleCts is not null)
            {
                staleCts.Dispose();
                DisposeSocket();
            }

            try
            {
                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                connectCts.CancelAfter(ConnectTimeout);
                try
                {
                    if (_options.Transport == GatewayTransport.WebSocket)
                    {
                        await ConnectWebSocketAsync(_options, connectCts.Token)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        // An empty event batch registers/touches the gateway.
                        await _api.SendGatewayEventsAsync(_options.GatewayId, [],
                            _options.Firmware, _options.Provisioned, _options.SpoolPending,
                            connectCts.Token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when
                    (!cancellationToken.IsCancellationRequested && !cts.IsCancellationRequested)
                {
                    throw new TimeoutException(
                        $"Gateway connection timed out after {ConnectTimeout.TotalSeconds:F0}s.");
                }

                long generation;
                var ack = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                lock (_gate)
                {
                    _helloAck = _options.Transport == GatewayTransport.WebSocket ? ack : null;
                    _loopAlive = true;
                    generation = ++_runGeneration;
                }
                lock (_gate)
                {
                    _loop = _options.Transport == GatewayTransport.WebSocket
                        ? Task.Run(() => ReceiveLoopAsync(cts.Token, generation),
                            CancellationToken.None)
                        : Task.Run(() => PollLoopAsync(cts.Token, generation),
                            CancellationToken.None);
                }

                if (_options.Transport == GatewayTransport.WebSocket)
                {
                    // The receive loop is already running, so a timeout here
                    // leaves the socket usable for older servers that do not
                    // send helloAck.  A close/rejection still fails Start.
                    using var ackTimeout = CancellationTokenSource.CreateLinkedTokenSource(
                        cts.Token);
                    ackTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                    try
                    {
                        await ack.Task.WaitAsync(ackTimeout.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!cts.IsCancellationRequested)
                    {
                        // Older server: assume the still-open socket is usable.
                    }
                }

                lock (_gate)
                {
                    if (!_loopAlive || generation != _runGeneration)
                    {
                        throw new WebSocketException(
                            "The gateway disconnected before the connection was ready.");
                    }
                }
                RaiseConnection(true,
                    $"{TransportName(_options.Transport)} connected as {_options.GatewayId}");
            }
            catch
            {
                lock (_gate)
                {
                    _runCts = null;
                    _loopAlive = false;
                    _loop = null;
                    _helloAck = null;
                }
                cts.Cancel();
                cts.Dispose();
                DisposeSocket();
                throw;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync()
    {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            CancellationTokenSource? cts;
            Task? loop;
            ClientWebSocket? socket;
            lock (_gate)
            {
                cts = _runCts;
                loop = _loop;
                socket = _socket;
                _runCts = null;
                _loop = null;
                _socket = null;
                _loopAlive = false;
            }
            if (cts is null)
            {
                return;
            }

            cts.Cancel();
            if (socket is not null)
            {
                // ClientWebSocket does not allow concurrent sends; CloseAsync is
                // itself a send, so it must share the send gate with uplink sends.
                // Do not wait forever if a send is stuck on a dead connection.
                var gateAcquired = await _sendGate.WaitAsync(TimeSpan.FromSeconds(1))
                    .ConfigureAwait(false);
                if (!gateAcquired)
                {
                    socket.Abort();
                }
                else
                {
                    try
                    {
                        using var closeTimeout = new CancellationTokenSource(
                            TimeSpan.FromSeconds(1));
                        try
                        {
                            if (socket.State is WebSocketState.Open or
                                WebSocketState.CloseReceived)
                            {
                                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure,
                                    "debug tool stopped", closeTimeout.Token)
                                    .ConfigureAwait(false);
                            }
                        }
                        catch (Exception exception) when (exception is WebSocketException or
                                                          OperationCanceledException)
                        {
                            socket.Abort();
                        }
                    }
                    finally
                    {
                        _sendGate.Release();
                    }
                }
                socket.Dispose();
            }

            if (loop is not null)
            {
                try
                {
                    await loop.WaitAsync(StopTimeout).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    RaiseLog("Gateway loop did not stop within the timeout.");
                }
                catch (OperationCanceledException)
                {
                    // Expected when the loop saw the cancellation.
                }
            }

            cts.Dispose();
            RaiseConnection(false, "Stopped");
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public Task SendEventAsync(GatewayEventInfo message,
        CancellationToken cancellationToken = default) =>
        SendEventsAsync([message], cancellationToken);

    public async Task SendEventsAsync(IReadOnlyList<GatewayEventInfo> messages,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsRunning)
        {
            throw new InvalidOperationException("Start the gateway emulator first.");
        }
        if (messages.Count == 0)
        {
            return;
        }

        // Validate and normalise payload hex before any transport is touched.
        var prepared = messages.Select(message =>
        {
            var normalized = message with
            {
                Payload = ProtocolInspector.NormalizePayloadHex(message.Payload)
            };
            return (
                Message: normalized,
                RawJson: GatewayProtocolCodec.CreateUplink(normalized),
                Decoded: ProtocolInspector.Decode(normalized.Opcode, normalized.Payload));
        }).ToArray();

        CancellationToken runToken;
        ClientWebSocket? socket = null;
        lock (_gate)
        {
            runToken = _runCts?.Token ?? throw new InvalidOperationException(
                "The gateway emulator is not running.");
            if (_options.Transport == GatewayTransport.WebSocket)
            {
                socket = _socket ?? throw new InvalidOperationException(
                    "The WebSocket is not connected.");
            }
        }

        // StopAsync cancels the run token, so an in-flight send cannot keep the
        // app shutdown blocked on a dead socket.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, runToken);
        var token = linked.Token;

        if (_options.Transport == GatewayTransport.WebSocket)
        {
            foreach (var item in prepared)
            {
                await SendOnSocketAsync(socket!, item.RawJson, token).ConfigureAwait(false);
                RaiseSentEvent(item.Message, item.Decoded, item.RawJson);
            }
        }
        else
        {
            await _api.SendGatewayEventsAsync(_options.GatewayId,
                prepared.Select(item => item.Message).ToArray(),
                _options.Firmware, _options.Provisioned, _options.SpoolPending,
                token).ConfigureAwait(false);
            foreach (var item in prepared)
            {
                RaiseSentEvent(item.Message, item.Decoded, item.RawJson);
            }
        }


    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
        }
        await StopAsync().ConfigureAwait(false);
        if (_ownsApi)
        {
            _api.Dispose();
        }
        // A send that was aborted by a timed-out Stop may still be unwinding.
        // Only dispose the gate when no send holds it.
        if (_sendGate.CurrentCount == 1)
        {
            _sendGate.Dispose();
        }
        _lifecycleGate.Dispose();
    }

    private async Task ConnectWebSocketAsync(GatewayEmulatorOptions options,
        CancellationToken cancellationToken)
    {
        var uri = GatewayProtocolCodec.BuildWebSocketUri(options.ServerUri);
        var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        try
        {
            await socket.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);
            await SendOnSocketAsync(socket, GatewayProtocolCodec.CreateHello(options),
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
        lock (_gate)
        {
            _socket = socket;
        }
        RaiseLog($"WebSocket connected: {uri}");
    }

    private async Task PollLoopAsync(CancellationToken cancellationToken, long generation)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var commands = await _api.ReadGatewayCommandsAsync(_options.GatewayId,
                    _options.PollWaitMs, cancellationToken).ConfigureAwait(false);
                foreach (var command in commands)
                {
                    RaiseCommand(command, rawJson: null);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        catch (Exception exception)
        {
            RaiseLog($"HTTP polling stopped: {exception.Message}");
        }
        finally
        {
            MarkLoopStopped(generation, "HTTP polling stopped");
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken, long generation)
    {
        ClientWebSocket? socket;
        lock (_gate)
        {
            socket = _socket;
        }
        if (socket is null)
        {
            MarkLoopStopped(generation, "WebSocket is not connected");
            return;
        }

        var buffer = new byte[64 * 1024];
        using var stream = new MemoryStream();
        try
        {
            while (!cancellationToken.IsCancellationRequested &&
                   socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer, cancellationToken)
                    .ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _helloAck?.TrySetException(new WebSocketException(
                        $"Gateway handshake rejected ({result.CloseStatus}: " +
                        $"{result.CloseStatusDescription}). Check the gateway token."));
                    RaiseLog($"Server closed the WebSocket: {result.CloseStatus}");
                    break;
                }
                if (result.MessageType != WebSocketMessageType.Text)
                {
                    continue;
                }
                stream.Write(buffer, 0, result.Count);
                if (stream.Length > 1024 * 1024)
                {
                    RaiseLog("Downlink message exceeded 1 MiB; closing the socket.");
                    break;
                }
                if (!result.EndOfMessage)
                {
                    continue;
                }
                var text = Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
                stream.SetLength(0);
                if (IsHelloAck(text))
                {
                    _helloAck?.TrySetResult(true);
                    continue;
                }
                if (GatewayProtocolCodec.TryParseDownlink(text, out var command, out var error))
                {
                    // Any accepted mesh frame also proves the hello was
                    // accepted (older servers do not send helloAck).
                    _helloAck?.TrySetResult(true);
                    RaiseCommand(command, text);
                }
                else
                {
                    RaiseLog($"Ignored downlink message: {error}");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        catch (WebSocketException exception)
        {
            _helloAck?.TrySetException(exception);
            RaiseLog($"WebSocket closed: {exception.Message}");
        }
        catch (Exception exception)
        {
            _helloAck?.TrySetException(exception);
            RaiseLog($"WebSocket receive failed: {exception.Message}");
        }
        finally
        {
            _helloAck?.TrySetException(new WebSocketException(
                "The WebSocket disconnected before the gateway handshake completed."));
            // The server closed or the read failed: release the socket now so
            // a restart does not leak the old connection.
            DisposeSocket();
            MarkLoopStopped(generation, "WebSocket disconnected");
        }
    }

    private static bool IsHelloAck(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   document.RootElement.TryGetProperty("type", out var type) &&
                   string.Equals(type.GetString(), "helloAck",
                       StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task SendOnSocketAsync(ClientWebSocket socket, string text,
        CancellationToken cancellationToken)
    {
        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (socket.State != WebSocketState.Open)
            {
                throw new InvalidOperationException("The WebSocket is not open.");
            }
            var bytes = Encoding.UTF8.GetBytes(text);
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private void RaiseCommand(GatewayCommandInfo command, string? rawJson)
    {
        DecodedMessage decoded;
        try
        {
            decoded = ProtocolInspector.Decode(command.Opcode, command.Payload);
        }
        catch (Exception exception)
        {
            decoded = new DecodedMessage(command.Opcode, false, null, "DECODE_ERROR",
                $"Could not decode the payload: {exception.Message}", [], [exception.Message]);
        }
        var json = rawJson ?? GatewayProtocolCodec.CreateCommandJson(command);
        var received = new ReceivedGatewayCommand(DateTimeOffset.UtcNow, command, decoded,
            json, _options.Transport);
        SafeInvoke(CommandReceived, received);
    }

    private void RaiseSentEvent(GatewayEventInfo message, DecodedMessage decoded, string rawJson)
    {
        var sent = new SentGatewayEvent(DateTimeOffset.UtcNow, message, decoded, rawJson,
            _options.Transport);
        SafeInvoke(EventSent, sent);
    }

    private void MarkLoopStopped(long generation, string reason)
    {
        bool wasAlive;
        lock (_gate)
        {
            // A timed-out Stop can be followed by a new Start. The old loop must
            // not clear the new run's state or emit a stale disconnect event.
            if (generation != _runGeneration)
            {
                return;
            }
            wasAlive = _loopAlive;
            _loopAlive = false;
        }
        if (wasAlive)
        {
            RaiseConnection(false, reason);
        }
    }

    private void RaiseConnection(bool connected, string description) =>
        SafeInvoke(ConnectionChanged, new GatewayConnectionState(connected, description));

    private void RaiseLog(string message) => SafeInvoke(Log, message);

    private static void SafeInvoke<T>(Action<T>? handlers, T value)
    {
        if (handlers is null)
        {
            return;
        }
        foreach (var handler in handlers.GetInvocationList().Cast<Action<T>>())
        {
            try
            {
                handler(value);
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Gateway emulator event handler failed: {exception}");
            }
        }
    }

    private void DisposeSocket()
    {
        ClientWebSocket? socket;
        lock (_gate)
        {
            socket = _socket;
            _socket = null;
        }
        if (socket is null)
        {
            return;
        }
        socket.Abort();
        socket.Dispose();
    }

    private static string TransportName(GatewayTransport transport) => transport switch
    {
        GatewayTransport.WebSocket => "WebSocket",
        GatewayTransport.Http => "HTTP polling",
        _ => transport.ToString()
    };
}
