using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using BalloonMesh.DebugTool.Core;
using Xunit;

namespace BalloonMesh.DebugTool.Tests;

public sealed class CookieSessionTests
{
    [Fact]
    public async Task DefaultHandler_UsesMockCookieThenManualCookieWithoutDuplicates()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var cookies = new ConcurrentQueue<string?>();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var server = Task.Run(() => ServeAsync(listener, cookies, cancellation.Token),
            cancellation.Token);

        try
        {
            using var client = new DebugApiClient(new Uri($"http://127.0.0.1:{port}"));

            await client.LoginMockAsync("Admin");
            await client.GetHealthAsync();
            client.AuthCookie = "Manual=1";
            await client.GetHealthAsync();

            var sent = cookies.ToArray();
            Assert.Equal(3, sent.Length);
            Assert.Null(sent[0]);
            Assert.Equal("BalloonMesh.Auth=frommock", sent[1]);
            Assert.Equal("Manual=1", sent[2]);
        }
        finally
        {
            cancellation.Cancel();
            listener.Stop();
            try
            {
                await server;
            }
            catch (Exception exception) when (exception is OperationCanceledException or
                                              SocketException or
                                              ObjectDisposedException)
            {
                // Expected when the test stops the listener.
            }
        }
    }

    private static async Task ServeAsync(TcpListener listener,
        ConcurrentQueue<string?> cookies, CancellationToken cancellationToken)
    {
        for (var served = 0; served < 3; served++)
        {
            using var tcp = await listener.AcceptTcpClientAsync(cancellationToken);
            await using var stream = tcp.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            var requestLine = await reader.ReadLineAsync(cancellationToken)
                ?? throw new IOException("Client closed before sending a request line.");
            string? cookie = null;
            string? line;
            while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(cancellationToken)))
            {
                if (line.StartsWith("Cookie:", StringComparison.OrdinalIgnoreCase))
                {
                    cookie = line["Cookie:".Length..].Trim();
                }
            }
            cookies.Enqueue(cookie);

            var path = requestLine.Split(' ')[1];
            var isLogin = path.StartsWith("/auth/login/mock", StringComparison.Ordinal);
            var status = isLogin ? "302 Found" : "200 OK";
            var body = isLogin ? string.Empty :
                """{"status":"ok","version":"1","time":"2026-01-01T00:00:00Z"}""";
            var bodyBytes = Encoding.UTF8.GetBytes(body);
            var response = new StringBuilder()
                .Append("HTTP/1.1 ").Append(status).Append("\r\n")
                .Append("Content-Type: application/json\r\n")
                .Append("Content-Length: ").Append(bodyBytes.Length).Append("\r\n")
                .Append("Connection: close\r\n");
            if (isLogin)
            {
                response.Append("Location: /\r\n")
                    .Append("Set-Cookie: BalloonMesh.Auth=frommock; Path=/; HttpOnly\r\n");
            }
            response.Append("\r\n");

            await stream.WriteAsync(Encoding.ASCII.GetBytes(response.ToString()),
                cancellationToken);
            await stream.WriteAsync(bodyBytes, cancellationToken);
        }
    }
}
