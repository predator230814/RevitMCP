using Autodesk.Revit.UI;
using RevitMCP.Addin.Approval;
using RevitMCP.Addin.Identity;
using RevitMCP.Addin.Lifecycle;

namespace RevitMCP.Addin;

public sealed class RevitMcpApplication : IExternalApplication
{
    private readonly RevitExecutionDispatcherFactory? _productionDispatchers;
    private readonly ApprovalUiRuntime? _approvalUi;
    private readonly AddinLifecycleCoordinator _lifecycle;

    public RevitMcpApplication()
    {
        _approvalUi = new ApprovalUiRuntime();
        _productionDispatchers = new RevitExecutionDispatcherFactory
        {
            ApprovalUi = _approvalUi,
        };
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
                try
                {
                    _productionDispatchers.ActiveDocumentEvents = new RevitActiveDocumentEventSource(application);
                }
                catch (Exception)
                {
                    _productionDispatchers.ActiveDocumentEvents = new DisabledActiveDocumentEventSource();
                }

                if (_approvalUi is not null)
                {
                    try
                    {
                        application.RegisterDockablePane(
                            new DockablePaneId(ApprovalPaneIds.PaneId),
                            ApprovalPaneIds.Title,
                            new ApprovalPaneProvider(_approvalUi));
                    }
                    catch (Exception)
                    {
                        _approvalUi.MarkUnavailable();
                    }
                }
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
        try
        {
            _lifecycle.Shutdown();
        }
        finally
        {
            _approvalUi?.Shutdown();
        }

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
