using Autodesk.Revit.UI;
using BimIt.Revit.Addin.Approval;
using BimIt.Revit.Addin.Assistant;
using BimIt.Revit.Addin.Identity;
using BimIt.Revit.Addin.Lifecycle;

namespace BimIt.Revit.Addin;

public sealed class RevitMcpApplication : IExternalApplication
{
    private readonly RevitExecutionDispatcherFactory? _productionDispatchers;
    private readonly ApprovalUiRuntime? _approvalUi;
    private readonly AssistantShell? _assistant;
    private readonly AddinLifecycleCoordinator _lifecycle;

    public RevitMcpApplication()
    {
        _approvalUi = new ApprovalUiRuntime();
        var calibration = new MepDocumentCalibrationPickerSlot();
        _productionDispatchers = new RevitExecutionDispatcherFactory
        {
            ApprovalUi = _approvalUi,
            DocumentCalibration = calibration,
        };
        _lifecycle = new AddinLifecycleCoordinator(
            _productionDispatchers,
            new NamedPipeLifecycleBridgeFactory(),
            new ProcessRuntimeMetadataSource());
        _assistant = new AssistantShell(
            () => _lifecycle.InstanceId,
            () => _lifecycle.State == AddinLifecycleState.Ready,
            () => (_lifecycle.Metadata?.RevitVersion, _lifecycle.Metadata?.RevitBuild),
            new ProcessAssistantFactory(),
            new NamedPipeAssistantLink(),
            calibrationPicker: calibration);
        AssistantCommandBridge.Attach(_assistant);
        _assistant.OnRevitStartup();
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

                if (_assistant is not null)
                {
                    try
                    {
                        application.RegisterDockablePane(
                            new DockablePaneId(AssistantPaneIds.PaneId),
                            AssistantPaneIds.Title,
                            new AssistantPaneProvider(_assistant));
                        AssistantRibbonRegistration.Register(application);
                    }
                    catch (Exception)
                    {
                        // Ribbon or pane registration failure leaves the Bridge and Approval usable.
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
        AddinHostShutdown.Complete(_lifecycle, _assistant, _approvalUi);
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
