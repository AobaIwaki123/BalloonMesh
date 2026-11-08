using BalloonMesh.Server.Services;

namespace BalloonMesh.Server.Tests;

public sealed class GatewayAuthTests
{
    [Fact]
    public void EmptyConfiguredTokenAllowsAnyGateway()
    {
        Assert.True(GatewayAuth.Matches(null, ""));
        Assert.True(GatewayAuth.Matches("anything", null));
    }

    [Fact]
    public void TokenMustMatchExactly()
    {
        Assert.True(GatewayAuth.Matches("secret", "secret"));
        Assert.False(GatewayAuth.Matches("secret", "other"));
        Assert.False(GatewayAuth.Matches("secret", "secret-longer"));
        Assert.False(GatewayAuth.Matches(null, "secret"));
    }
}
