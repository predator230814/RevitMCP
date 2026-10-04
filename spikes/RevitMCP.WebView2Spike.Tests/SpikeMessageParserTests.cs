using RevitMCP.WebView2Spike.Messaging;
using Xunit;

namespace RevitMCP.WebView2Spike.Tests;

public class SpikeMessageParserTests
{
    [Fact]
    public void Ping_is_accepted()
    {
        var result = SpikeMessageParser.Parse("""{"type":"ping"}""");

        Assert.True(result.Accepted);
        Assert.Equal("ping", result.MessageType);
        Assert.Equal("", result.Rejection);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_input_is_rejected(string? json)
    {
        var result = SpikeMessageParser.Parse(json);

        Assert.False(result.Accepted);
        Assert.Equal("empty", result.Rejection);
    }

    [Fact]
    public void Unknown_type_is_rejected()
    {
        var result = SpikeMessageParser.Parse("""{"type":"execute"}""");

        Assert.False(result.Accepted);
        Assert.Equal("execute", result.MessageType);
        Assert.Equal("unknown", result.Rejection);
    }

    [Fact]
    public void Extra_property_is_rejected()
    {
        var result = SpikeMessageParser.Parse("""{"type":"ping","command":"Parameter.Set"}""");

        Assert.False(result.Accepted);
        Assert.Equal("malformed", result.Rejection);
    }

    [Fact]
    public void Malformed_json_is_rejected()
    {
        var result = SpikeMessageParser.Parse("{");

        Assert.False(result.Accepted);
        Assert.Equal("malformed", result.Rejection);
    }

    [Fact]
    public void Host_messages_are_not_accepted_as_ui_commands()
    {
        var result = SpikeMessageParser.Parse("""{"type":"hostReady"}""");

        Assert.False(result.Accepted);
        Assert.Equal("unknown", result.Rejection);
    }
}
