using BalloonMesh.Server.Auth;

namespace BalloonMesh.Server.Tests;

public sealed class AuthTests
{
    [Theory]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("/", "/")]
    [InlineData("/api/state", "/api/state")]
    [InlineData("//evil.example", "/")]
    [InlineData("https://evil.example", "/")]
    [InlineData("\\\\evil", "/")]
    public void ReturnUrlMustBeLocal(string? input, string expected)
    {
        Assert.Equal(expected, AuthSetup.SafeReturnUrl(input));
    }
}
