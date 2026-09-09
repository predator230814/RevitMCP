using System.Text.Json;
using RevitMCP.Contracts;
using Xunit;

namespace RevitMCP.Bridge.Tests;

public sealed class RegistrationStoreTests
{
    [Fact]
    public void Registration_path_uses_session_and_instance_layout()
    {
        var root = Path.Combine(Path.GetTempPath(), "RevitMCP.Tests", Guid.NewGuid().ToString("N"));
        var store = new FileRegistrationStore(root);

        var path = store.GetRegistrationPath(2, "a81e3fd0-6d57-4d27-8dbc-b2f9e98221ac");

        Assert.Equal(
            Path.Combine(root, "2", "a81e3fd0-6d57-4d27-8dbc-b2f9e98221ac.json"),
            path);
        Assert.Equal(root, store.RootPath);
    }

    [Fact]
    public async Task Publish_writes_complete_json_and_cleans_up_on_dispose()
    {
        var root = Path.Combine(Path.GetTempPath(), "RevitMCP.Tests", Guid.NewGuid().ToString("N"));
        var store = new FileRegistrationStore(root);
        var metadata = TestSupport.CreateMetadata();
        var registration = TestSupport.CreateRegistration(metadata);
        var path = store.GetRegistrationPath(registration.WindowsSessionId, registration.InstanceId);

        await using (var lease = await store.PublishAsync(registration, CancellationToken.None))
        {
            Assert.True(File.Exists(path));
            var json = await File.ReadAllTextAsync(path);
            var restored = JsonSerializer.Deserialize<RevitInstanceRegistration>(json, ContractJson.Options);
            Assert.NotNull(restored);
            Assert.Equal(registration.InstanceId, restored.InstanceId);
            Assert.Equal(registration.PipeName, restored.PipeName);
            Assert.DoesNotContain(".tmp", json, StringComparison.Ordinal);
            Assert.Same(registration, lease.Registration);
        }

        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Malformed_registration_does_not_break_discovery_of_valid_records()
    {
        var root = Path.Combine(Path.GetTempPath(), "RevitMCP.Tests", Guid.NewGuid().ToString("N"));
        var store = new FileRegistrationStore(root);
        var metadata = TestSupport.CreateMetadata();
        var registration = TestSupport.CreateRegistration(metadata);

        await using var lease = await store.PublishAsync(registration, CancellationToken.None);
        var sessionDirectory = Path.Combine(root, metadata.WindowsSessionId.ToString());
        await File.WriteAllTextAsync(Path.Combine(sessionDirectory, "broken.json"), "{ not json");

        var processes = new FakeProcessInspector();
        processes.Add(metadata.ProcessId, metadata.ProcessStartTimeUtc);
        var factory = new FakeBridgeClientFactory
        {
            Connect = _ => Task.FromResult<IRevitBridgeClient>(new FakeBridgeClient((request, _) =>
            {
                var service = new BridgeHandshakeService(metadata);
                return service.HandshakeAsync(request, CancellationToken.None);
            }))
        };

        var discovery = new LocalInstanceDiscovery(store, processes, factory);
        var results = await discovery.DiscoverAsync(metadata.WindowsSessionId, CancellationToken.None);

        Assert.Single(results);
        Assert.Equal(DiscoveryState.Ready, results[0].State);
        Assert.Equal(registration.InstanceId, results[0].Registration?.InstanceId);
    }

    [Fact]
    public async Task Registration_with_mismatched_session_id_is_ignored()
    {
        var root = Path.Combine(Path.GetTempPath(), "RevitMCP.Tests", Guid.NewGuid().ToString("N"));
        var store = new FileRegistrationStore(root);
        var requestedSession = 7;
        var spoofed = TestSupport.CreateMetadata(sessionId: 99);
        var registration = TestSupport.CreateRegistration(spoofed);
        var sessionDirectory = Path.Combine(root, requestedSession.ToString());
        Directory.CreateDirectory(sessionDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(sessionDirectory, $"{registration.InstanceId}.json"),
            System.Text.Json.JsonSerializer.Serialize(registration, ContractJson.Options));

        var reads = await store.ReadSessionAsync(requestedSession, CancellationToken.None);
        Assert.All(reads, result => Assert.True(result.IsMalformed));

        var processes = new FakeProcessInspector();
        processes.Add(spoofed.ProcessId, spoofed.ProcessStartTimeUtc);
        var factory = new FakeBridgeClientFactory
        {
            Connect = _ => Task.FromResult<IRevitBridgeClient>(new FakeBridgeClient((request, _) =>
                new BridgeHandshakeService(spoofed).HandshakeAsync(request, CancellationToken.None)))
        };
        var discovery = new LocalInstanceDiscovery(store, processes, factory);
        var results = await discovery.DiscoverAsync(requestedSession, CancellationToken.None);

        Assert.Empty(results);
    }
}
