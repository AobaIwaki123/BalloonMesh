using System.Text.Json;
using BalloonMesh.DebugTool.Core;
using Xunit;

namespace BalloonMesh.DebugTool.Tests;

public sealed class GatewayProtocolCodecTests
{
    [Fact]
    public void BuildWebSocketUri_MapsHttpSchemesAndPreservesBasePath()
    {
        Assert.Equal("ws://localhost:5080/ws/gateway",
            GatewayProtocolCodec.BuildWebSocketUri(new Uri("http://localhost:5080")).ToString());
        Assert.Equal("wss://example/base/ws/gateway",
            GatewayProtocolCodec.BuildWebSocketUri(new Uri("https://example/base")).ToString());
    }

    [Fact]
    public void CreateHello_ContainsConnectionFieldsButNotPollWait()
    {
        var options = new GatewayEmulatorOptions(
            new Uri("http://localhost:5080"),
            "gw-1",
            "token-1",
            GatewayTransport.Http,
            "fw-1",
            Provisioned: true,
            SpoolPending: 4,
            PollWaitMs: 5_000);

        using var document = JsonDocument.Parse(GatewayProtocolCodec.CreateHello(options));
        var root = document.RootElement;

        Assert.Equal("hello", root.GetProperty("type").GetString());
        Assert.Equal("gw-1", root.GetProperty("gatewayId").GetString());
        Assert.Equal("token-1", root.GetProperty("token").GetString());
        Assert.Equal("fw-1", root.GetProperty("firmware").GetString());
        Assert.True(root.GetProperty("provisioned").GetBoolean());
        Assert.Equal(4, root.GetProperty("spoolPending").GetInt32());
    }

    [Fact]
    public void CreateCommandJson_RoundTripsThroughDownlinkParser()
    {
        var command = new GatewayCommandInfo(0xC011, 0xC102E5, "0102030405060708", 7);

        var json = GatewayProtocolCodec.CreateCommandJson(command);

        Assert.True(GatewayProtocolCodec.TryParseDownlink(json, out var parsed, out var error),
            error);
        Assert.Equal(command, parsed);
    }

    [Fact]
    public void CreateUplink_NormalizesHexPayload()
    {
        var message = new GatewayEventInfo(0x0101, 0xC011, 0xC702E5, "0x01 02:03", -45, 7);

        using var document = JsonDocument.Parse(GatewayProtocolCodec.CreateUplink(message));
        var root = document.RootElement;

        Assert.Equal("mesh", root.GetProperty("type").GetString());
        Assert.Equal("010203", root.GetProperty("payload").GetString());
        Assert.Equal(-45, root.GetProperty("rssi").GetInt32());
        Assert.False(root.TryGetProperty("receivedMs", out _));
    }

    [Theory]
    [InlineData("""{"type":"mesh","dst":49169,"opcode":13042405,"payload":"0102030405060708","ttl":7}""")]
    [InlineData("""{"type":"mesh","dst":"0xC011","opcode":"0xC702E5","payload":"0x0102030405060708","ttl":7}""")]
    public void TryParseDownlink_AcceptsNumberOrStringFields(string json)
    {
        var parsed = GatewayProtocolCodec.TryParseDownlink(json, out var command, out var error);

        Assert.True(parsed, error);
        Assert.Equal(0xC702E5u, command.Opcode);
        Assert.Equal(0xC011, command.Dst);
        Assert.Equal("0102030405060708", command.Payload);
        Assert.Equal(7, command.Ttl);
    }

    [Theory]
    [InlineData("""{"type":"other","dst":1,"opcode":1,"payload":""}""", "mesh")]
    [InlineData("""{"type":"mesh","dst":1,"opcode":1,"payload":"GG"}""", "payload")]
    [InlineData("""{"type":"mesh","opcode":1,"payload":""}""", "dst")]
    public void TryParseDownlink_RejectsMalformedMessages(string json, string expectedError)
    {
        var parsed = GatewayProtocolCodec.TryParseDownlink(json, out _, out var error);

        Assert.False(parsed);
        Assert.Contains(expectedError, error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GatewayOptions_ValidateRejectsInvalidValues()
    {
        Assert.Throws<ArgumentException>(() => new GatewayEmulatorOptions(
            new Uri("http://localhost:5080"), " ").Validate());
        Assert.Throws<ArgumentException>(() => new GatewayEmulatorOptions(
            new Uri("http://localhost:5080"), "gw", SpoolPending: -1).Validate());
        Assert.Throws<ArgumentException>(() => new GatewayEmulatorOptions(
            new Uri("http://localhost:5080"), "gw", PollWaitMs: 30_001).Validate());
        Assert.Throws<ArgumentException>(() => new GatewayEmulatorOptions(
            new Uri("http://localhost:5080"), "gw",
            Transport: GatewayTransport.Http, PollWaitMs: 0).Validate());
    }
}
