using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BalloonMesh.DebugTool.Core;
using Xunit;

namespace BalloonMesh.DebugTool.Tests;

public sealed class DebugApiClientTests
{
    [Fact]
    public void NormalizeBaseUri_AddsTrailingSlashAndValidatesScheme()
    {
        Assert.Equal("http://localhost:5080/",
            DebugApiClient.NormalizeBaseUri(new Uri("http://localhost:5080")).AbsoluteUri);
        Assert.Equal("https://example/base/",
            DebugApiClient.NormalizeBaseUri(new Uri("https://example/base")).AbsoluteUri);

        Assert.Throws<ArgumentException>(() =>
            DebugApiClient.NormalizeBaseUri(new Uri("ftp://localhost")));
        Assert.Throws<ArgumentException>(() =>
            DebugApiClient.NormalizeBaseUri(new Uri("/relative", UriKind.Relative)));
        Assert.Throws<ArgumentException>(() =>
            DebugApiClient.NormalizeBaseUri(new Uri("http://user:pass@localhost:5080")));
    }

    [Fact]
    public async Task SendGatewayEventsAsync_SendsTokenCookieAndBody()
    {
        var handler = new CapturingHandler(_ => JsonResponse("""{"accepted":2}"""));
        using var client = new DebugApiClient(new Uri("http://localhost:5080"), handler)
        {
            GatewayToken = "secret-token",
            AuthCookie = "Cookie: BalloonMesh.Auth=abc"
        };

        var result = await client.SendGatewayEventsAsync("gw-1",
        [
            new GatewayEventInfo(0x0101, 0xC011, 0xC702E5, "0102030405060708", -50, 7)
        ], "fw-1", true, 3);

        Assert.Equal(2, result.Accepted);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("http://localhost:5080/api/gateway/gw-1/events",
            handler.Uri!.AbsoluteUri);
        Assert.Equal("secret-token", handler.Headers["X-Gateway-Token"].Single());
        Assert.Equal("BalloonMesh.Auth=abc", handler.Headers["Cookie"].Single());
        Assert.Equal("application/json", handler.ContentType!.MediaType);

        using var document = JsonDocument.Parse(handler.Body!);
        var root = document.RootElement;
        Assert.Equal("gw-1", root.GetProperty("gatewayId").GetString());
        Assert.Equal("fw-1", root.GetProperty("firmware").GetString());
        Assert.True(root.GetProperty("provisioned").GetBoolean());
        Assert.Equal(3, root.GetProperty("spoolPending").GetInt32());
        var message = root.GetProperty("events")[0];
        Assert.Equal(0x0101, message.GetProperty("src").GetInt32());
        Assert.Equal(0xC702E5, message.GetProperty("opcode").GetInt64());
        Assert.Equal("0102030405060708", message.GetProperty("payload").GetString());
        Assert.False(message.TryGetProperty("receivedMs", out _));
    }

    [Fact]
    public async Task ReadGatewayCommandsAsync_EscapesIdAndHandlesNoContent()
    {
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var client = new DebugApiClient(new Uri("http://localhost:5080"), handler);

        var commands = await client.ReadGatewayCommandsAsync("gw 1", 5_000);

        Assert.Empty(commands);
        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Contains("gw%201", handler.Uri!.AbsoluteUri);
        Assert.Contains("waitMs=5000", handler.Uri.AbsoluteUri);
    }

    [Fact]
    public async Task ReadGatewayCommandsAsync_ClampsWait()
    {
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var client = new DebugApiClient(new Uri("http://localhost:5080"), handler);

        _ = await client.ReadGatewayCommandsAsync("gw", 99_999);

        Assert.Contains("waitMs=30000", handler.Uri!.AbsoluteUri);
    }

    [Fact]
    public async Task GetStateAsync_ThrowsFriendlyExceptionOnUnauthorized()
    {
        var handler = new CapturingHandler(_ =>
            JsonResponse("""{"error":"authentication required"}""", HttpStatusCode.Unauthorized));
        using var client = new DebugApiClient(new Uri("http://localhost:5080"), handler);

        var exception = await Assert.ThrowsAsync<DebugApiException>(
            () => client.GetStateAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        Assert.Contains("authentication required", exception.Message);
        Assert.Contains("Mock login", exception.Message);
    }

    [Fact]
    public async Task LoginMockAsync_ClearsManualCookieAndUsesRoleQuery()
    {
        var handler = new CapturingHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.TryAddWithoutValidation("Set-Cookie",
                "BalloonMesh.Auth=frommock; Path=/; HttpOnly");
            return response;
        });
        using var client = new DebugApiClient(new Uri("http://localhost:5080"), handler)
        {
            AuthCookie = "BalloonMesh.Auth=stale"
        };

        await client.LoginMockAsync("Operator");

        Assert.Null(client.AuthCookie);
        Assert.False(handler.Headers.ContainsKey("Cookie"));
        Assert.Equal("http://localhost:5080/auth/login/mock?role=Operator",
            handler.Uri!.AbsoluteUri);
    }

    [Fact]
    public async Task LoginMockAsync_CapturesCookieAndManualCookieOverridesIt()
    {
        var sentCookies = new List<string?>();
        var handler = new CapturingHandler(request =>
        {
            sentCookies.Add(request.Headers.TryGetValues("Cookie", out var values)
                ? string.Join("; ", values)
                : null);
            if (request.RequestUri!.AbsolutePath.Contains("auth/login/mock",
                    StringComparison.Ordinal))
            {
                var response = new HttpResponseMessage(HttpStatusCode.Redirect);
                response.Headers.TryAddWithoutValidation("Set-Cookie",
                    "BalloonMesh.Auth=frommock; Path=/; HttpOnly");
                return response;
            }
            return JsonResponse("""{"status":"ok","version":"1.0","time":"2026-01-01T00:00:00Z"}""");
        });
        using var client = new DebugApiClient(new Uri("http://localhost:5080"), handler);

        await client.LoginMockAsync("Admin");
        await client.GetHealthAsync();
        Assert.Equal("BalloonMesh.Auth=frommock", sentCookies[^1]);

        client.AuthCookie = "Manual=1";
        await client.GetHealthAsync();
        Assert.Equal("Manual=1", sentCookies[^1]);
    }

    [Fact]
    public async Task LoginMockAsync_RejectsUnknownRoleBeforeSending()
    {
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = new DebugApiClient(new Uri("http://localhost:5080"), handler);

        await Assert.ThrowsAsync<ArgumentException>(() => client.LoginMockAsync("root"));

        Assert.Null(handler.Uri);
    }

    [Fact]
    public async Task GetShowStepsAsync_ClampsLimit()
    {
        var handler = new CapturingHandler(_ => JsonResponse("[]"));
        using var client = new DebugApiClient(new Uri("http://localhost:5080"), handler);

        _ = await client.GetShowStepsAsync(999);

        Assert.Contains("limit=200", handler.Uri!.AbsoluteUri);
    }

    [Fact]
    public async Task GetStateAsync_DeserializesServerCamelCaseShape()
    {
        const string json = """
            {
              "serverTime": "2026-01-02T03:04:05Z",
              "version": "1.2.3",
              "simulationRunning": true,
              "balloons": [{
                "meshAddress": 257, "name": "B-01", "groupId": 1, "groupName": "Zone A",
                "lat": 35.63, "lon": 139.77, "confidence": 0.8,
                "r": 1, "g": 2, "b": 3, "queueCount": 4, "estimatedQueueCount": 5,
                "lastMethod": "ftm", "online": true
              }],
              "anchors": [],
              "gateways": [],
              "groups": []
            }
            """;
        var handler = new CapturingHandler(_ => JsonResponse(json));
        using var client = new DebugApiClient(new Uri("http://localhost:5080"), handler);

        var state = await client.GetStateAsync();

        Assert.Equal("1.2.3", state.Version);
        Assert.True(state.SimulationRunning);
        var balloon = Assert.Single(state.Balloons);
        Assert.Equal(0x0101, balloon.MeshAddress);
        Assert.Equal("B-01", balloon.Name);
        Assert.Equal(5, balloon.EstimatedQueueCount);
        Assert.Equal(3, balloon.B);
    }

    [Fact]
    public async Task GetShowStepsAsync_DeserializesRows()
    {
        const string json = """
            [{"id":7,"name":"intro","targetKind":"all","target":"all",
              "executeAt":"2026-01-02T03:04:05Z","r":1,"g":2,"b":3,"status":"queued"}]
            """;
        var handler = new CapturingHandler(_ => JsonResponse(json));
        using var client = new DebugApiClient(new Uri("http://localhost:5080"), handler);

        var steps = await client.GetShowStepsAsync();

        var step = Assert.Single(steps);
        Assert.Equal(7, step.Id);
        Assert.Equal("intro", step.Name);
        Assert.Equal("queued", step.Status);
    }

    [Fact]
    public async Task ReadGatewayCommandsAsync_DeserializesCommands()
    {
        const string json = """
            {"commands":[{"dst":49169,"opcode":13042405,"payload":"0102","ttl":7}]}
            """;
        var handler = new CapturingHandler(_ => JsonResponse(json));
        using var client = new DebugApiClient(new Uri("http://localhost:5080"), handler);

        var commands = await client.ReadGatewayCommandsAsync("gw", 100);

        var command = Assert.Single(commands);
        Assert.Equal(0xC011, command.Dst);
        Assert.Equal(0xC702E5u, command.Opcode);
        Assert.Equal("0102", command.Payload);
        Assert.Equal(7, command.Ttl);
    }

    [Fact]
    public async Task SendLedNowAsync_DeserializesCommandResult()
    {
        const string json = """
            {"accepted":true,"message":"ok","gateways":2,"balloons":50,
             "opcode":"0xC402E5","payload":"0102030405060708"}
            """;
        var handler = new CapturingHandler(_ => JsonResponse(json));
        using var client = new DebugApiClient(new Uri("http://localhost:5080"), handler);

        var result = await client.SendLedNowAsync(new LedNowCommand("all", "all", 1, 2, 3));

        Assert.True(result.Accepted);
        Assert.Equal(2, result.Gateways);
        Assert.Equal(50, result.Balloons);
        Assert.Equal("0xC402E5", result.Opcode);
    }

    private static HttpResponseMessage JsonResponse(string json,
        HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class CapturingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public Uri? Uri { get; private set; }
        public Dictionary<string, string[]> Headers { get; private set; } =
            new(StringComparer.OrdinalIgnoreCase);
        public string? Body { get; private set; }
        public MediaTypeHeaderValue? ContentType { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            Uri = request.RequestUri;
            Headers = request.Headers.ToDictionary(header => header.Key,
                header => header.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
            ContentType = request.Content?.Headers.ContentType;
            if (request.Content is not null)
            {
                Body = await request.Content.ReadAsStringAsync(cancellationToken);
            }
            return response(request);
        }
    }
}
