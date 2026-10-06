namespace RevitMCP.Addin.Approval;

/// <summary>
/// WebView2 is created only for an explicit review on a loaded pane that does not already have one.
/// </summary>
internal static class ApprovalWebViewLifecycle
{
    public static bool ShouldCreate(bool showRequested, bool paneLoaded, bool alreadyCreated)
    {
        return showRequested && paneLoaded && !alreadyCreated;
    }
}
