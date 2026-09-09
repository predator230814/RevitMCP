using RevitMCP.Addin.Lifecycle;
using RevitMCP.Contracts;
using Xunit;

namespace RevitMCP.Addin.Tests;

public sealed class AddinLifecycleCoordinatorTests
{
    private static readonly LifecycleRevitRuntime Runtime = new("2026", "26.5.0.0");

    [Fact]
    public void Startup_subscribes_exactly_one_bootstrap_callback_without_creating_runtime()
    {
        var events = new List<string>();
        var dispatchers = new RecordingDispatcherFactory(events);
        var bridges = new RecordingBridgeFactory(events);
        var scheduler = new RecordingBootstrapScheduler();
        var coordinator = CreateCoordinator(dispatchers, bridges);

        coordinator.Prepare(scheduler.Subscribe(_ => { }));

        Assert.Equal(1, scheduler.SubscribeCount);
        Assert.Equal(AddinLifecycleState.BootstrapPending, coordinator.State);
        Assert.Equal(0, dispatchers.CreateCount);
        Assert.Empty(events);
        Assert.False(string.IsNullOrWhiteSpace(coordinator.InstanceId));
    }

    [Fact]
    public void Duplicate_bootstrap_notifications_initialize_at_most_once()
    {
        var events = new List<string>();
        var dispatchers = new RecordingDispatcherFactory(events);
        var bridges = new RecordingBridgeFactory(events);
        var coordinator = Prepare(dispatchers, bridges);

        coordinator.TryBootstrap(Runtime);
        coordinator.TryBootstrap(Runtime);

        Assert.Equal(AddinLifecycleState.Ready, coordinator.State);
        Assert.Equal(1, dispatchers.CreateCount);
        Assert.Equal(new[] { "dispatcher.create", "bridge.start", "registration.publish" }, events);
    }

    [Fact]
    public void Successful_bootstrap_creates_dispatcher_before_bridge_and_reaches_ready()
    {
        var events = new List<string>();
        var dispatchers = new RecordingDispatcherFactory(events);
        var bridges = new RecordingBridgeFactory(events);
        var coordinator = Prepare(dispatchers, bridges);

        coordinator.TryBootstrap(Runtime);

        Assert.Equal(AddinLifecycleState.Ready, coordinator.State);
        Assert.Equal("dispatcher.create", events[0]);
        Assert.Equal("bridge.start", events[1]);
        Assert.Equal("registration.publish", events[2]);
        Assert.True(bridges.Published);
        Assert.True(bridges.LastBridge?.HasRegistration);
        Assert.NotNull(coordinator.Metadata);
        Assert.Equal(coordinator.InstanceId, coordinator.Metadata!.InstanceId);
        Assert.Equal("2026", coordinator.Metadata.RevitVersion);
        Assert.Equal(BridgeProtocol.SupportedVersions, coordinator.Metadata.SupportedProtocolVersions);
    }

    [Fact]
    public void Zero_active_document_is_not_required_to_become_ready()
    {
        var events = new List<string>();
        var coordinator = Prepare(new RecordingDispatcherFactory(events), new RecordingBridgeFactory(events));

        coordinator.TryBootstrap(new LifecycleRevitRuntime("2025", "25.0.0.0"));

        Assert.Equal(AddinLifecycleState.Ready, coordinator.State);
    }

    [Fact]
    public void Bridge_startup_failure_disposes_dispatcher_and_leaves_no_registration()
    {
        var events = new List<string>();
        var dispatchers = new RecordingDispatcherFactory(events);
        var bridges = new RecordingBridgeFactory(events, new InvalidOperationException("pipe failed"));
        var coordinator = Prepare(dispatchers, bridges);

        coordinator.TryBootstrap(Runtime);

        Assert.Equal(AddinLifecycleState.Faulted, coordinator.State);
        Assert.False(bridges.Published);
        Assert.Contains("dispatcher.create", events);
        Assert.Contains("dispatcher.stop", events);
        Assert.Contains("dispatcher.dispose", events);
        Assert.DoesNotContain("registration.publish", events);
        Assert.False(string.IsNullOrWhiteSpace(coordinator.FailureReason));
    }

    [Fact]
    public void Shutdown_before_first_idling_prevents_bootstrap()
    {
        var events = new List<string>();
        var dispatchers = new RecordingDispatcherFactory(events);
        var scheduler = new RecordingBootstrapScheduler();
        var coordinator = CreateCoordinator(dispatchers, new RecordingBridgeFactory(events));
        coordinator.Prepare(scheduler.Subscribe(_ => coordinator.TryBootstrap(Runtime)));

        coordinator.Shutdown();
        scheduler.Raise(new object());

        Assert.Equal(AddinLifecycleState.Stopped, coordinator.State);
        Assert.Equal(0, dispatchers.CreateCount);
        Assert.Equal(1, scheduler.Subscription!.DisposeCount);
        Assert.Empty(events);
    }

    [Fact]
    public void Shutdown_after_ready_stops_dispatcher_before_bridge_teardown()
    {
        var events = new List<string>();
        var dispatchers = new RecordingDispatcherFactory(events);
        var bridges = new RecordingBridgeFactory(events);
        var coordinator = Prepare(dispatchers, bridges);
        coordinator.TryBootstrap(Runtime);

        coordinator.Shutdown();

        Assert.Equal(AddinLifecycleState.Stopped, coordinator.State);
        Assert.Equal(
            new[]
            {
                "dispatcher.create",
                "bridge.start",
                "registration.publish",
                "dispatcher.stop",
                "registration.withdraw",
                "bridge.dispose",
                "dispatcher.dispose"
            },
            events);
        Assert.False(bridges.LastBridge!.HasRegistration);
    }

    [Fact]
    public void Shutdown_is_idempotent()
    {
        var events = new List<string>();
        var coordinator = Prepare(new RecordingDispatcherFactory(events), new RecordingBridgeFactory(events));
        coordinator.TryBootstrap(Runtime);

        coordinator.Shutdown();
        coordinator.Shutdown();

        Assert.Equal(AddinLifecycleState.Stopped, coordinator.State);
        Assert.Equal(1, events.Count(item => item == "dispatcher.stop"));
        Assert.Equal(1, events.Count(item => item == "dispatcher.dispose"));
        Assert.Equal(1, events.Count(item => item == "bridge.dispose"));
    }

    [Fact]
    public async Task Reentrant_shutdown_while_stopping_owns_cleanup_once()
    {
        var events = new List<string>();
        using var disposeEntered = new ManualResetEventSlim(false);
        using var disposeRelease = new ManualResetEventSlim(false);
        var coordinator = Prepare(
            new RecordingDispatcherFactory(events),
            new RecordingBridgeFactory(events, disposeEntered: disposeEntered, disposeRelease: disposeRelease));
        coordinator.TryBootstrap(Runtime);

        var first = Task.Run(coordinator.Shutdown);
        Assert.True(disposeEntered.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(AddinLifecycleState.Stopping, coordinator.State);

        coordinator.Shutdown();
        disposeRelease.Set();
        await first.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(AddinLifecycleState.Stopped, coordinator.State);
        Assert.Equal(1, events.Count(item => item == "dispatcher.stop"));
        Assert.Equal(1, events.Count(item => item == "dispatcher.dispose"));
        Assert.Equal(1, events.Count(item => item == "bridge.dispose"));
    }

    [Fact]
    public void Bootstrap_unsubscribe_failure_is_contained_and_faults()
    {
        var events = new List<string>();
        var dispatchers = new RecordingDispatcherFactory(events);
        var coordinator = CreateCoordinator(dispatchers, new RecordingBridgeFactory(events));
        coordinator.Prepare(new ThrowingBootstrapSubscription());

        var thrown = Record.Exception(() => coordinator.TryBootstrap(Runtime));

        Assert.Null(thrown);
        Assert.Equal(AddinLifecycleState.Faulted, coordinator.State);
        Assert.Equal(0, dispatchers.CreateCount);
        Assert.DoesNotContain("registration.publish", events);
        Assert.Contains("unsubscribe failed", coordinator.FailureReason);
    }

    [Fact]
    public async Task Bootstrap_cannot_become_ready_after_shutdown_begins()
    {
        var events = new List<string>();
        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        var dispatchers = new RecordingDispatcherFactory(events, entered, release);
        var coordinator = Prepare(dispatchers, new RecordingBridgeFactory(events));

        var bootstrap = Task.Run(() => coordinator.TryBootstrap(Runtime));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        coordinator.Shutdown();
        release.Set();
        await bootstrap.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotEqual(AddinLifecycleState.Ready, coordinator.State);
        Assert.Equal(AddinLifecycleState.Stopped, coordinator.State);
        Assert.DoesNotContain("registration.publish", events);
    }

    [Fact]
    public void Metadata_contains_only_accepted_process_revit_and_addin_fields()
    {
        var source = new ProcessRuntimeMetadataSource();
        var metadata = source.Capture("instance-a", new LifecycleRevitRuntime("2027", "27.2.0.0"));
        var names = metadata.GetType().GetProperties().Select(property => property.Name).OrderBy(name => name).ToArray();

        Assert.Equal(
            new[]
            {
                nameof(metadata.AddinVersion),
                nameof(metadata.InstanceId),
                nameof(metadata.ProcessId),
                nameof(metadata.ProcessStartTimeUtc),
                nameof(metadata.RevitBuild),
                nameof(metadata.RevitVersion),
                nameof(metadata.SupportedProtocolVersions),
                nameof(metadata.WindowsSessionId)
            },
            names);
        Assert.Equal("instance-a", metadata.InstanceId);
        Assert.Equal("2027", metadata.RevitVersion);
        Assert.Equal("27.2.0.0", metadata.RevitBuild);
        Assert.Equal(AddinVersionInfo.Current, metadata.AddinVersion);
        Assert.Equal(BridgeProtocol.SupportedVersions, metadata.SupportedProtocolVersions);
        Assert.Equal(Environment.ProcessId, metadata.ProcessId);
    }

    private static AddinLifecycleCoordinator Prepare(
        ILifecycleDispatcherFactory dispatchers,
        ILifecycleBridgeFactory bridges)
    {
        var coordinator = CreateCoordinator(dispatchers, bridges);
        coordinator.Prepare(new RecordingBootstrapSubscription());
        return coordinator;
    }

    private static AddinLifecycleCoordinator CreateCoordinator(
        ILifecycleDispatcherFactory dispatchers,
        ILifecycleBridgeFactory bridges)
    {
        return new AddinLifecycleCoordinator(
            dispatchers,
            bridges,
            new FixedMetadataSource(),
            startupTimeout: TimeSpan.FromSeconds(2),
            shutdownTimeout: TimeSpan.FromSeconds(2));
    }
}
