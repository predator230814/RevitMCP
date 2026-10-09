using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using System.IO;
using RevitMCP.Addin.Apply;
using RevitMCP.Addin.Approval;
using RevitMCP.Addin.Audit;
using RevitMCP.Addin.Capabilities;
using RevitMCP.Addin.Execution;
using RevitMCP.Addin.Identity;
using RevitMCP.Addin.Intents;
using RevitMCP.Bridge;

namespace RevitMCP.Addin.Lifecycle;

internal sealed class RevitIdlingScheduler : IBootstrapScheduler
{
    private readonly UIControlledApplication _application;

    public RevitIdlingScheduler(UIControlledApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);
        _application = application;
    }

    public IBootstrapSubscription Subscribe(Action<object> onBootstrap)
    {
        ArgumentNullException.ThrowIfNull(onBootstrap);
        return new RevitIdlingSubscription(_application, onBootstrap);
    }
}

internal sealed class RevitIdlingSubscription : IBootstrapSubscription
{
    private readonly UIControlledApplication _application;
    private readonly EventHandler<IdlingEventArgs> _handler;
    private int _disposed;

    public RevitIdlingSubscription(UIControlledApplication application, Action<object> onBootstrap)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(onBootstrap);
        _application = application;
        _handler = (sender, _) =>
        {
            if (sender is not null)
            {
                onBootstrap(sender);
            }
        };
        _application.Idling += _handler;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _application.Idling -= _handler;
    }
}

internal sealed class RevitExecutionDispatcherLifetime : ILifecycleDispatcher
{
    private readonly RevitExecutionDispatcher _dispatcher;
    private readonly Action _stopExecution;
    private readonly Action _disposeExecution;
    private readonly EphemeralWriteIntentStore _intentStore;
    private readonly ControlledApplyAttemptStore _applyAttempts;
    private readonly ControlledWriteAuditWriter _auditWriter;
    private readonly RevitLocalApprovalProviderStateMachine _approval;
    private readonly RevitLocalApprovalInteractionController _interaction;
    private readonly OpenDocumentIdentityService _identity;
    private readonly OpenDocumentParameterIdentityService _parameterRefs;
    private IDisposable? _closeCleanup;
    private IDisposable? _activeDocumentObservation;
    private ApprovalUiRuntime? _approvalUi;
    private int _disposed;

    internal RevitLocalApprovalProviderStateMachine ApprovalProvider => _approval;

    internal ControlledApplyAttemptStore ApplyAttempts => _applyAttempts;

    internal ControlledWriteAuditWriter AuditWriter => _auditWriter;

    internal RevitLocalApprovalInteractionController ApprovalInteraction => _interaction;

    public RevitExecutionDispatcherLifetime(
        RevitExecutionDispatcher dispatcher,
        IDocumentCloseEventSource? closeEvents = null,
        IActiveDocumentEventSource? activeDocumentEvents = null)
        : this(dispatcher, new EphemeralWriteIntentStore(), closeEvents, activeDocumentEvents)
    {
    }

    internal RevitExecutionDispatcherLifetime(
        RevitExecutionDispatcher dispatcher,
        EphemeralWriteIntentStore intentStore,
        IDocumentCloseEventSource? closeEvents = null,
        IActiveDocumentEventSource? activeDocumentEvents = null)
        : this(intentStore, BindExecution(dispatcher))
    {
        if (closeEvents is not null)
        {
            _closeCleanup = closeEvents.Subscribe(new DocumentCloseIdentityCleanup<Autodesk.Revit.DB.Document>(ForgetDocument));
        }

        TrySubscribeActiveDocument(activeDocumentEvents);
    }

    private RevitExecutionDispatcherLifetime(
        EphemeralWriteIntentStore intentStore,
        (Action Stop, Action Dispose, RevitExecutionDispatcher Dispatcher) execution)
        : this(intentStore, execution.Stop, execution.Dispose, execution.Dispatcher)
    {
    }

    private static (Action Stop, Action Dispose, RevitExecutionDispatcher Dispatcher) BindExecution(RevitExecutionDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        return (dispatcher.Stop, dispatcher.Dispose, dispatcher);
    }

    internal RevitExecutionDispatcherLifetime(
        EphemeralWriteIntentStore intentStore,
        Action stopExecution,
        Action disposeExecution,
        IDisposable? closeCleanup = null,
        IActiveDocumentEventSource? activeDocumentEvents = null,
        AuditWriterOptions? auditOptions = null)
    {
        ArgumentNullException.ThrowIfNull(intentStore);
        ArgumentNullException.ThrowIfNull(stopExecution);
        ArgumentNullException.ThrowIfNull(disposeExecution);
        _dispatcher = null!;
        _stopExecution = stopExecution;
        _disposeExecution = disposeExecution;
        _intentStore = intentStore;
        _applyAttempts = new ControlledApplyAttemptStore();
        _auditWriter = new ControlledWriteAuditWriter(options: auditOptions);
        if (auditOptions is not null)
        {
            _auditWriter.TryHousekeepingAtProcessStart();
        }
        _approval = new RevitLocalApprovalProviderStateMachine(intentStore);
        _interaction = new RevitLocalApprovalInteractionController(intentStore, _approval);
        _closeCleanup = closeCleanup;
        _identity = null!;
        _parameterRefs = null!;
        TrySubscribeActiveDocument(activeDocumentEvents);
    }

    private RevitExecutionDispatcherLifetime(
        EphemeralWriteIntentStore intentStore,
        Action stopExecution,
        Action disposeExecution,
        RevitExecutionDispatcher dispatcher,
        IDisposable? closeCleanup = null)
    {
        ArgumentNullException.ThrowIfNull(intentStore);
        ArgumentNullException.ThrowIfNull(stopExecution);
        ArgumentNullException.ThrowIfNull(disposeExecution);
        _dispatcher = dispatcher;
        _stopExecution = stopExecution;
        _disposeExecution = disposeExecution;
        _intentStore = intentStore;
        _applyAttempts = new ControlledApplyAttemptStore();
        _auditWriter = new ControlledWriteAuditWriter();
        _auditWriter.TryHousekeepingAtProcessStart();
        _approval = new RevitLocalApprovalProviderStateMachine(intentStore);
        _interaction = new RevitLocalApprovalInteractionController(intentStore, _approval);
        _closeCleanup = closeCleanup;
        _identity = new OpenDocumentIdentityService();
        _parameterRefs = new OpenDocumentParameterIdentityService();
    }

    private bool ForgetDocument(Autodesk.Revit.DB.Document document)
    {
        var hasExistingDocumentId = _identity.TryGet(document, out var documentId);
        return ApplySuccessfulDocumentClose(
            hasExistingDocumentId,
            documentId,
            _approval.ForgetDocument,
            _applyAttempts.ForgetDocument,
            _intentStore.ForgetDocument,
            () => _parameterRefs.Forget(document),
            () => _identity.Forget(document));
    }

    internal static bool ApplySuccessfulDocumentClose(
        bool hasExistingDocumentId,
        string? documentId,
        Func<string, int> forgetProvider,
        Func<string, int> forgetApplyAttempts,
        Func<string, int> forgetIntent,
        Func<bool> forgetParameterRefs,
        Func<bool> forgetDocumentIdentity)
    {
        ArgumentNullException.ThrowIfNull(forgetProvider);
        ArgumentNullException.ThrowIfNull(forgetApplyAttempts);
        ArgumentNullException.ThrowIfNull(forgetIntent);
        ArgumentNullException.ThrowIfNull(forgetParameterRefs);
        ArgumentNullException.ThrowIfNull(forgetDocumentIdentity);

        var forgotten = false;
        if (hasExistingDocumentId)
        {
            ArgumentNullException.ThrowIfNull(documentId);
            forgotten = forgetProvider(documentId) > 0;
            forgotten = forgetApplyAttempts(documentId) > 0 || forgotten;
            forgotten = forgetIntent(documentId) > 0 || forgotten;
        }

        var forgottenRefs = forgetParameterRefs();
        var forgottenIdentity = forgetDocumentIdentity();
        return forgotten || forgottenRefs || forgottenIdentity;
    }

    internal void ObserveMappedActiveDocument(bool hasExistingDocumentId, string? documentId)
    {
        _approval.ObserveActiveDocument(hasExistingDocumentId ? documentId : null);
    }

    public void Stop()
    {
        _approval.Stop();
        try
        {
            DetachActiveDocumentObservation();
        }
        finally
        {
            _approvalUi?.Detach();
            _applyAttempts.Clear();
            try
            {
                _auditWriter.Stop();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }

            _intentStore.Clear();
            _stopExecution();
        }
    }

    internal void AttachApprovalUi(ApprovalUiRuntime approvalUi)
    {
        ArgumentNullException.ThrowIfNull(approvalUi);
        if (_approvalUi is not null)
        {
            return;
        }

        _approvalUi = approvalUi;
        var gate = new RevitLocalApprovalPresentationGate(_dispatcher, _identity, approvalUi);
        var adapter = new RevitLocalApprovalPresentationAdapter(_interaction, approvalUi, gate);
        approvalUi.Attach(adapter, _interaction);
    }

    private void TrySubscribeActiveDocument(IActiveDocumentEventSource? activeDocumentEvents)
    {
        if (activeDocumentEvents is null)
        {
            return;
        }

        try
        {
            _activeDocumentObservation = activeDocumentEvents.Subscribe(OnActiveDocumentChanged, _identity);
        }
        catch (Exception)
        {
            _activeDocumentObservation = null;
            _approval.Stop();
        }
    }

    private void OnActiveDocumentChanged(bool hasExistingDocumentId, string? documentId)
    {
        ObserveMappedActiveDocument(hasExistingDocumentId, documentId);
    }

    private void DetachActiveDocumentObservation()
    {
        var observation = _activeDocumentObservation;
        _activeDocumentObservation = null;
        observation?.Dispose();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Stop();
        _closeCleanup?.Dispose();
        _disposeExecution();
    }

    public IRevitCapabilityService CreateCapability(BridgeInstanceMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return new RevitGetContextService(_dispatcher, metadata);
    }

    public IRevitQueryElementsService CreateQuery(BridgeInstanceMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return new RevitQueryElementsService(_dispatcher, metadata, _identity);
    }

    public IRevitGetElementsService CreateGetElements(BridgeInstanceMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return new RevitGetElementsService(_dispatcher, metadata, _identity);
    }

    public IRevitDescribeParametersService CreateDescribeParameters(BridgeInstanceMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return new RevitDescribeParametersService(_dispatcher, metadata, _identity, _parameterRefs);
    }

    public IRevitGetParameterValuesService CreateGetParameterValues(BridgeInstanceMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return new RevitGetParameterValuesService(_dispatcher, metadata, _identity, _parameterRefs);
    }

    public IRevitGetMepTopologyService CreateGetMepTopology(BridgeInstanceMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return new RevitGetMepTopologyService(_dispatcher, metadata, _identity);
    }

    public IRevitPreviewParameterUpdatesService CreatePreviewParameterUpdates(BridgeInstanceMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return new RevitPreviewParameterUpdatesService(_dispatcher, metadata, _identity, _parameterRefs, _intentStore);
    }

    public IRevitRequestParameterUpdateReviewService? CreateRequestParameterUpdateReview(BridgeInstanceMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return _approvalUi is null ? null : new RevitRequestParameterUpdateReviewService(_approvalUi);
    }

    public IRevitGetWarningsService CreateGetWarnings(BridgeInstanceMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return new RevitGetWarningsService(_dispatcher, metadata, _identity);
    }

    public IRevitApplyParameterUpdatesService CreateApplyParameterUpdates(BridgeInstanceMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return new RevitApplyParameterUpdatesService(
            _dispatcher,
            metadata,
            _identity,
            _parameterRefs,
            _intentStore,
            _approval,
            _applyAttempts,
            _auditWriter);
    }
}

internal sealed class RevitExecutionDispatcherFactory : ILifecycleDispatcherFactory
{
    public IDocumentCloseEventSource? CloseEvents { get; set; }

    public IActiveDocumentEventSource? ActiveDocumentEvents { get; set; }

    public ApprovalUiRuntime? ApprovalUi { get; set; }

    public ILifecycleDispatcher Create()
    {
        var lifetime = new RevitExecutionDispatcherLifetime(RevitExecutionDispatcher.Create(), CloseEvents, ActiveDocumentEvents);
        if (ApprovalUi is not null)
        {
            lifetime.AttachApprovalUi(ApprovalUi);
        }

        return lifetime;
    }
}

internal sealed class NamedPipeLifecycleBridge : ILifecycleBridge
{
    private readonly NamedPipeBridgeHost _host;

    public NamedPipeLifecycleBridge(NamedPipeBridgeHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
    }

    public bool HasRegistration => _host.Registration is not null;

    public ValueTask DisposeAsync() => _host.DisposeAsync();
}

internal sealed class NamedPipeLifecycleBridgeFactory : ILifecycleBridgeFactory
{
    private readonly IRegistrationStore _store;

    public NamedPipeLifecycleBridgeFactory(IRegistrationStore? store = null)
    {
        _store = store ?? new FileRegistrationStore();
    }

    public ILifecycleBridge Start(
        BridgeInstanceMetadata metadata,
        IRevitCapabilityService? capability,
        IRevitQueryElementsService? query,
        IRevitGetElementsService? getElements,
        IRevitDescribeParametersService? describeParameters,
        IRevitGetParameterValuesService? getParameterValues,
        IRevitGetMepTopologyService? getMepTopology,
        IRevitPreviewParameterUpdatesService? previewParameterUpdates,
        IRevitRequestParameterUpdateReviewService? requestParameterUpdateReview,
        IRevitApplyParameterUpdatesService? applyParameterUpdates,
        IRevitGetWarningsService? getWarnings,
        CancellationToken cancellationToken)
    {
        var host = NamedPipeBridgeHost
            .StartAsync(metadata, _store, capability, query, getElements, describeParameters, getParameterValues, getMepTopology, previewParameterUpdates, requestParameterUpdateReview, applyParameterUpdates, getWarnings, cancellationToken)
            .GetAwaiter()
            .GetResult();
        return new NamedPipeLifecycleBridge(host);
    }
}
