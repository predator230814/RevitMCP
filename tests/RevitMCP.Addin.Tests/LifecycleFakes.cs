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

    public ILifecycleBridge Start(BridgeInstanceMetadata metadata, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
