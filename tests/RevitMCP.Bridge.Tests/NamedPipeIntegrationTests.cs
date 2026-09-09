using System.Diagnostics;
using RevitMCP.Contracts;
using Xunit;

namespace RevitMCP.Bridge.Tests;

public sealed class NamedPipeIntegrationTests
{
    [Fact]
    public async Task Valid_registration_and_matching_named_pipe_becomes_ready()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await using var context = await StartHostAsync();
        var discovery = new LocalInstanceDiscovery(context.Store, context.Processes, new NamedPipeBridgeClientFactory());

        var results = await discovery.DiscoverAsync(context.Metadata.WindowsSessionId, CancellationToken.None);

        Assert.Single(results);
        Assert.Equal(DiscoveryState.Ready, results[0].State);
        Assert.Equal(context.Metadata.InstanceId, results[0].Handshake?.InstanceId);
        Assert.Equal(1, results[0].Handshake?.SelectedProtocolVersion);
    }

    [Fact]
    public async Task Two_clients_can_handshake_with_the_same_host()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await using var context = await StartHostAsync();
        var request = new BridgeHandshakeRequest
        {
            ExpectedInstanceId = context.Metadata.InstanceId,
            SupportedProtocolVersions = [1],
            ClientName = "RevitMCP.Tests"
        };

        await using var first = await NamedPipeBridgeClient.ConnectAsync(context.Host.PipeName, TimeSpan.FromSeconds(3), CancellationToken.None);
        await using var second = await NamedPipeBridgeClient.ConnectAsync(context.Host.PipeName, TimeSpan.FromSeconds(3), CancellationToken.None);

        var firstResult = await first.HandshakeAsync(request, CancellationToken.None);
        var secondResult = await second.HandshakeAsync(request, CancellationToken.None);

        Assert.Equal(context.Metadata.InstanceId, firstResult.InstanceId);
        Assert.Equal(context.Metadata.InstanceId, secondResult.InstanceId);
        Assert.Equal(1, firstResult.SelectedProtocolVersion);
        Assert.Equal(1, secondResult.SelectedProtocolVersion);
    }

    [Fact]
    public async Task Publish_failure_disposes_the_started_host()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var process = Process.GetCurrentProcess();
        var metadata = TestSupport.CreateMetadata(
            processId: process.Id,
            startTime: new DateTimeOffset(process.StartTime).ToUniversalTime(),
            sessionId: process.SessionId);
        var pipeName = BridgePipeNames.Create(metadata.WindowsSessionId, metadata.InstanceId);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NamedPipeBridgeHost.StartAsync(metadata, new ThrowingRegistrationStore(), CancellationToken.None));

        var exception = await Assert.ThrowsAsync<BridgeException>(() =>
            NamedPipeBridgeClient.ConnectAsync(pipeName, TimeSpan.FromMilliseconds(400), CancellationToken.None));

        Assert.Equal(BridgeErrorCodes.HandshakeTimeout, exception.ErrorCode);
    }

    [Fact]
    public async Task Mismatched_instance_id_over_named_pipe_is_rejected()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await using var context = await StartHostAsync();
        await using var client = await NamedPipeBridgeClient.ConnectAsync(context.Host.PipeName, TimeSpan.FromSeconds(3), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<BridgeException>(() => client.HandshakeAsync(
            new BridgeHandshakeRequest
            {
                ExpectedInstanceId = Guid.NewGuid().ToString("D"),
                SupportedProtocolVersions = [1]
            },
            CancellationToken.None));

        Assert.Equal(BridgeErrorCodes.IdentityMismatch, exception.ErrorCode);
        Assert.IsNotType<StreamJsonRpc.RemoteInvocationException>(exception);
    }

    private static async Task<HostContext> StartHostAsync()
    {
        var process = Process.GetCurrentProcess();
        var metadata = TestSupport.CreateMetadata(
            processId: process.Id,
            startTime: new DateTimeOffset(process.StartTime).ToUniversalTime(),
            sessionId: process.SessionId);
        var root = Path.Combine(Path.GetTempPath(), "RevitMCP.Tests", Guid.NewGuid().ToString("N"));
        var store = new FileRegistrationStore(root);
        var host = await NamedPipeBridgeHost.StartAsync(metadata, store, CancellationToken.None);
        var processes = new FakeProcessInspector();
        processes.Add(metadata.ProcessId, metadata.ProcessStartTimeUtc);
        return new HostContext(store, metadata, host, processes);
    }

    private sealed class HostContext : IAsyncDisposable
    {
        public HostContext(FileRegistrationStore store, BridgeInstanceMetadata metadata, NamedPipeBridgeHost host, FakeProcessInspector processes)
        {
            Store = store;
            Metadata = metadata;
            Host = host;
            Processes = processes;
        }

        public FileRegistrationStore Store { get; }

        public BridgeInstanceMetadata Metadata { get; }

        public NamedPipeBridgeHost Host { get; }

        public FakeProcessInspector Processes { get; }

        public async ValueTask DisposeAsync()
        {
            await Host.DisposeAsync();
        }
    }
}
