using Xunit;
using BalloonMesh.DebugTool.Cli;
using BalloonMesh.DebugTool.Core;

namespace BalloonMesh.DebugTool.Cli.Tests;

public sealed class EventSpecTests
{
    [Fact]
    public void FtmEventEncodesDistanceAndDefaults()
    {
        var message = EventSpec.Parse("ftm:anchor=1,distance=3000,batch=2");

        Assert.Equal((ushort)0x0101, message.Src);
        Assert.Equal((ushort)0xC011, message.Dst);
        Assert.Equal(ProtocolInspector.ToVendorOpcode(0x07), message.Opcode);
        var decoded = ProtocolInspector.Decode(message.Opcode, message.Payload);
        Assert.Contains(decoded.Fields, field =>
            field.Name == "Distance" && field.Value.StartsWith("3000"));
        Assert.Contains(decoded.Fields, field =>
            field.Name == "Batch id" && field.Value == "2");
    }

    [Fact]
    public void QueueEventMapsKind()
    {
        var message = EventSpec.Parse("queue:kind=nearfull,count=401");

        var decoded = ProtocolInspector.Decode(message.Opcode, message.Payload);
        Assert.Contains(decoded.Fields, field =>
            field.Name == "Kind" && field.Value == "NEAR_FULL");
        Assert.Contains(decoded.Fields, field =>
            field.Name == "Queue count" && field.Value == "401");
    }

    [Fact]
    public void NeighborEventKeepsBothEntries()
    {
        var message = EventSpec.Parse(
            "neighbor:batch=1,index=0,neighbor=0x0102@-45,neighbor=0x0103@-60");

        var decoded = ProtocolInspector.Decode(message.Opcode, message.Payload);
        Assert.Equal(2, decoded.Fields.Count(field => field.Name == "Neighbor"));
    }

    [Fact]
    public void LedNowEventEncodesRgb()
    {
        var message = EventSpec.Parse("lednow:r=1,g=2,b=3");

        var decoded = ProtocolInspector.Decode(message.Opcode, message.Payload);
        Assert.Contains(decoded.Fields, field =>
            field.Name == "RGB" && field.Value.StartsWith("#010203"));
    }

    [Fact]
    public void RawEventUsesOpcodeAndSourceOverrides()
    {
        var message = EventSpec.Parse(
            "raw:opcode=0xCA02E5,payload=0102,src=0x0202,dst=0xC010,rssi=-30,ttl=5");

        Assert.Equal((ushort)0x0202, message.Src);
        Assert.Equal((ushort)0xC010, message.Dst);
        Assert.Equal((sbyte)-30, message.Rssi);
        Assert.Equal(5, message.Ttl);
        Assert.Equal("0102", message.Payload);
    }

    [Theory]
    [InlineData("neighbor_request")]
    [InlineData("neighbor_probe")]
    [InlineData("time_request")]
    public void RequestEventsHaveEmptyPayload(string spec)
    {
        var message = EventSpec.Parse(spec);

        Assert.Empty(message.Payload);
    }

    [Fact]
    public void UnknownFieldIsRejected()
    {
        var exception = Assert.Throws<FormatException>(
            () => EventSpec.Parse("ftm:distance=3000,nope=1"));
        Assert.Contains("nope", exception.Message);
    }

    [Fact]
    public void UnknownKindIsRejected()
    {
        var exception = Assert.Throws<FormatException>(() => EventSpec.Parse("bogus:value=1"));
        Assert.Contains("Unknown event kind", exception.Message);
    }

    [Fact]
    public void MissingRequiredFieldIsRejected()
    {
        Assert.Throws<FormatException>(() => EventSpec.Parse("ftm:anchor=1"));
        Assert.Throws<FormatException>(() => EventSpec.Parse("raw:payload=00"));
    }
}
