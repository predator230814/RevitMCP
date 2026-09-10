using RevitMCP.Addin.Lifecycle;
using RevitMCP.Bridge;
using Xunit;

namespace RevitMCP.Addin.Tests;

public sealed class CapabilityInfrastructureTests
{
    [Fact]
    public void Capability_sources_do_not_create_transactions_or_read_revit_on_construction()
    {
        var directory = Path.Combine(FindRepoRoot(), "src", "RevitMCP.Addin", "Capabilities");
        var sources = Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories);
        Assert.NotEmpty(sources);

        foreach (var source in sources)
        {
            var text = File.ReadAllText(source);
            Assert.DoesNotContain("new Transaction", text, StringComparison.Ordinal);
            Assert.DoesNotContain("new SubTransaction", text, StringComparison.Ordinal);
            Assert.DoesNotContain("new TransactionGroup", text, StringComparison.Ordinal);
            Assert.Contains("EnqueueAsync", text, StringComparison.Ordinal);
            Assert.DoesNotContain("GetElementIds().Select", text, StringComparison.Ordinal);
            Assert.DoesNotContain("PathName", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Username", text, StringComparison.Ordinal);
            Assert.DoesNotContain("CloudPath", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Compatibility_element_id_helper_formats_without_arithmetic()
    {
        var text = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "RevitMCP.Addin", "Compatibility", "RevitElementIds.cs"));
        Assert.Contains("InvariantCulture", text, StringComparison.Ordinal);
        Assert.Contains(".Value", text, StringComparison.Ordinal);
        Assert.DoesNotContain("IntegerValue", text, StringComparison.Ordinal);
        Assert.DoesNotContain(" + ", text, StringComparison.Ordinal);
        Assert.DoesNotContain(" - ", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Lifecycle_adapters_wire_dispatcher_backed_capability_into_bridge_start()
    {
        var adapters = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "RevitMCP.Addin", "Lifecycle", "RevitLifecycleAdapters.cs"));
        var coordinator = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "RevitMCP.Addin", "Lifecycle", "AddinLifecycleCoordinator.cs"));

        Assert.Contains("new RevitGetContextService(_dispatcher, metadata)", adapters, StringComparison.Ordinal);
        Assert.Contains("new RevitQueryElementsService(_dispatcher, metadata, _identity)", adapters, StringComparison.Ordinal);
        Assert.Contains("StartAsync(metadata, _store, capability, query, cancellationToken)", adapters, StringComparison.Ordinal);
        Assert.Contains("dispatcher.CreateCapability(metadata)", coordinator, StringComparison.Ordinal);
        Assert.Contains("dispatcher.CreateQuery(metadata)", coordinator, StringComparison.Ordinal);
        Assert.Contains("_bridges.Start(metadata, capability, query, startup.Token)", coordinator, StringComparison.Ordinal);
        Assert.DoesNotContain("GetContextRequest", adapters, StringComparison.Ordinal);
        Assert.DoesNotContain("GetContextResult", adapters, StringComparison.Ordinal);
        Assert.DoesNotContain("GetContextRequest", coordinator, StringComparison.Ordinal);
        Assert.DoesNotContain("UIApplication", coordinator, StringComparison.Ordinal);
    }

    [Fact]
    public void Handshake_only_capability_is_still_created_before_bridge_start()
    {
        var events = new List<string>();
        var dispatchers = new NullCapabilityDispatcherFactory(events);
        var bridges = new RecordingBridgeFactory(events);
        var coordinator = new AddinLifecycleCoordinator(
            dispatchers,
            bridges,
            new FixedMetadataSource(),
            startupTimeout: TimeSpan.FromSeconds(2),
            shutdownTimeout: TimeSpan.FromSeconds(2));
        coordinator.Prepare(new RecordingBootstrapSubscription());

        coordinator.TryBootstrap(new LifecycleRevitRuntime("2026", "26.5.0.0"));

        Assert.Equal(AddinLifecycleState.Ready, coordinator.State);
        Assert.Null(bridges.LastCapability);
        Assert.Null(bridges.LastQuery);
        Assert.Equal(new[] { "dispatcher.create", "capability.create", "query.create", "bridge.start", "registration.publish" }, events);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "RevitMCP.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate RevitMCP.sln from the test output directory.");
    }
}

internal sealed class NullCapabilityDispatcherFactory : ILifecycleDispatcherFactory
{
    private readonly List<string> _events;

    public NullCapabilityDispatcherFactory(List<string> events)
    {
        _events = events;
    }

    public ILifecycleDispatcher Create()
    {
        _events.Add("dispatcher.create");
        return new NullCapabilityDispatcher(_events);
    }
}

internal sealed class NullCapabilityDispatcher : ILifecycleDispatcher
{
    private readonly List<string> _events;

    public NullCapabilityDispatcher(List<string> events)
    {
        _events = events;
    }

    public void Stop() => _events.Add("dispatcher.stop");

    public void Dispose() => _events.Add("dispatcher.dispose");

    public IRevitCapabilityService? CreateCapability(BridgeInstanceMetadata metadata)
    {
        _ = metadata;
        _events.Add("capability.create");
        return null;
    }

    public IRevitQueryElementsService? CreateQuery(BridgeInstanceMetadata metadata)
    {
        _ = metadata;
        _events.Add("query.create");
        return null;
    }
}
