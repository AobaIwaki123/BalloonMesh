using System.Security.Cryptography;
using System.Text;

namespace BalloonMesh.Server.Services;

public static class GatewayAuth
{
    public const string HeaderName = "X-Gateway-Token";

    public static string? ConfiguredToken(IConfiguration configuration) =>
        configuration["Gateway:Token"];

    public static bool IsAuthorized(HttpContext context, IConfiguration configuration)
    {
        var expected = ConfiguredToken(configuration);
        if (string.IsNullOrWhiteSpace(expected))
        {
            return true;
        }
        if (!context.Request.Headers.TryGetValue(HeaderName, out var values))
        {
            return false;
        }
        return Matches(values.ToString(), expected);
    }

    public static bool Matches(string? provided, string? expected)
    {
        if (string.IsNullOrWhiteSpace(expected))
        {
            return true;
        }
        if (string.IsNullOrWhiteSpace(provided))
        {
            return false;
        }
        var left = Encoding.UTF8.GetBytes(provided);
        var right = Encoding.UTF8.GetBytes(expected);
        return left.Length == right.Length &&
               CryptographicOperations.FixedTimeEquals(left, right);
    }
}
