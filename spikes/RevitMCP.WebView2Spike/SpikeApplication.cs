using Autodesk.Revit.UI;

namespace RevitMCP.WebView2Spike;

public sealed class SpikeApplication : IExternalApplication
{
    public Result OnStartup(UIControlledApplication application)
    {
        try
        {
            var provider = new SpikePaneProvider(
                application.ControlledApplication.VersionNumber,
                application.ControlledApplication.VersionBuild);
            application.RegisterDockablePane(new DockablePaneId(SpikeIds.PaneGuid), "RevitMCP UI Spike", provider);
            return Result.Succeeded;
        }
        catch (Exception)
        {
            return Result.Failed;
        }
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        try
        {
            SpikePaneLifetime.DisposeHost();
        }
        catch (Exception)
        {
            // Shutdown must not depend on WebView2 still being alive.
        }

        return Result.Succeeded;
    }
}
