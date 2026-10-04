using Autodesk.Revit.UI;

namespace RevitMCP.WebView2Spike;

internal sealed class SpikePaneProvider : IDockablePaneProvider
{
    private readonly string _revitVersionNumber;
    private readonly string _revitBuild;

    public SpikePaneProvider(string revitVersionNumber, string revitBuild)
    {
        _revitVersionNumber = revitVersionNumber;
        _revitBuild = revitBuild;
    }

    public void SetupDockablePane(DockablePaneProviderData data)
    {
        var host = new SpikePaneHost(_revitVersionNumber, _revitBuild);
        SpikePaneLifetime.Attach(host);
        data.FrameworkElement = host;
        data.VisibleByDefault = false;
        data.InitialState = new DockablePaneState
        {
            DockPosition = DockPosition.Right,
        };
    }
}

internal static class SpikePaneLifetime
{
    private static SpikePaneHost? _host;

    public static void Attach(SpikePaneHost host) => _host = host;

    public static void DisposeHost()
    {
        _host?.DisposeHost();
        _host = null;
    }
}
