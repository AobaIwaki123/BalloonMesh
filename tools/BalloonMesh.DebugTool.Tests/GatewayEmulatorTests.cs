using System.Net;
using System.Text;
using BalloonMesh.DebugTool.Core;
using Xunit;

namespace BalloonMesh.DebugTool.Tests;

public sealed class GatewayEmulatorTests
{
    [Fact]
    public async Task HttpEmulator_CanStopAndRestart()
    {
        var handler = new GatewayHandler();
        using var client = new DebugApiClient(new Uri("http://localhost:5080"), handler);
        await using var emulator = new GatewayEmulator(CreateOptions(), client);

        await emulator.StartAsync();
        Assert.True(emulator.IsRunning);

        await emulator.StopAsync();
        Assert.False(emulator.IsRunning);

        await emulator.StartAsync();
        Assert.True(emulator.IsRunning);

        await emulator.StopAsync();
        Assert.False(emulator.IsRunning);
    }

    [Fact]
    public async Task HttpEmulator_RestartsAfterUnexpectedPollFailure()
    {
        var handler = new GatewayHandler();
        var failCommands = true;
        handler.Commands = _ => failCommands
            ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
            : new HttpResponseMessage(HttpStatusCode.NoContent);
        using var client = new DebugApiClient(new Uri("http://localhost:5080"), handler);
        await using var emulator = new GatewayEmulator(CreateOptions(), client);

        await emulator.StartAsync();
        Assert.True(await WaitUntilAsync(() => !emulator.IsRunning,
            TimeSpan.FromSeconds(3)));

        failCommands = false;
        await emulator.StartAsync();
        Assert.True(emulator.IsRunning);

        await emulator.StopAsync();
    }

    [Fact]
    public async Task HttpEmulator_SendsUplinkThroughTheEventEndpoint()
    {
        var handler = new GatewayHandler();
        using var client = new DebugApiClient(new Uri("http://localhost:5080"), handler);
        await using var emulator = new GatewayEmulator(CreateOptions(), client);

        await emulator.StartAsync();
        await emulator.SendEventAsync(new GatewayEventInfo(
            0x0101, 0xC011, 0xC702E5, "0102030405060708", -50, 7));

        Assert.Equal(2, handler.EventPosts);
        await emulator.StopAsync();
    }

    [Fact]
    public async Task HttpEmulator_RaisesSentEventForUplink()
    {
        var handler = new GatewayHandler();
        using var client = new DebugApiClient(new Uri("http://localhost:5080"), handler);
        await using var emulator = new GatewayEmulator(CreateOptions(), client);
        var sent = new TaskCompletionSource<SentGatewayEvent>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        emulator.EventSent += message => sent.TrySetResult(message);

        await emulator.StartAsync();
        await emulator.SendEventAsync(new GatewayEventInfo(
            0x0101, 0xC011, 0xC702E5, "0102030405060708", -50, 7));

        var message = await sent.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(GatewayTransport.Http, message.Transport);
        Assert.Contains("FTM_REPORT", ProtocolInspector.FormatDecoded(message.Decoded));
        Assert.Contains("\"type\":\"mesh\"", message.RawJson);
        await emulator.StopAsync();
    }

    [Fact]
    public async Task HttpEmulator_DecodedDownlinkHasSyntheticRawJson()
    {
        const string commandJson = """
            {"commands":[{"dst":49169,"opcode":13042405,
              "payload":"0102030405060708","ttl":7}]}
            """;
        var handler = new GatewayHandler();
        var firstPoll = true;
        handler.Commands = _ =>
        {
            if (!firstPoll)
            {
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            firstPoll = false;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(commandJson, Encoding.UTF8, "application/json")
            };
        };
        using var client = new DebugApiClient(new Uri("http://localhost:5080"), handler);
        await using var emulator = new GatewayEmulator(CreateOptions(), client);
        var received = new TaskCompletionSource<ReceivedGatewayCommand>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        emulator.CommandReceived += message => received.TrySetResult(message);

        await emulator.StartAsync();
        var message = await received.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(GatewayTransport.Http, message.Transport);
        Assert.Equal("FTM_REPORT", message.Decoded.Name);
        Assert.Contains("0102030405060708", message.RawJson);
        Assert.Contains("\"type\":\"mesh\"", message.RawJson);
        await emulator.StopAsync();
    }

    [Fact]
    public void HttpEmulator_RejectsBusyLoopPollWait()
    {
        Assert.Throws<ArgumentException>(() => new GatewayEmulatorOptions(
            new Uri("http://localhost:5080"),
            "gw",
            Transport: GatewayTransport.Http,
            PollWaitMs: 0).Validate());
    }

    private static GatewayEmulatorOptions CreateOptions() => new(
        new Uri("http://localhost:5080"),
        "debug-gw",
        Transport: GatewayTransport.Http,
        PollWaitMs: 100);

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                return false;
            }
            await Task.Delay(20);
        }
        return true;
    }

    private sealed class GatewayHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Commands { get; set; } =
            _ => new HttpResponseMessage(HttpStatusCode.NoContent);

        public int EventPosts { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (path.EndsWith("/events", StringComparison.Ordinal))
            {
                EventPosts++;
                return JsonResponse("""{"accepted":0}""");
            }
            if (path.EndsWith("/commands", StringComparison.Ordinal))
            {
                await Task.Delay(25, cancellationToken);
                return Commands(request);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
