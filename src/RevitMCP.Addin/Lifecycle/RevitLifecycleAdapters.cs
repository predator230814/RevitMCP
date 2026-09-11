using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using RevitMCP.Addin.Capabilities;
using RevitMCP.Addin.Execution;
using RevitMCP.Addin.Identity;
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
    private readonly OpenDocumentIdentityService _identity;
    private readonly OpenDocumentParameterIdentityService _parameterRefs;
    private readonly IDisposable? _closeCleanup;

    public RevitExecutionDispatcherLifetime(
        RevitExecutionDispatcher dispatcher,
        IDocumentCloseEventSource? closeEvents = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _dispatcher = dispatcher;
        _identity = new OpenDocumentIdentityService();
        _parameterRefs = new OpenDocumentParameterIdentityService();
        if (closeEvents is not null)
        {
            _closeCleanup = closeEvents.Subscribe(new DocumentCloseIdentityCleanup<Autodesk.Revit.DB.Document>(ForgetDocument));
        }
    }

    private bool ForgetDocument(Autodesk.Revit.DB.Document document)
    {
        var forgottenId = _identity.Forget(document);
        var forgottenRefs = _parameterRefs.Forget(document);
        return forgottenId || forgottenRefs;
    }

    public void Stop() => _dispatcher.Stop();

    public void Dispose()
    {
        _closeCleanup?.Dispose();
        _dispatcher.Dispose();
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
        CancellationToken cancellationToken)
    {
        var host = NamedPipeBridgeHost
            .StartAsync(metadata, _store, capability, query, getElements, describeParameters, cancellationToken)
            .GetAwaiter()
            .GetResult();
        return new NamedPipeLifecycleBridge(host);
    }
}
