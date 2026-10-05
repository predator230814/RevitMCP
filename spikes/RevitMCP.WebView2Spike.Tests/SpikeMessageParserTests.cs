using RevitMCP.WebView2Spike;
using RevitMCP.WebView2Spike.Messaging;
using Xunit;

namespace RevitMCP.WebView2Spike.Tests;

public class SpikeWebViewLifecycleTests
{
    [Fact]
    public void WebView_is_not_created_before_show_or_load()
    {
        Assert.False(SpikeWebViewLifecycle.ShouldCreate(showRequested: false, paneLoaded: true, alreadyCreated: false));
        Assert.False(SpikeWebViewLifecycle.ShouldCreate(showRequested: true, paneLoaded: false, alreadyCreated: false));
        Assert.False(SpikeWebViewLifecycle.ShouldCreate(showRequested: false, paneLoaded: false, alreadyCreated: false));
    }

    [Fact]
    public void WebView_is_created_once_after_show_and_load()
    {
        Assert.True(SpikeWebViewLifecycle.ShouldCreate(showRequested: true, paneLoaded: true, alreadyCreated: false));
        Assert.False(SpikeWebViewLifecycle.ShouldCreate(showRequested: true, paneLoaded: true, alreadyCreated: true));
    }
}

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

    [Theory]
    [InlineData("https://revitmcp-spike.local/")]
    [InlineData("https://revitmcp-spike.local/index.html")]
    [InlineData("https://REVITMCP-SPIKE.LOCAL/index.html?ready=1")]
    [InlineData("https://revitmcp-spike.local:443/index.html")]
    public void Local_virtual_origin_is_accepted(string uri)
    {
        Assert.True(SpikeOrigin.IsLocal(uri));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("about:blank")]
    [InlineData("http://revitmcp-spike.local/")]
    [InlineData("https://revitmcp-spike.local.evil.com/")]
    [InlineData("https://evil.com/")]
    [InlineData("https://evil.com@revitmcp-spike.local/")]
    [InlineData("https://revitmcp-spike.local:444/")]
    [InlineData("https://revitmcp-spike.local.evil.com")]
    [InlineData("file:///C:/ui/index.html")]
    public void Other_origins_are_rejected(string? uri)
    {
        Assert.False(SpikeOrigin.IsLocal(uri));
    }

    [Fact]
    public void Host_messages_are_not_accepted_as_ui_commands()
    {
        var result = SpikeMessageParser.Parse("""{"type":"hostReady"}""");

        Assert.False(result.Accepted);
        Assert.Equal("unknown", result.Rejection);
    }
}
