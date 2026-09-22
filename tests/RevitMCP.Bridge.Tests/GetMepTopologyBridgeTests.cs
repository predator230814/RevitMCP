using System.Diagnostics;
using RevitMCP.Contracts;
using Xunit;

namespace RevitMCP.Bridge.Tests;

public sealed class GetMepTopologyBridgeTests
{
    [Fact]
    public void Complete_v7_prefix_advertises_protocol_7_and_lower_prefixes_stay_unchanged()
    {
        var metadata = TestSupport.CreateMetadata();
        var full = NamedPipeBridgeHost.WithAdvertisedProtocol(
            metadata,
            new FakeCapabilityService(),
            new FakeQueryElementsService(),
            new FakeGetElementsService(),
            new FakeDescribeParametersService(),
            new FakeGetParameterValuesService(),
            new FakeGetMepTopologyService());
        var valuesOnly = NamedPipeBridgeHost.WithAdvertisedProtocol(
            metadata,
            new FakeCapabilityService(),
            new FakeQueryElementsService(),
            new FakeGetElementsService(),
            new FakeDescribeParametersService(),
            new FakeGetParameterValuesService());
        var topologyOnly = NamedPipeBridgeHost.WithAdvertisedProtocol(
            metadata,
            getMepTopology: new FakeGetMepTopologyService());
        var missingValues = NamedPipeBridgeHost.WithAdvertisedProtocol(
            metadata,
            new FakeCapabilityService(),
            new FakeQueryElementsService(),
            new FakeGetElementsService(),
            new FakeDescribeParametersService(),
            getParameterValues: null,
            getMepTopology: new FakeGetMepTopologyService());

        Assert.Equal(BridgeProtocol.SupportedVersions, full.SupportedProtocolVersions);
        Assert.Equal(7, full.SupportedProtocolVersions.Max());
        Assert.Equal(BridgeProtocol.GetParameterValuesVersions, valuesOnly.SupportedProtocolVersions);
        Assert.Equal(BridgeProtocol.HandshakeOnlyVersions, topologyOnly.SupportedProtocolVersions);
        Assert.Equal(BridgeProtocol.DescribeParametersVersions, missingValues.SupportedProtocolVersions);
        Assert.DoesNotContain(7, valuesOnly.SupportedProtocolVersions);
        Assert.DoesNotContain(7, topologyOnly.SupportedProtocolVersions);
        Assert.DoesNotContain(7, missingValues.SupportedProtocolVersions);
    }

    [Fact]
    public async Task Typed_client_invokes_topology_only_after_v7_and_preserves_timeout_behavior()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var topology = new FakeGetMepTopologyService();
        await using var context = await StartV7HostAsync(topology);
        await using var client = await ConnectAndHandshakeAsync(context, BridgeProtocol.SupportedVersions);

        var result = await client.GetMepTopologyAsync(FakeGetMepTopologyService.CreateRequest(), TimeSpan.FromSeconds(3), CancellationToken.None);
        Assert.Equal(MepTopologySeedStatus.Ok, Assert.Single(result.Seeds).Status);
        Assert.Equal(1, topology.InvokeCount);

        var values = await client.GetParameterValuesAsync(
            FakeGetParameterValuesService.CreateValidRequest(),
            TimeSpan.FromSeconds(3),
            CancellationToken.None);
        Assert.Equal(GetParameterValueStatus.Ok, Assert.Single(values.Items).Status);
    }

    [Theory]
    [InlineData(new[] { 1 })]
    [InlineData(new[] { 2, 1 })]
    [InlineData(new[] { 3, 2, 1 })]
    [InlineData(new[] { 4, 3, 2, 1 })]
    [InlineData(new[] { 5, 4, 3, 2, 1 })]
    [InlineData(new[] { 6, 5, 4, 3, 2, 1 })]
    public async Task Topology_is_rejected_on_v1_through_v6(int[] versions)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var topology = new FakeGetMepTopologyService();
        await using var context = await StartV7HostAsync(topology);
        await using var client = await ConnectAndHandshakeAsync(context, versions);

        var exception = await Assert.ThrowsAsync<BridgeException>(() =>
            client.GetMepTopologyAsync(FakeGetMepTopologyService.CreateRequest(), TimeSpan.FromSeconds(3), CancellationToken.None));

        Assert.Equal(BridgeErrorCodes.ProtocolIncompatible, exception.ErrorCode);
        Assert.Equal(0, topology.InvokeCount);
    }

    [Fact]
    public async Task Unknown_v8_cannot_negotiate_and_does_not_invoke_topology()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var topology = new FakeGetMepTopologyService();
        await using var context = await StartV7HostAsync(topology);
        await using var client = await NamedPipeBridgeClient.ConnectAsync(context.Host.PipeName, TimeSpan.FromSeconds(3), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<BridgeException>(() =>
            client.HandshakeAsync(
                new BridgeHandshakeRequest
                {
                    ExpectedInstanceId = context.Metadata.InstanceId,
                    SupportedProtocolVersions = [8],
                    ClientName = "RevitMCP.Tests"
                },
                CancellationToken.None));

        Assert.Equal(BridgeErrorCodes.ProtocolIncompatible, exception.ErrorCode);
        Assert.Equal(0, topology.InvokeCount);
    }

    [Fact]
    public async Task Timeout_cancels_the_topology_request_and_does_not_retry()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var topology = new FakeGetMepTopologyService
        {
            Hold = new TaskCompletionSource<GetMepTopologyResult>(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        await using var context = await StartV7HostAsync(topology);
        await using var client = await ConnectAndHandshakeAsync(context, [7, 6, 5, 4, 3, 2, 1]);

        var exception = await Assert.ThrowsAsync<BridgeException>(() =>
            client.GetMepTopologyAsync(
                FakeGetMepTopologyService.CreateRequest(),
                TimeSpan.FromMilliseconds(400),
                CancellationToken.None));

        Assert.Equal(CapabilityErrorCodes.ExecutionTimeout, exception.ErrorCode);
        await topology.RequestTokenCancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var unusable = await Assert.ThrowsAsync<BridgeException>(() =>
            client.GetMepTopologyAsync(FakeGetMepTopologyService.CreateRequest(), TimeSpan.FromSeconds(3), CancellationToken.None));
        Assert.Equal(CapabilityErrorCodes.ExecutionFailed, unusable.ErrorCode);
        Assert.Equal(1, topology.InvokeCount);
        topology.Hold.TrySetCanceled();
    }

    private static async Task<NamedPipeBridgeClient> ConnectAndHandshakeAsync(HostContext context, IReadOnlyList<int> versions)
    {
        var client = await NamedPipeBridgeClient.ConnectAsync(context.Host.PipeName, TimeSpan.FromSeconds(3), CancellationToken.None);
        await client.HandshakeAsync(
            new BridgeHandshakeRequest
            {
                ExpectedInstanceId = context.Metadata.InstanceId,
                SupportedProtocolVersions = versions,
                ClientName = "RevitMCP.Tests"
            },
            CancellationToken.None);
        return client;
    }

    private static async Task<HostContext> StartV7HostAsync(IRevitGetMepTopologyService topology)
    {
        var process = Process.GetCurrentProcess();
        var metadata = TestSupport.CreateMetadata(
            processId: process.Id,
            startTime: new DateTimeOffset(process.StartTime).ToUniversalTime(),
            sessionId: process.SessionId);
        var root = Path.Combine(Path.GetTempPath(), "RevitMCP.Tests", Guid.NewGuid().ToString("N"));
        var host = await NamedPipeBridgeHost.StartAsync(
            metadata,
            new FileRegistrationStore(root),
            new FakeCapabilityService(),
            new FakeQueryElementsService(),
            new FakeGetElementsService(),
            new FakeDescribeParametersService(),
            new FakeGetParameterValuesService(),
            topology,
            CancellationToken.None);
        return new HostContext(host, metadata);
    }

    private sealed class HostContext(NamedPipeBridgeHost host, BridgeInstanceMetadata metadata) : IAsyncDisposable
    {
        public NamedPipeBridgeHost Host { get; } = host;

        public BridgeInstanceMetadata Metadata { get; } = metadata;

        public ValueTask DisposeAsync() => Host.DisposeAsync();
    }
}
