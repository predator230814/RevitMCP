using RevitMCP.Addin.Lifecycle;
using RevitMCP.Bridge;
using RevitMCP.Contracts;

namespace RevitMCP.Addin.Tests;

internal sealed class RecordingBootstrapScheduler : IBootstrapScheduler
{
    private Action<object>? _callback;

    public int SubscribeCount { get; private set; }

    public RecordingBootstrapSubscription? Subscription { get; private set; }

    public IBootstrapSubscription Subscribe(Action<object> onBootstrap)
    {
        SubscribeCount++;
        _callback = onBootstrap;
        Subscription = new RecordingBootstrapSubscription();
        return Subscription;
    }

    public void Raise(object sender)
    {
        _callback?.Invoke(sender);
    }
}

internal sealed class RecordingBootstrapSubscription : IBootstrapSubscription
{
    public int DisposeCount { get; private set; }

    public void Dispose() => DisposeCount++;
}

internal sealed class ThrowingBootstrapSubscription : IBootstrapSubscription
{
    public void Dispose() => throw new InvalidOperationException("unsubscribe failed");
}

internal sealed class RecordingDispatcher : ILifecycleDispatcher
{
    private readonly List<string> _events;

    public RecordingDispatcher(List<string> events)
    {
        _events = events;
    }

    public void Stop() => _events.Add("dispatcher.stop");

    public void Dispose() => _events.Add("dispatcher.dispose");

    public IRevitCapabilityService? CreateCapability(BridgeInstanceMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        _events.Add("capability.create");
        return new RecordingCapabilityService();
    }

    public IRevitQueryElementsService? CreateQuery(BridgeInstanceMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        _events.Add("query.create");
        return new RecordingQueryElementsService();
    }

    public IRevitGetElementsService? CreateGetElements(BridgeInstanceMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        _events.Add("getelements.create");
        return new RecordingGetElementsService();
    }

    public IRevitDescribeParametersService? CreateDescribeParameters(BridgeInstanceMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        _events.Add("describe.create");
        return new RecordingDescribeParametersService();
    }
}

internal sealed class RecordingCapabilityService : IRevitCapabilityService
{
    public Task<GetContextResult> GetContextAsync(GetContextRequest request, CancellationToken cancellationToken)
    {
        _ = request;
        _ = cancellationToken;
        throw new NotSupportedException("Lifecycle recording capability does not execute Revit work.");
    }
}

internal sealed class RecordingQueryElementsService : IRevitQueryElementsService
{
    public Task<QueryElementsResult> QueryElementsAsync(QueryElementsRequest request, CancellationToken cancellationToken)
    {
        _ = request;
        _ = cancellationToken;
        throw new NotSupportedException("Lifecycle recording query does not execute Revit work.");
    }
}

internal sealed class RecordingGetElementsService : IRevitGetElementsService
{
    public Task<GetElementsResult> GetElementsAsync(GetElementsRequest request, CancellationToken cancellationToken)
    {
        _ = request;
        _ = cancellationToken;
        throw new NotSupportedException("Lifecycle recording get-elements does not execute Revit work.");
    }
}

internal sealed class RecordingDescribeParametersService : IRevitDescribeParametersService
{
    public Task<DescribeParametersResult> DescribeParametersAsync(
        DescribeParametersRequest request,
        CancellationToken cancellationToken)
    {
        _ = request;
        _ = cancellationToken;
        throw new NotSupportedException("Lifecycle recording describe-parameters does not execute Revit work.");
    }
}

internal sealed class RecordingDispatcherFactory : ILifecycleDispatcherFactory
{
    private readonly List<string> _events;
    private readonly ManualResetEventSlim? _entered;
    private readonly ManualResetEventSlim? _release;

    public RecordingDispatcherFactory(
        List<string> events,
        ManualResetEventSlim? entered = null,
        ManualResetEventSlim? release = null)
    {
        _events = events;
        _entered = entered;
        _release = release;
    }

    public int CreateCount { get; private set; }

    public ILifecycleDispatcher Create()
    {
        CreateCount++;
        _events.Add("dispatcher.create");
        _entered?.Set();
        _release?.Wait();
        return new RecordingDispatcher(_events);
    }
}

internal sealed class RecordingBridge : ILifecycleBridge
{
    private readonly List<string> _events;
    private readonly ManualResetEventSlim? _disposeEntered;
    private readonly ManualResetEventSlim? _disposeRelease;

    public RecordingBridge(
        List<string> events,
        bool hasRegistration,
        ManualResetEventSlim? disposeEntered = null,
        ManualResetEventSlim? disposeRelease = null)
    {
        _events = events;
        HasRegistration = hasRegistration;
        _disposeEntered = disposeEntered;
        _disposeRelease = disposeRelease;
    }

    public bool HasRegistration { get; private set; }

    public ValueTask DisposeAsync()
    {
        _disposeEntered?.Set();
        _disposeRelease?.Wait();
        _events.Add("registration.withdraw");
        _events.Add("bridge.dispose");
        HasRegistration = false;
        return ValueTask.CompletedTask;
    }
}

internal sealed class RecordingBridgeFactory : ILifecycleBridgeFactory
{
    private readonly List<string> _events;
    private readonly Exception? _startError;
    private readonly ManualResetEventSlim? _disposeEntered;
    private readonly ManualResetEventSlim? _disposeRelease;

    public RecordingBridgeFactory(
        List<string> events,
        Exception? startError = null,
        ManualResetEventSlim? disposeEntered = null,
        ManualResetEventSlim? disposeRelease = null)
    {
        _events = events;
        _startError = startError;
        _disposeEntered = disposeEntered;
        _disposeRelease = disposeRelease;
        Published = false;
    }

    public bool Published { get; private set; }

    public RecordingBridge? LastBridge { get; private set; }

    public IRevitCapabilityService? LastCapability { get; private set; }

    public IRevitQueryElementsService? LastQuery { get; private set; }

    public IRevitGetElementsService? LastGetElements { get; private set; }

    public IRevitDescribeParametersService? LastDescribeParameters { get; private set; }

    public ILifecycleBridge Start(
        BridgeInstanceMetadata metadata,
        IRevitCapabilityService? capability,
        IRevitQueryElementsService? query,
        IRevitGetElementsService? getElements,
        IRevitDescribeParametersService? describeParameters,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastCapability = capability;
        LastQuery = query;
        LastGetElements = getElements;
        LastDescribeParameters = describeParameters;
        _events.Add("bridge.start");
        if (_startError is not null)
        {
            throw _startError;
        }

        Published = true;
        _events.Add("registration.publish");
        LastBridge = new RecordingBridge(_events, hasRegistration: true, _disposeEntered, _disposeRelease);
        return LastBridge;
    }
}

internal sealed class FixedMetadataSource : IRuntimeMetadataSource
{
    public BridgeInstanceMetadata Capture(string instanceId, LifecycleRevitRuntime revit)
    {
        return new BridgeInstanceMetadata
        {
            InstanceId = instanceId,
            ProcessId = 11,
            ProcessStartTimeUtc = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero),
            WindowsSessionId = 2,
            RevitVersion = revit.VersionNumber,
            RevitBuild = revit.VersionBuild,
            AddinVersion = "1.0.0",
            SupportedProtocolVersions = BridgeProtocol.SupportedVersions
        };
    }
}
