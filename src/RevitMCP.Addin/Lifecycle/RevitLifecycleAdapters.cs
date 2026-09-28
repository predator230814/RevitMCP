using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
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
    private readonly OpenDocumentIdentityService _identity;
    private readonly OpenDocumentParameterIdentityService _parameterRefs;
    private IDisposable? _closeCleanup;
    private int _disposed;

    public RevitExecutionDispatcherLifetime(
        RevitExecutionDispatcher dispatcher,
        IDocumentCloseEventSource? closeEvents = null)
        : this(dispatcher, new EphemeralWriteIntentStore(), closeEvents)
    {
    }

    internal RevitExecutionDispatcherLifetime(
        RevitExecutionDispatcher dispatcher,
        EphemeralWriteIntentStore intentStore,
        IDocumentCloseEventSource? closeEvents = null)
        : this(intentStore, BindExecution(dispatcher))
    {
        if (closeEvents is not null)
        {
            _closeCleanup = closeEvents.Subscribe(new DocumentCloseIdentityCleanup<Autodesk.Revit.DB.Document>(ForgetDocument));
        }
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
        IDisposable? closeCleanup = null)
    {
        ArgumentNullException.ThrowIfNull(intentStore);
        ArgumentNullException.ThrowIfNull(stopExecution);
        ArgumentNullException.ThrowIfNull(disposeExecution);
        _dispatcher = null!;
        _stopExecution = stopExecution;
        _disposeExecution = disposeExecution;
        _intentStore = intentStore;
        _closeCleanup = closeCleanup;
        _identity = null!;
        _parameterRefs = null!;
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
            _intentStore,
            () => _parameterRefs.Forget(document),
            () => _identity.Forget(document));
    }

    internal static bool ApplySuccessfulDocumentClose(
        bool hasExistingDocumentId,
        string? documentId,
        EphemeralWriteIntentStore intentStore,
        Func<bool> forgetParameterRefs,
        Func<bool> forgetDocumentIdentity)
    {
        ArgumentNullException.ThrowIfNull(intentStore);
        ArgumentNullException.ThrowIfNull(forgetParameterRefs);
        ArgumentNullException.ThrowIfNull(forgetDocumentIdentity);

        var forgottenIntent = false;
        if (hasExistingDocumentId)
        {
            ArgumentNullException.ThrowIfNull(documentId);
            forgottenIntent = intentStore.ForgetDocument(documentId) > 0;
        }

        var forgottenRefs = forgetParameterRefs();
        var forgottenIdentity = forgetDocumentIdentity();
        return forgottenIntent || forgottenRefs || forgottenIdentity;
    }

    public void Stop()
    {
        _intentStore.Clear();
        _stopExecution();
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
}

internal sealed class RevitExecutionDispatcherFactory : ILifecycleDispatcherFactory
{
    public IDocumentCloseEventSource? CloseEvents { get; set; }

    public ILifecycleDispatcher Create() =>
        new RevitExecutionDispatcherLifetime(RevitExecutionDispatcher.Create(), CloseEvents);
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
        CancellationToken cancellationToken)
    {
        var host = NamedPipeBridgeHost
            .StartAsync(metadata, _store, capability, query, getElements, describeParameters, getParameterValues, getMepTopology, cancellationToken)
            .GetAwaiter()
            .GetResult();
        return new NamedPipeLifecycleBridge(host);
    }
}
