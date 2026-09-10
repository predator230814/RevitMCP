using Autodesk.Revit.UI;
using RevitMCP.Addin.Identity;
using RevitMCP.Addin.Lifecycle;

namespace RevitMCP.Addin;

public sealed class RevitMcpApplication : IExternalApplication
{
    private readonly RevitExecutionDispatcherFactory? _productionDispatchers;
    private readonly AddinLifecycleCoordinator _lifecycle;

    public RevitMcpApplication()
    {
        _productionDispatchers = new RevitExecutionDispatcherFactory();
        _lifecycle = new AddinLifecycleCoordinator(
            _productionDispatchers,
            new NamedPipeLifecycleBridgeFactory(),
            new ProcessRuntimeMetadataSource());
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
        IBootstrapSubscription? subscription = null;
        try
        {
            subscription = scheduler.Subscribe(OnBootstrapSender);
            _lifecycle.Prepare(subscription);
            subscription = null;
        }
        catch
        {
            subscription?.Dispose();
            throw;
        }
    }

    public Result OnStartup(UIControlledApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);

        try
        {
            if (_productionDispatchers is not null)
            {
                _productionDispatchers.CloseEvents =
                    new RevitDocumentCloseEventSource(application.ControlledApplication);
            }

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
