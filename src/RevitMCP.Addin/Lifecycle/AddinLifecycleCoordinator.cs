using RevitMCP.Bridge;

namespace RevitMCP.Addin.Lifecycle;

internal sealed class AddinLifecycleCoordinator
{
    private readonly object _gate = new();
    private readonly ILifecycleDispatcherFactory _dispatchers;
    private readonly ILifecycleBridgeFactory _bridges;
    private readonly IRuntimeMetadataSource _metadata;
    private readonly TimeSpan _startupTimeout;
    private readonly TimeSpan _shutdownTimeout;
    private IBootstrapSubscription? _subscription;
    private ILifecycleDispatcher? _dispatcher;
    private ILifecycleBridge? _bridge;
    private AddinLifecycleState _state = AddinLifecycleState.Created;

    public AddinLifecycleCoordinator(
        ILifecycleDispatcherFactory dispatchers,
        ILifecycleBridgeFactory bridges,
        IRuntimeMetadataSource metadata,
        TimeSpan? startupTimeout = null,
        TimeSpan? shutdownTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(dispatchers);
        ArgumentNullException.ThrowIfNull(bridges);
        ArgumentNullException.ThrowIfNull(metadata);
        _dispatchers = dispatchers;
        _bridges = bridges;
        _metadata = metadata;
        _startupTimeout = startupTimeout ?? LifecycleMaintenanceTimeouts.Startup;
        _shutdownTimeout = shutdownTimeout ?? LifecycleMaintenanceTimeouts.Shutdown;
        InstanceId = Guid.NewGuid().ToString("D");
    }

    public string InstanceId { get; }

    public AddinLifecycleState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public string? FailureReason { get; private set; }

    public BridgeInstanceMetadata? Metadata { get; private set; }

    public void Prepare(IBootstrapSubscription subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        lock (_gate)
        {
            if (_state != AddinLifecycleState.Created)
            {
                throw new InvalidOperationException("Lifecycle startup can be prepared only once.");
            }

            _subscription = subscription;
            _state = AddinLifecycleState.BootstrapPending;
        }
    }

    public void TryBootstrap(LifecycleRevitRuntime revit)
    {
        IBootstrapSubscription? subscription;
        lock (_gate)
        {
            if (_state != AddinLifecycleState.BootstrapPending)
            {
                return;
            }

            _state = AddinLifecycleState.Starting;
            subscription = _subscription;
            _subscription = null;
        }

        ILifecycleDispatcher? dispatcher = null;
        ILifecycleBridge? bridge = null;
        try
        {
            subscription?.Dispose();
            EnsureNotStopping();
            var metadata = _metadata.Capture(InstanceId, revit);
            Metadata = metadata;
            EnsureNotStopping();

            dispatcher = _dispatchers.Create();
            EnsureNotStopping();

            var capability = dispatcher.CreateCapability(metadata);
            EnsureNotStopping();

            var query = dispatcher.CreateQuery(metadata);
            EnsureNotStopping();

            var getElements = dispatcher.CreateGetElements(metadata);
            EnsureNotStopping();

            using var startup = new CancellationTokenSource(_startupTimeout);
            bridge = _bridges.Start(metadata, capability, query, getElements, startup.Token);

            lock (_gate)
            {
                if (_state is AddinLifecycleState.Stopping or AddinLifecycleState.Stopped)
                {
                    // Fall through to dispose locals without becoming Ready.
                }
                else
                {
                    _dispatcher = dispatcher;
                    _bridge = bridge;
                    _state = AddinLifecycleState.Ready;
                    dispatcher = null;
                    bridge = null;
                    return;
                }
            }
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
            lock (_gate)
            {
                if (_state is not (AddinLifecycleState.Stopping or AddinLifecycleState.Stopped))
                {
                    _state = AddinLifecycleState.Faulted;
                }
            }
        }

        DisposeOwned(bridge, dispatcher);
    }

    public void Shutdown()
    {
        IBootstrapSubscription? subscription;
        ILifecycleDispatcher? dispatcher;
        ILifecycleBridge? bridge;
        lock (_gate)
        {
            if (_state is AddinLifecycleState.Stopping or AddinLifecycleState.Stopped)
            {
                return;
            }

            _state = AddinLifecycleState.Stopping;
            subscription = _subscription;
            _subscription = null;
            dispatcher = _dispatcher;
            bridge = _bridge;
        }

        try
        {
            subscription?.Dispose();
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
        }

        try
        {
            dispatcher?.Stop();
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
        }

        DisposeBridgeBounded(bridge);

        try
        {
            dispatcher?.Dispose();
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
        }

        lock (_gate)
        {
            _dispatcher = null;
            _bridge = null;
            _state = AddinLifecycleState.Stopped;
        }
    }

    private void EnsureNotStopping()
    {
        lock (_gate)
        {
            if (_state is AddinLifecycleState.Stopping or AddinLifecycleState.Stopped)
            {
                throw new InvalidOperationException("Lifecycle shutdown has already begun.");
            }
        }
    }

    private void DisposeOwned(ILifecycleBridge? bridge, ILifecycleDispatcher? dispatcher)
    {
        DisposeBridgeBounded(bridge);

        try
        {
            dispatcher?.Stop();
            dispatcher?.Dispose();
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
        }
    }

    private void DisposeBridgeBounded(ILifecycleBridge? bridge)
    {
        if (bridge is null)
        {
            return;
        }

        var dispose = bridge.DisposeAsync().AsTask();
        using var shutdown = new CancellationTokenSource(_shutdownTimeout);
        try
        {
            dispose.WaitAsync(shutdown.Token).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            ObserveIncomplete(dispose);
            RecordFailure(exception);
        }
    }

    private void RecordFailure(Exception exception)
    {
        FailureReason ??= exception.GetType().Name + ": " + exception.Message;
    }

    private static void ObserveIncomplete(Task task)
    {
        if (task.IsCompleted)
        {
            _ = task.Exception;
            return;
        }

        _ = task.ContinueWith(
            completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
