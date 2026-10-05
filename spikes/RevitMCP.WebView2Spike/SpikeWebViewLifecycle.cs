namespace RevitMCP.WebView2Spike;

/// <summary>
/// Decides when the spike may construct its single WebView2 control.
/// </summary>
public static class SpikeWebViewLifecycle
{
    public static bool ShouldCreate(bool showRequested, bool paneLoaded, bool alreadyCreated)
    {
        return showRequested && paneLoaded && !alreadyCreated;
    }
}
