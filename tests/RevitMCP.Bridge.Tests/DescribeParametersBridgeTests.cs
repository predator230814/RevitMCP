using System.Diagnostics;
using RevitMCP.Contracts;
using Xunit;

namespace RevitMCP.Bridge.Tests;

public sealed class DescribeParametersBridgeTests
{
    [Fact]
    public void Full_composition_advertises_protocol_v5()
    {
        var advertised = NamedPipeBridgeHost.WithAdvertisedProtocol(
            TestSupport.CreateMetadata(),
            new FakeCapabilityService(),
            new FakeQueryElementsService(),
            new FakeGetElementsService(),
            new FakeDescribeParametersService());

        Assert.Equal(BridgeProtocol.SupportedVersions, advertised.SupportedProtocolVersions);
        Assert.Equal(5, advertised.SupportedProtocolVersions.Max());
    }

    [Fact]
    public void Get_elements_host_without_describe_stays_on_protocol_v4()
    {
        var advertised = NamedPipeBridgeHost.WithAdvertisedProtocol(
            TestSupport.CreateMetadata(),
            new FakeCapabilityService(),
            new FakeQueryElementsService(),
            new FakeGetElementsService());

        Assert.Equal(BridgeProtocol.GetElementsVersions, advertised.SupportedProtocolVersions);
        Assert.DoesNotContain(5, advertised.SupportedProtocolVersions);
    }

    [Fact]
    public void Describe_without_inherited_prefix_does_not_advertise_v5()
    {
        var advertised = NamedPipeBridgeHost.WithAdvertisedProtocol(
            TestSupport.CreateMetadata(),
            capability: null,
            query: null,
            getElements: null,
            describeParameters: new FakeDescribeParametersService());

        Assert.Equal(BridgeProtocol.HandshakeOnlyVersions, advertised.SupportedProtocolVersions);
    }

    [Fact]
    public async Task Registration_highest_protocol_is_5_only_when_fully_capable()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await using var full = await StartV5HostAsync();
        await using var getElements = await StartHostAsync(
            new FakeCapabilityService(),
            new FakeQueryElementsService(),
            new FakeGetElementsService());

        Assert.Equal(5, full.Host.Registration?.BridgeProtocolVersion);
        Assert.Equal(4, getElements.Host.Registration?.BridgeProtocolVersion);
    }

    [Fact]
    public async Task V5_handshake_selects_5_and_describe_round_trips()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var describe = new FakeDescribeParametersService { Result = FakeDescribeParametersService.CreateOkResult() };
        await using var context = await StartV5HostAsync(describe);
        await using var client = await ConnectAndHandshakeAsync(context, BridgeProtocol.SupportedVersions);

        var result = await client.DescribeParametersAsync(
            FakeDescribeParametersService.CreateValidRequest(),
            TimeSpan.FromSeconds(3),
            CancellationToken.None);

        Assert.Equal(1, describe.InvokeCount);
        Assert.Equal("opaque-parameter-ref", Assert.Single(result.Parameters).ParameterRef);
        Assert.Equal(GetElementResultStatus.Ok, Assert.Single(result.Elements).Status);
    }

    [Fact]
    public async Task Inherited_capabilities_still_execute_after_v5()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var capability = new FakeCapabilityService { Result = FakeCapabilityService.CreateZeroDocumentResult() };
        var query = new FakeQueryElementsService { Result = FakeQueryElementsService.CreateBoundedResult() };
        var getElements = new FakeGetElementsService { Result = FakeGetElementsService.CreateOkResult() };
        await using var context = await StartV5HostAsync(
            new FakeDescribeParametersService(),
            capability,
            query,
            getElements);
        await using var client = await ConnectAndHandshakeAsync(context, BridgeProtocol.SupportedVersions);

        Assert.Null((await client.GetContextAsync(new GetContextRequest(), TimeSpan.FromSeconds(3), CancellationToken.None)).Document);
        Assert.Equal(2, (await client.QueryElementsAsync(FakeQueryElementsService.CreateValidRequest(), TimeSpan.FromSeconds(3), CancellationToken.None)).MatchedCount);
        Assert.Equal(GetElementResultStatus.Ok, Assert.Single((await client.GetElementsAsync(FakeGetElementsService.CreateValidRequest(), TimeSpan.FromSeconds(3), CancellationToken.None)).Elements).Status);
        Assert.Equal(1, capability.InvokeCount);
        Assert.Equal(1, query.InvokeCount);
        Assert.Equal(1, getElements.InvokeCount);
    }

    [Fact]
    public async Task Describe_is_rejected_after_v4_without_invoking_the_service()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var describe = new FakeDescribeParametersService();
        await using var context = await StartV5HostAsync(describe);
        await using var client = await ConnectAndHandshakeAsync(context, [4, 3, 2, 1]);

        var exception = await Assert.ThrowsAsync<BridgeException>(() =>
            client.DescribeParametersAsync(
                FakeDescribeParametersService.CreateValidRequest(),
                TimeSpan.FromSeconds(3),
                CancellationToken.None));

        Assert.Equal(BridgeErrorCodes.ProtocolIncompatible, exception.ErrorCode);
        Assert.Equal(0, describe.InvokeCount);
    }

    [Fact]
    public async Task Capability_error_survives_streamjsonrpc()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var describe = new FakeDescribeParametersService
        {
            Error = new BridgeException(
                CapabilityErrorCodes.InvalidParameterDiscovery,
                "The parameter discovery request is invalid.")
        };
        await using var context = await StartV5HostAsync(describe);
        await using var client = await ConnectAndHandshakeAsync(context, BridgeProtocol.SupportedVersions);

        var exception = await Assert.ThrowsAsync<BridgeException>(() =>
            client.DescribeParametersAsync(
                FakeDescribeParametersService.CreateValidRequest(),
                TimeSpan.FromSeconds(3),
                CancellationToken.None));

        Assert.Equal(CapabilityErrorCodes.InvalidParameterDiscovery, exception.ErrorCode);
    }

    [Fact]
    public async Task Timeout_cancels_queued_work_and_marks_client_unusable()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var describe = new FakeDescribeParametersService
        {
            Hold = new TaskCompletionSource<DescribeParametersResult>(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        await using var context = await StartV5HostAsync(describe);
        await using var client = await ConnectAndHandshakeAsync(context, BridgeProtocol.SupportedVersions);

        var exception = await Assert.ThrowsAsync<BridgeException>(() =>
            client.DescribeParametersAsync(
                FakeDescribeParametersService.CreateValidRequest(),
                TimeSpan.FromMilliseconds(400),
                CancellationToken.None));

        Assert.Equal(CapabilityErrorCodes.ExecutionTimeout, exception.ErrorCode);
        await describe.RequestTokenCancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));

        var unusable = await Assert.ThrowsAsync<BridgeException>(() =>
            client.DescribeParametersAsync(
                FakeDescribeParametersService.CreateValidRequest(),
                TimeSpan.FromSeconds(3),
                CancellationToken.None));
        Assert.Equal(CapabilityErrorCodes.ExecutionFailed, unusable.ErrorCode);
        describe.Hold.TrySetCanceled();
    }

    private static BridgeHandshakeRequest CreateHandshakeRequest(string instanceId, IReadOnlyList<int> versions)
    {
        return new BridgeHandshakeRequest
        {
            ExpectedInstanceId = instanceId,
            SupportedProtocolVersions = versions,
            ClientName = "RevitMCP.Tests"
        };
    }

    private static async Task<NamedPipeBridgeClient> ConnectAndHandshakeAsync(HostContext context, IReadOnlyList<int> versions)
    {
        var client = await NamedPipeBridgeClient.ConnectAsync(context.Host.PipeName, TimeSpan.FromSeconds(3), CancellationToken.None);
        await client.HandshakeAsync(CreateHandshakeRequest(context.Metadata.InstanceId, versions), CancellationToken.None);
        return client;
    }

    private static Task<HostContext> StartV5HostAsync(
        IRevitDescribeParametersService? describe = null,
        IRevitCapabilityService? capability = null,
        IRevitQueryElementsService? query = null,
        IRevitGetElementsService? getElements = null)
    {
        return StartHostAsync(
            capability ?? new FakeCapabilityService(),
            query ?? new FakeQueryElementsService(),
            getElements ?? new FakeGetElementsService(),
            describe ?? new FakeDescribeParametersService());
    }

    private static async Task<HostContext> StartHostAsync(
        IRevitCapabilityService? capability = null,
        IRevitQueryElementsService? query = null,
        IRevitGetElementsService? getElements = null,
        IRevitDescribeParametersService? describe = null)
    {
        var metadata = CreateLiveMetadata();
        var root = Path.Combine(Path.GetTempPath(), "RevitMCP.Tests", Guid.NewGuid().ToString("N"));
        var host = await NamedPipeBridgeHost.StartAsync(
            metadata,
            new FileRegistrationStore(root),
            capability,
            query,
            getElements,
            describe,
            CancellationToken.None);
        return new HostContext(host, metadata);
    }

    private static BridgeInstanceMetadata CreateLiveMetadata()
    {
        var process = Process.GetCurrentProcess();
        return TestSupport.CreateMetadata(
            processId: process.Id,
            startTime: new DateTimeOffset(process.StartTime).ToUniversalTime(),
            sessionId: process.SessionId);
    }

    private sealed class HostContext : IAsyncDisposable
    {
        public HostContext(NamedPipeBridgeHost host, BridgeInstanceMetadata metadata)
        {
            Host = host;
            Metadata = metadata;
        }

        public NamedPipeBridgeHost Host { get; }

        public BridgeInstanceMetadata Metadata { get; }

        public ValueTask DisposeAsync() => Host.DisposeAsync();
    }
}
