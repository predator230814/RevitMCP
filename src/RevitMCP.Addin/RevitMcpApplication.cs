using Autodesk.Revit.UI;
using RevitMCP.Addin.Lifecycle;

namespace RevitMCP.Addin;

public sealed class RevitMcpApplication : IExternalApplication
{
    private readonly AddinLifecycleCoordinator _lifecycle;

    public RevitMcpApplication()
        : this(
            new AddinLifecycleCoordinator(
                new RevitExecutionDispatcherFactory(),
                new NamedPipeLifecycleBridgeFactory(),
                new ProcessRuntimeMetadataSource()))
    {
    }

    internal RevitMcpApplication(AddinLifecycleCoordinator lifecycle)
    {
        ArgumentNullException.ThrowIfNull(lifecycle);
        _lifecycle = lifecycle;
    }

    internal AddinLifecycleCoordinator Lifecycle => _lifecycle;

    internal void BeginStartup(IBootstrapScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        _lifecycle.Prepare(scheduler.Subscribe(OnBootstrapSender));
    }

    public Result OnStartup(UIControlledApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);

        try
        {
            BeginStartup(new RevitIdlingScheduler(application));
            return Result.Succeeded;
        }
        catch
        {
            _lifecycle.Shutdown();
            return Result.Failed;
        }
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        _lifecycle.Shutdown();
        return Result.Succeeded;
    }

    private void OnBootstrapSender(object sender)
    {
        if (sender is not UIApplication uiApplication)
        {
            return;
        }

        var revit = new LifecycleRevitRuntime(
            uiApplication.Application.VersionNumber,
            uiApplication.Application.VersionBuild);
        _lifecycle.TryBootstrap(revit);
    }
}
