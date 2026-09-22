using System.Diagnostics;
using RevitMCP.Contracts;
using Xunit;

namespace RevitMCP.Bridge.Tests;

public sealed class GetParameterValuesBridgeTests
{
    [Fact]
    public void Full_composition_advertises_protocol_v6()
    {
        var advertised = NamedPipeBridgeHost.WithAdvertisedProtocol(
            TestSupport.CreateMetadata(),
            new FakeCapabilityService(),
            new FakeQueryElementsService(),
            new FakeGetElementsService(),
            new FakeDescribeParametersService(),
            new FakeGetParameterValuesService());

        Assert.Equal(BridgeProtocol.GetParameterValuesVersions, advertised.SupportedProtocolVersions);
        Assert.Equal(6, advertised.SupportedProtocolVersions.Max());
        Assert.DoesNotContain(7, advertised.SupportedProtocolVersions);
    }

    [Fact]
    public void Describe_host_without_parameter_values_stays_on_protocol_v5()
    {
        var advertised = NamedPipeBridgeHost.WithAdvertisedProtocol(
            TestSupport.CreateMetadata(),
            new FakeCapabilityService(),
            new FakeQueryElementsService(),
            new FakeGetElementsService(),
            new FakeDescribeParametersService());

        Assert.Equal(BridgeProtocol.DescribeParametersVersions, advertised.SupportedProtocolVersions);
        Assert.DoesNotContain(6, advertised.SupportedProtocolVersions);
        Assert.Equal(5, advertised.SupportedProtocolVersions.Max());
    }

    [Fact]
    public void Incomplete_prefix_does_not_advertise_v6()
    {
        var valuesOnly = NamedPipeBridgeHost.WithAdvertisedProtocol(
            TestSupport.CreateMetadata(),
            capability: null,
            query: null,
            getElements: null,
            describeParameters: null,
            getParameterValues: new FakeGetParameterValuesService());
        var missingDescribe = NamedPipeBridgeHost.WithAdvertisedProtocol(
            TestSupport.CreateMetadata(),
            new FakeCapabilityService(),
            new FakeQueryElementsService(),
            new FakeGetElementsService(),
            describeParameters: null,
            getParameterValues: new FakeGetParameterValuesService());

        Assert.Equal(BridgeProtocol.HandshakeOnlyVersions, valuesOnly.SupportedProtocolVersions);
        Assert.Equal(BridgeProtocol.GetElementsVersions, missingDescribe.SupportedProtocolVersions);
        Assert.DoesNotContain(6, valuesOnly.SupportedProtocolVersions);
        Assert.DoesNotContain(6, missingDescribe.SupportedProtocolVersions);
    }

    [Fact]
    public async Task Registration_highest_protocol_is_6_only_when_fully_capable()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await using var full = await StartV6HostAsync();
        await using var describe = await StartHostAsync(
            new FakeCapabilityService(),
            new FakeQueryElementsService(),
            new FakeGetElementsService(),
            new FakeDescribeParametersService());

        Assert.Equal(6, full.Host.Registration?.BridgeProtocolVersion);
        Assert.Equal(5, describe.Host.Registration?.BridgeProtocolVersion);
    }

    [Fact]
    public async Task V6_handshake_selects_6_and_parameter_values_round_trip()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var values = new FakeGetParameterValuesService { Result = FakeGetParameterValuesService.CreateOkResult() };
        await using var context = await StartV6HostAsync(values);
        await using var client = await ConnectAndHandshakeAsync(context, BridgeProtocol.SupportedVersions);

        var result = await client.GetParameterValuesAsync(
            FakeGetParameterValuesService.CreateValidRequest(),
            TimeSpan.FromSeconds(3),
            CancellationToken.None);

        Assert.Equal(1, values.InvokeCount);
        var item = Assert.Single(result.Items);
        Assert.Equal(GetParameterValueStatus.Ok, item.Status);
        Assert.Equal(4, Assert.IsType<GetParameterIntegerValue>(item.Value).Value);
    }

    [Fact]
    public async Task Inherited_capabilities_still_execute_after_v6()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var capability = new FakeCapabilityService { Result = FakeCapabilityService.CreateZeroDocumentResult() };
        var query = new FakeQueryElementsService { Result = FakeQueryElementsService.CreateBoundedResult() };
        var getElements = new FakeGetElementsService { Result = FakeGetElementsService.CreateOkResult() };
        var describe = new FakeDescribeParametersService { Result = FakeDescribeParametersService.CreateOkResult() };
        await using var context = await StartV6HostAsync(
            new FakeGetParameterValuesService(),
            capability,
            query,
            getElements,
            describe);
        await using var client = await ConnectAndHandshakeAsync(context, BridgeProtocol.SupportedVersions);

        Assert.Null((await client.GetContextAsync(new GetContextRequest(), TimeSpan.FromSeconds(3), CancellationToken.None)).Document);
        Assert.Equal(2, (await client.QueryElementsAsync(FakeQueryElementsService.CreateValidRequest(), TimeSpan.FromSeconds(3), CancellationToken.None)).MatchedCount);
        Assert.Equal(GetElementResultStatus.Ok, Assert.Single((await client.GetElementsAsync(FakeGetElementsService.CreateValidRequest(), TimeSpan.FromSeconds(3), CancellationToken.None)).Elements).Status);
        Assert.Equal("opaque-parameter-ref", Assert.Single((await client.DescribeParametersAsync(FakeDescribeParametersService.CreateValidRequest(), TimeSpan.FromSeconds(3), CancellationToken.None)).Parameters).ParameterRef);
        Assert.Equal(1, capability.InvokeCount);
        Assert.Equal(1, query.InvokeCount);
        Assert.Equal(1, getElements.InvokeCount);
        Assert.Equal(1, describe.InvokeCount);
    }

    [Theory]
    [InlineData(new[] { 1 })]
    [InlineData(new[] { 2, 1 })]
    [InlineData(new[] { 3, 2, 1 })]
    [InlineData(new[] { 4, 3, 2, 1 })]
    [InlineData(new[] { 5, 4, 3, 2, 1 })]
    public async Task Get_parameter_values_is_rejected_after_v1_through_v5_without_invoking_the_service(int[] versions)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var values = new FakeGetParameterValuesService();
        await using var context = await StartV6HostAsync(values);
        await using var client = await ConnectAndHandshakeAsync(context, versions);

        var exception = await Assert.ThrowsAsync<BridgeException>(() =>
            client.GetParameterValuesAsync(
                FakeGetParameterValuesService.CreateValidRequest(),
                TimeSpan.FromSeconds(3),
                CancellationToken.None));

        Assert.Equal(BridgeErrorCodes.ProtocolIncompatible, exception.ErrorCode);
        Assert.Equal(0, values.InvokeCount);
    }

    [Fact]
    public async Task Get_parameter_values_is_rejected_before_handshake()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var values = new FakeGetParameterValuesService();
        await using var context = await StartV6HostAsync(values);
        await using var client = await NamedPipeBridgeClient.ConnectAsync(context.Host.PipeName, TimeSpan.FromSeconds(3), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<BridgeException>(() =>
            client.GetParameterValuesAsync(
                FakeGetParameterValuesService.CreateValidRequest(),
                TimeSpan.FromSeconds(3),
                CancellationToken.None));

        Assert.Equal(BridgeErrorCodes.HandshakeFailed, exception.ErrorCode);
        Assert.Equal(0, values.InvokeCount);
    }

    [Fact]
    public async Task Capability_error_survives_streamjsonrpc()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var values = new FakeGetParameterValuesService
        {
            Error = new BridgeException(
                CapabilityErrorCodes.InvalidParameterRead,
                "The parameter value request is invalid.")
        };
        await using var context = await StartV6HostAsync(values);
        await using var client = await ConnectAndHandshakeAsync(context, BridgeProtocol.SupportedVersions);

        var exception = await Assert.ThrowsAsync<BridgeException>(() =>
            client.GetParameterValuesAsync(
                FakeGetParameterValuesService.CreateValidRequest(),
                TimeSpan.FromSeconds(3),
                CancellationToken.None));

        Assert.Equal(CapabilityErrorCodes.InvalidParameterRead, exception.ErrorCode);
    }

    [Fact]
    public async Task Per_item_statuses_remain_successful_results()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var values = new FakeGetParameterValuesService
        {
            Result = FakeGetParameterValuesService.CreateItemStatusResult(GetParameterValueStatus.ParameterRefNotFound)
        };
        await using var context = await StartV6HostAsync(values);
        await using var client = await ConnectAndHandshakeAsync(context, BridgeProtocol.SupportedVersions);

        var result = await client.GetParameterValuesAsync(
            FakeGetParameterValuesService.CreateValidRequest(),
            TimeSpan.FromSeconds(3),
            CancellationToken.None);

        Assert.Equal(GetParameterValueStatus.ParameterRefNotFound, Assert.Single(result.Items).Status);
    }

    [Fact]
    public async Task Timeout_cancels_queued_work_and_marks_client_unusable()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var values = new FakeGetParameterValuesService
        {
            Hold = new TaskCompletionSource<GetParameterValuesResult>(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        await using var context = await StartV6HostAsync(values);
        await using var client = await ConnectAndHandshakeAsync(context, BridgeProtocol.SupportedVersions);

        var exception = await Assert.ThrowsAsync<BridgeException>(() =>
            client.GetParameterValuesAsync(
                FakeGetParameterValuesService.CreateValidRequest(),
                TimeSpan.FromMilliseconds(400),
                CancellationToken.None));

        Assert.Equal(CapabilityErrorCodes.ExecutionTimeout, exception.ErrorCode);
        await values.RequestTokenCancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));

        var unusable = await Assert.ThrowsAsync<BridgeException>(() =>
            client.GetParameterValuesAsync(
                FakeGetParameterValuesService.CreateValidRequest(),
                TimeSpan.FromSeconds(3),
                CancellationToken.None));
        Assert.Equal(CapabilityErrorCodes.ExecutionFailed, unusable.ErrorCode);
        values.Hold.TrySetCanceled();
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

    private static Task<HostContext> StartV6HostAsync(
        IRevitGetParameterValuesService? values = null,
        IRevitCapabilityService? capability = null,
        IRevitQueryElementsService? query = null,
        IRevitGetElementsService? getElements = null,
        IRevitDescribeParametersService? describe = null)
    {
        return StartHostAsync(
            capability ?? new FakeCapabilityService(),
            query ?? new FakeQueryElementsService(),
            getElements ?? new FakeGetElementsService(),
            describe ?? new FakeDescribeParametersService(),
            values ?? new FakeGetParameterValuesService());
    }

    private static async Task<HostContext> StartHostAsync(
        IRevitCapabilityService? capability = null,
        IRevitQueryElementsService? query = null,
        IRevitGetElementsService? getElements = null,
        IRevitDescribeParametersService? describe = null,
        IRevitGetParameterValuesService? values = null)
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
            values,
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
