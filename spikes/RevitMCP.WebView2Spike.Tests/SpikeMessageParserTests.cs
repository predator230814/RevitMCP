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

    [Fact]
    public void Failed_attempt_can_be_retried_on_a_later_show()
    {
        Assert.False(SpikeWebViewLifecycle.ShouldCreate(showRequested: true, paneLoaded: true, alreadyCreated: true));
        Assert.True(SpikeWebViewLifecycle.ShouldCreate(showRequested: true, paneLoaded: true, alreadyCreated: false));
    }
}

public class SpikeWebViewPackageTests
{
    [Theory]
    [InlineData("2025", "1.0.2045.28")]
    [InlineData("2026", "1.0.2478.35")]
    [InlineData("2027", "1.0.2478.35")]
    public void Managed_sdk_matches_the_revit_year(string revitYear, string packageVersion)
    {
        Assert.Equal(packageVersion, SpikeWebViewPackages.ForRevitYear(revitYear));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2024")]
    [InlineData("2028")]
    public void Unknown_revit_year_has_no_package(string? revitYear)
    {
        Assert.Null(SpikeWebViewPackages.ForRevitYear(revitYear));
    }

    [Fact]
    public void Show_failures_are_distinct()
    {
        var registered = SpikeShowDiagnostics.Message(SpikeShowStep.NotRegistered);
        var shown = SpikeShowDiagnostics.Message(SpikeShowStep.ShowFailed);
        var created = SpikeShowDiagnostics.Message(SpikeShowStep.WebViewCreationFailed);

        Assert.Equal("The RevitMCP UI spike pane is not registered.", registered);
        Assert.Equal("The RevitMCP UI spike pane could not be shown.", shown);
        Assert.Equal("WebView2 could not be created in the spike pane.", created);
        Assert.NotEqual(registered, shown);
        Assert.NotEqual(registered, created);
        Assert.NotEqual(shown, created);
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
