using Autodesk.Revit.UI;

namespace RevitMCP.Addin.Approval;

internal sealed class ApprovalPaneProvider : IDockablePaneProvider
{
    private readonly ApprovalUiRuntime _runtime;

    public ApprovalPaneProvider(ApprovalUiRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        _runtime = runtime;
    }

    public void SetupDockablePane(DockablePaneProviderData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var host = new ApprovalPaneHost(_runtime);
        _runtime.RegisterSurface(host);
        data.FrameworkElement = host;
        data.VisibleByDefault = false;
        data.InitialState = new DockablePaneState
        {
            DockPosition = DockPosition.Right,
        };
    }
}
