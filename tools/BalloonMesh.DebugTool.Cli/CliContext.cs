using System.Text.Json;
using System.Text.Json.Serialization;
using BalloonMesh.DebugTool.Core;

namespace BalloonMesh.DebugTool.Cli;

/// <summary>
/// Connection settings and output helpers shared by all commands. Secrets are
/// read from options or environment variables and never written to disk.
/// </summary>
public sealed class CliContext : IDisposable
{
    public static readonly string[] GlobalOptions =
    [
        "help", "json", "verbose", "server", "cookie", "role", "gateway-token"
    ];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly CliArguments _arguments;
    private readonly string? _cookie;
    private readonly string? _role;
    private DebugApiClient? _client;

    public CliContext(CliArguments arguments, TextWriter output, TextWriter error)
    {
        _arguments = arguments;
        Out = output;
        Error = error;
        Json = arguments.Flag("json");
        Verbose = arguments.Flag("verbose");

        var serverText = arguments.Value("server")
            ?? Environment.GetEnvironmentVariable("BALLOONMESH_SERVER")
            ?? "http://localhost:5080";
        if (!Uri.TryCreate(serverText, UriKind.Absolute, out var server))
        {
            throw new CliUsageException($"Server URL '{serverText}' is not an absolute URL.");
        }
        try
        {
            Server = DebugApiClient.NormalizeBaseUri(server);
        }
        catch (ArgumentException exception)
        {
            throw new CliUsageException(exception.Message);
        }

        _cookie = arguments.Value("cookie")
            ?? Environment.GetEnvironmentVariable("BALLOONMESH_COOKIE");
        _role = arguments.Value("role")
            ?? Environment.GetEnvironmentVariable("BALLOONMESH_ROLE");
        GatewayToken = arguments.Value("gateway-token")
            ?? Environment.GetEnvironmentVariable("BALLOONMESH_GATEWAY_TOKEN");

        if (_cookie is not null && _role is not null)
        {
            throw new CliUsageException("Use either --cookie or --role, not both.");
        }
        if (_role is not null && _role is not ("Admin" or "Operator" or "Viewer"))
        {
            throw new CliUsageException("--role must be Admin, Operator or Viewer.");
        }
        if (arguments.Errors.Count > 0)
        {
            throw new CliUsageException(string.Join(Environment.NewLine,
                arguments.Errors.Distinct(StringComparer.Ordinal)));
        }
    }

    public Uri Server { get; }

    public string? GatewayToken { get; }

    public bool Json { get; }

    public bool Verbose { get; }

    public TextWriter Out { get; }

    public TextWriter Error { get; }

    public void ValidateOptions(params string[] commandOptions) =>
        _arguments.ValidateOptions([.. GlobalOptions, .. commandOptions]);

    /// <summary>
    /// Creates the API client once and performs the Development mock login when
    /// <c>--role</c> was supplied.
    /// </summary>
    public async Task<DebugApiClient> GetClientAsync(CancellationToken cancellationToken)
    {
        if (_client is not null)
        {
            return _client;
        }
        var client = new DebugApiClient(Server)
        {
            GatewayToken = GatewayToken,
            AuthCookie = _cookie
        };
        try
        {
            if (_role is not null)
            {
                await client.LoginMockAsync(_role, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            client.Dispose();
            throw;
        }
        _client = client;
        return client;
    }

    public void WriteJson(object value) =>
        Out.WriteLine(JsonSerializer.Serialize(value, JsonOptions));

    public void Dispose() => _client?.Dispose();
}
