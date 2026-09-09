using RevitMCP.Contracts;
using Xunit;

namespace RevitMCP.Bridge.Tests;

public sealed class DiscoveryTests
{
    [Fact]
    public async Task Missing_process_is_stale_and_registration_is_removed()
    {
        var (store, metadata, path) = await PublishAsync();
        var discovery = new LocalInstanceDiscovery(store, new FakeProcessInspector(), new FakeBridgeClientFactory());

        var results = await discovery.DiscoverAsync(metadata.WindowsSessionId, CancellationToken.None);

        Assert.Single(results);
        Assert.Equal(DiscoveryState.Stale, results[0].State);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Process_start_time_mismatch_is_stale()
    {
        var (store, metadata, path) = await PublishAsync();
        var processes = new FakeProcessInspector();
        processes.Add(metadata.ProcessId, metadata.ProcessStartTimeUtc.AddMinutes(5));
        var discovery = new LocalInstanceDiscovery(store, processes, new FakeBridgeClientFactory());

        var results = await discovery.DiscoverAsync(metadata.WindowsSessionId, CancellationToken.None);

        Assert.Equal(DiscoveryState.Stale, results[0].State);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Live_process_with_unreachable_pipe_is_unavailable_and_keeps_registration()
    {
        var (store, metadata, path) = await PublishAsync();
        var processes = new FakeProcessInspector();
        processes.Add(metadata.ProcessId, metadata.ProcessStartTimeUtc);
        var factory = new FakeBridgeClientFactory
        {
            Connect = _ => throw new BridgeException(BridgeErrorCodes.HandshakeTimeout, "timed out")
        };
        var discovery = new LocalInstanceDiscovery(store, processes, factory);

        var results = await discovery.DiscoverAsync(metadata.WindowsSessionId, CancellationToken.None);

        Assert.Equal(DiscoveryState.Unavailable, results[0].State);
        Assert.Equal(BridgeErrorCodes.HandshakeTimeout, results[0].ErrorCode);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task Matching_process_and_handshake_is_ready()
    {
        var (store, metadata, _) = await PublishAsync();
        var processes = new FakeProcessInspector();
        processes.Add(metadata.ProcessId, metadata.ProcessStartTimeUtc);
        var factory = new FakeBridgeClientFactory
        {
            Connect = _ => Task.FromResult<IRevitBridgeClient>(new FakeBridgeClient((request, _) =>
                new BridgeHandshakeService(metadata).HandshakeAsync(request, CancellationToken.None)))
        };
        var discovery = new LocalInstanceDiscovery(store, processes, factory);

        var results = await discovery.DiscoverAsync(metadata.WindowsSessionId, CancellationToken.None);

        Assert.Equal(DiscoveryState.Ready, results[0].State);
        Assert.Equal(metadata.InstanceId, results[0].Handshake?.InstanceId);
        Assert.Equal(1, results[0].Handshake?.SelectedProtocolVersion);
    }

    [Fact]
    public async Task Handshake_process_identity_mismatch_is_rejected()
    {
        var (store, metadata, _) = await PublishAsync();
        var processes = new FakeProcessInspector();
        processes.Add(metadata.ProcessId, metadata.ProcessStartTimeUtc);
        var other = TestSupport.CreateMetadata(instanceId: metadata.InstanceId, processId: metadata.ProcessId + 1, startTime: metadata.ProcessStartTimeUtc, sessionId: metadata.WindowsSessionId);
        var factory = new FakeBridgeClientFactory
        {
            Connect = _ => Task.FromResult<IRevitBridgeClient>(new FakeBridgeClient((_, _) =>
                Task.FromResult(new BridgeHandshakeResult
                {
                    InstanceId = metadata.InstanceId,
                    ProcessId = other.ProcessId,
                    ProcessStartTimeUtc = metadata.ProcessStartTimeUtc,
                    WindowsSessionId = metadata.WindowsSessionId,
                    RevitVersion = metadata.RevitVersion,
                    RevitBuild = metadata.RevitBuild,
                    AddinVersion = metadata.AddinVersion,
                    SupportedProtocolVersions = metadata.SupportedProtocolVersions,
                    SelectedProtocolVersion = 1
                })))
        };
        var discovery = new LocalInstanceDiscovery(store, processes, factory);

        var results = await discovery.DiscoverAsync(metadata.WindowsSessionId, CancellationToken.None);

        Assert.Equal(DiscoveryState.Unavailable, results[0].State);
        Assert.Equal(BridgeErrorCodes.IdentityMismatch, results[0].ErrorCode);
    }

    [Fact]
    public async Task Incompatible_protocol_is_reported()
    {
        var (store, metadata, _) = await PublishAsync();
        var processes = new FakeProcessInspector();
        processes.Add(metadata.ProcessId, metadata.ProcessStartTimeUtc);
        var factory = new FakeBridgeClientFactory
        {
            Connect = _ => Task.FromResult<IRevitBridgeClient>(new FakeBridgeClient((_, _) =>
                throw new BridgeException(BridgeErrorCodes.ProtocolIncompatible, "no common version")))
        };
        var discovery = new LocalInstanceDiscovery(store, processes, factory);

        var results = await discovery.DiscoverAsync(metadata.WindowsSessionId, CancellationToken.None);

        Assert.Equal(DiscoveryState.Incompatible, results[0].State);
        Assert.Equal(BridgeErrorCodes.ProtocolIncompatible, results[0].ErrorCode);
    }

    private static async Task<(FileRegistrationStore Store, BridgeInstanceMetadata Metadata, string Path)> PublishAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "RevitMCP.Tests", Guid.NewGuid().ToString("N"));
        var store = new FileRegistrationStore(root);
        var metadata = TestSupport.CreateMetadata();
        var registration = TestSupport.CreateRegistration(metadata);
        await store.PublishAsync(registration, CancellationToken.None);
        return (store, metadata, store.GetRegistrationPath(metadata.WindowsSessionId, metadata.InstanceId));
    }
}
