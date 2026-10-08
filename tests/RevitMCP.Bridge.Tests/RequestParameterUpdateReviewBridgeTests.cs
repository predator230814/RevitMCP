using System.Diagnostics;
using System.IO.Pipes;
using RevitMCP.Bridge.Implementation;
using RevitMCP.Contracts;
using StreamJsonRpc;
using Xunit;

namespace RevitMCP.Bridge.Tests;

public sealed class RequestParameterUpdateReviewBridgeTests
{
    [Fact]
    public void V9_host_advertises_v9_and_preview_without_review_advertises_v8()
    {
        var metadata = TestSupport.CreateMetadata();
        var services = CreateFullServices();
        var v9 = NamedPipeBridgeHost.WithAdvertisedProtocol(
            metadata,
            services.Capability,
            services.Query,
            services.Elements,
            services.Describe,
            services.Values,
            services.Topology,
            services.Preview,
            services.Review);
        var v8 = NamedPipeBridgeHost.WithAdvertisedProtocol(
            metadata,
            services.Capability,
            services.Query,
            services.Elements,
            services.Describe,
            services.Values,
            services.Topology,
            services.Preview);

        Assert.Equal(BridgeProtocol.RequestParameterUpdateReviewVersions, v9.SupportedProtocolVersions);
        Assert.Equal(9, v9.SupportedProtocolVersions.Max());
        Assert.Equal(BridgeProtocol.PreviewParameterUpdatesVersions, v8.SupportedProtocolVersions);
        Assert.Equal(8, v8.SupportedProtocolVersions.Max());
        Assert.DoesNotContain(9, v8.SupportedProtocolVersions);
    }

    [Fact]
    public async Task Request_review_is_rejected_before_handshake()
    {
        var review = new FakeRequestParameterUpdateReviewService();
        var adapter = CreateAdapter(review);
        var before = await Assert.ThrowsAsync<LocalRpcException>(() =>
            adapter.RequestParameterUpdateReviewAsync(ValidRequest(), CancellationToken.None));
        Assert.Equal(BridgeErrorCodes.HandshakeFailed, ErrorCode(before));
        Assert.Equal(0, review.InvokeCount);
    }

    [Theory]
    [InlineData(new[] { 1 })]
    [InlineData(new[] { 8, 7, 6, 5, 4, 3, 2, 1 })]
    public async Task Request_review_is_rejected_on_negotiated_v1_through_v8(int[] versions)
    {
        var review = new FakeRequestParameterUpdateReviewService();
        var adapter = CreateAdapter(review);
        await adapter.HandshakeAsync(Handshake("instance", versions), CancellationToken.None);
        var rejected = await Assert.ThrowsAsync<LocalRpcException>(() =>
            adapter.RequestParameterUpdateReviewAsync(ValidRequest(), CancellationToken.None));
        Assert.Equal(BridgeErrorCodes.ProtocolIncompatible, ErrorCode(rejected));
        Assert.Equal(0, review.InvokeCount);
    }

    [Fact]
    public async Task Request_review_is_allowed_on_v9_and_statuses_round_trip()
    {
        var review = new FakeRequestParameterUpdateReviewService();
        var adapter = CreateAdapter(review);
        await adapter.HandshakeAsync(Handshake("instance", BridgeProtocol.SupportedVersions), CancellationToken.None);

        foreach (var status in Enum.GetValues<RequestParameterUpdateReviewStatus>())
        {
            review.Result = new RequestParameterUpdateReviewResult { Status = status };
            var result = await adapter.RequestParameterUpdateReviewAsync(ValidRequest(), CancellationToken.None);
            Assert.Equal(status, result.Status);
        }

        Assert.Equal(Enum.GetValues<RequestParameterUpdateReviewStatus>().Length, review.InvokeCount);
    }

    [Fact]
    public async Task Missing_request_review_service_and_unknown_v10_are_rejected()
    {
        var review = new FakeRequestParameterUpdateReviewService();
        var missing = new StreamJsonRpcBridgeAdapter(
            new SelectedHandshake(9),
            new FakeCapabilityService(),
            new FakeQueryElementsService(),
            new FakeGetElementsService(),
            new FakeDescribeParametersService(),
            new FakeGetParameterValuesService(),
            new FakeGetMepTopologyService(),
            new FakePreviewParameterUpdatesService());
        await missing.HandshakeAsync(Handshake("any", [9]), CancellationToken.None);
        var incompatible = await Assert.ThrowsAsync<LocalRpcException>(() =>
            missing.RequestParameterUpdateReviewAsync(ValidRequest(), CancellationToken.None));
        Assert.Equal(BridgeErrorCodes.ProtocolIncompatible, ErrorCode(incompatible));

        var unknown = CreateAdapter(review, selectedVersion: 11);
        await unknown.HandshakeAsync(Handshake("any", [11]), CancellationToken.None);
        var future = await Assert.ThrowsAsync<LocalRpcException>(() =>
            unknown.RequestParameterUpdateReviewAsync(ValidRequest(), CancellationToken.None));
        Assert.Equal(BridgeErrorCodes.ProtocolIncompatible, ErrorCode(future));
        Assert.Equal(0, review.InvokeCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("bad ref")]
    public async Task Malformed_direct_request_is_invalid_and_does_not_invoke_the_service(string intentRef)
    {
        var review = new FakeRequestParameterUpdateReviewService();
        var adapter = CreateAdapter(review);
        await adapter.HandshakeAsync(Handshake("instance", BridgeProtocol.SupportedVersions), CancellationToken.None);

        var rejected = await Assert.ThrowsAsync<LocalRpcException>(() =>
            adapter.RequestParameterUpdateReviewAsync(
                new RequestParameterUpdateReviewRequest { IntentRef = intentRef },
                CancellationToken.None));

        Assert.Equal(CapabilityErrorCodes.InvalidApprovalReviewRequest, ErrorCode(rejected));
        Assert.Equal(0, review.InvokeCount);
    }

    [Fact]
    public async Task Oversized_direct_request_is_invalid()
    {
        var review = new FakeRequestParameterUpdateReviewService();
        var adapter = CreateAdapter(review);
        await adapter.HandshakeAsync(Handshake("instance", BridgeProtocol.SupportedVersions), CancellationToken.None);

        var rejected = await Assert.ThrowsAsync<LocalRpcException>(() =>
            adapter.RequestParameterUpdateReviewAsync(
                new RequestParameterUpdateReviewRequest { IntentRef = new string('a', 129) },
                CancellationToken.None));

        Assert.Equal(CapabilityErrorCodes.InvalidApprovalReviewRequest, ErrorCode(rejected));
        Assert.Equal(0, review.InvokeCount);
    }

    [Fact]
    public async Task Older_capabilities_remain_usable_on_negotiated_v9()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var review = new FakeRequestParameterUpdateReviewService();
        var capability = new FakeCapabilityService { Result = FakeCapabilityService.CreateZeroDocumentResult() };
        var query = new FakeQueryElementsService { Result = FakeQueryElementsService.CreateBoundedResult() };
        var elements = new FakeGetElementsService { Result = FakeGetElementsService.CreateOkResult() };
        var describe = new FakeDescribeParametersService { Result = FakeDescribeParametersService.CreateOkResult() };
        var values = new FakeGetParameterValuesService { Result = FakeGetParameterValuesService.CreateOkResult() };
        var topology = new FakeGetMepTopologyService { Result = FakeGetMepTopologyService.CreateResult() };
        var preview = new FakePreviewParameterUpdatesService();
        await using var context = await StartHostAsync(review, capability, query, elements, describe, values, topology, preview);
        await using var client = await NamedPipeBridgeClient.ConnectAsync(context.Host.PipeName, TimeSpan.FromSeconds(3), CancellationToken.None);
        var handshake = await client.HandshakeAsync(Handshake(context.Metadata.InstanceId, BridgeProtocol.SupportedVersions), CancellationToken.None);

        Assert.Equal(9, handshake.SelectedProtocolVersion);
        Assert.Null((await client.GetContextAsync(new GetContextRequest(), TimeSpan.FromSeconds(3), CancellationToken.None)).Document);
        Assert.Equal(2, (await client.QueryElementsAsync(FakeQueryElementsService.CreateValidRequest(), TimeSpan.FromSeconds(3), CancellationToken.None)).MatchedCount);
        Assert.Equal(GetElementResultStatus.Ok, Assert.Single((await client.GetElementsAsync(FakeGetElementsService.CreateValidRequest(), TimeSpan.FromSeconds(3), CancellationToken.None)).Elements).Status);
        Assert.Equal("opaque-parameter-ref", Assert.Single((await client.DescribeParametersAsync(FakeDescribeParametersService.CreateValidRequest(), TimeSpan.FromSeconds(3), CancellationToken.None)).Parameters).ParameterRef);
        Assert.Equal(GetParameterValueStatus.Ok, Assert.Single((await client.GetParameterValuesAsync(FakeGetParameterValuesService.CreateValidRequest(), TimeSpan.FromSeconds(3), CancellationToken.None)).Items).Status);
        Assert.Equal(MepTopologySeedStatus.Ok, Assert.Single((await client.GetMepTopologyAsync(FakeGetMepTopologyService.CreateRequest(), TimeSpan.FromSeconds(3), CancellationToken.None)).Seeds).Status);
        Assert.True((await client.PreviewParameterUpdatesAsync(FakePreviewParameterUpdatesService.CreateRequest(), TimeSpan.FromSeconds(3), CancellationToken.None)).Ready);
        var presented = await client.RequestParameterUpdateReviewAsync(ValidRequest(), TimeSpan.FromSeconds(3), CancellationToken.None);
        Assert.Equal(RequestParameterUpdateReviewStatus.Started, presented.Status);
        Assert.Equal(1, review.InvokeCount);
    }

    [Fact]
    public async Task Timeout_cancels_the_review_request_and_does_not_retry()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var review = new FakeRequestParameterUpdateReviewService
        {
            Hold = new TaskCompletionSource<RequestParameterUpdateReviewResult>(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        await using var context = await StartHostAsync(review);
        await using var client = await NamedPipeBridgeClient.ConnectAsync(context.Host.PipeName, TimeSpan.FromSeconds(3), CancellationToken.None);
        await client.HandshakeAsync(Handshake(context.Metadata.InstanceId, BridgeProtocol.SupportedVersions), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<BridgeException>(() =>
            client.RequestParameterUpdateReviewAsync(ValidRequest(), TimeSpan.FromMilliseconds(400), CancellationToken.None));

        Assert.Equal(CapabilityErrorCodes.ExecutionTimeout, exception.ErrorCode);
        await review.RequestTokenCancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var unusable = await Assert.ThrowsAsync<BridgeException>(() =>
            client.RequestParameterUpdateReviewAsync(ValidRequest(), TimeSpan.FromSeconds(3), CancellationToken.None));
        Assert.Equal(CapabilityErrorCodes.ExecutionFailed, unusable.ErrorCode);
        Assert.Equal(1, review.InvokeCount);
        review.Hold.TrySetCanceled();
    }

    [Fact]
    public async Task Raw_peer_cannot_bypass_the_review_gate()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var review = new FakeRequestParameterUpdateReviewService();
        await using var context = await StartHostAsync(review);
        await using var raw = await RawRpc.ConnectAsync(context.Host.PipeName);

        var before = await Assert.ThrowsAsync<RemoteInvocationException>(() =>
            raw.InvokeAsync<RequestParameterUpdateReviewResult>("revit.request_parameter_update_review", ValidRequest()));
        Assert.Equal(BridgeErrorCodes.HandshakeFailed, StreamJsonRpcExceptionMapper.FromRemote(before).ErrorCode);

        await raw.InvokeAsync<BridgeHandshakeResult>(
            "bridge.handshake",
            Handshake(context.Metadata.InstanceId, BridgeProtocol.PreviewParameterUpdatesVersions));
        var v8 = await Assert.ThrowsAsync<RemoteInvocationException>(() =>
            raw.InvokeAsync<RequestParameterUpdateReviewResult>("revit.request_parameter_update_review", ValidRequest()));
        Assert.Equal(BridgeErrorCodes.ProtocolIncompatible, StreamJsonRpcExceptionMapper.FromRemote(v8).ErrorCode);
        Assert.Equal(0, review.InvokeCount);
    }

    private static FullServices CreateFullServices()
    {
        return new FullServices(
            new FakeCapabilityService(),
            new FakeQueryElementsService(),
            new FakeGetElementsService(),
            new FakeDescribeParametersService(),
            new FakeGetParameterValuesService(),
            new FakeGetMepTopologyService(),
            new FakePreviewParameterUpdatesService(),
            new FakeRequestParameterUpdateReviewService());
    }

    private static StreamJsonRpcBridgeAdapter CreateAdapter(
        IRevitRequestParameterUpdateReviewService review,
        int? selectedVersion = null)
    {
        IRevitBridgeService handshake = selectedVersion is int version
            ? new SelectedHandshake(version)
            : new BridgeHandshakeService(TestSupport.CreateMetadata(instanceId: "instance"));
        return new StreamJsonRpcBridgeAdapter(
            handshake,
            new FakeCapabilityService(),
            new FakeQueryElementsService(),
            new FakeGetElementsService(),
            new FakeDescribeParametersService(),
            new FakeGetParameterValuesService(),
            new FakeGetMepTopologyService(),
            new FakePreviewParameterUpdatesService(),
            review);
    }

    private static async Task<HostContext> StartHostAsync(
        IRevitRequestParameterUpdateReviewService review,
        IRevitCapabilityService? capability = null,
        IRevitQueryElementsService? query = null,
        IRevitGetElementsService? elements = null,
        IRevitDescribeParametersService? describe = null,
        IRevitGetParameterValuesService? values = null,
        IRevitGetMepTopologyService? topology = null,
        IRevitPreviewParameterUpdatesService? preview = null)
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
            capability ?? new FakeCapabilityService(),
            query ?? new FakeQueryElementsService(),
            elements ?? new FakeGetElementsService(),
            describe ?? new FakeDescribeParametersService(),
            values ?? new FakeGetParameterValuesService(),
            topology ?? new FakeGetMepTopologyService(),
            preview ?? new FakePreviewParameterUpdatesService(),
            review,
            CancellationToken.None);
        return new HostContext(host, metadata);
    }

    private static RequestParameterUpdateReviewRequest ValidRequest()
    {
        return new RequestParameterUpdateReviewRequest { IntentRef = "intent-ref" };
    }

    private static BridgeHandshakeRequest Handshake(string instanceId, IReadOnlyList<int> versions)
    {
        return new BridgeHandshakeRequest
        {
            ExpectedInstanceId = instanceId,
            SupportedProtocolVersions = versions,
            ClientName = "RevitMCP.Tests"
        };
    }

    private static string ErrorCode(LocalRpcException exception)
    {
        return Assert.IsType<BridgeError>(exception.ErrorData).Code;
    }

    private sealed record FullServices(
        IRevitCapabilityService Capability,
        IRevitQueryElementsService Query,
        IRevitGetElementsService Elements,
        IRevitDescribeParametersService Describe,
        IRevitGetParameterValuesService Values,
        IRevitGetMepTopologyService Topology,
        IRevitPreviewParameterUpdatesService Preview,
        IRevitRequestParameterUpdateReviewService Review);

    private sealed class SelectedHandshake : IRevitBridgeService
    {
        private readonly int _selected;

        public SelectedHandshake(int selected)
        {
            _selected = selected;
        }

        public Task<BridgeHandshakeResult> HandshakeAsync(BridgeHandshakeRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new BridgeHandshakeResult
            {
                InstanceId = "selected-version",
                ProcessId = 1,
                ProcessStartTimeUtc = DateTimeOffset.UnixEpoch,
                WindowsSessionId = 1,
                RevitVersion = "2026",
                RevitBuild = "26.5.0.0",
                AddinVersion = "0.1.0",
                SupportedProtocolVersions = [_selected],
                SelectedProtocolVersion = _selected
            });
        }
    }

    private sealed record HostContext(NamedPipeBridgeHost Host, BridgeInstanceMetadata Metadata) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Host.DisposeAsync();
    }

    private sealed class RawRpc : IAsyncDisposable
    {
        private readonly JsonRpc _rpc;
        private readonly NamedPipeClientStream _pipe;

        private RawRpc(JsonRpc rpc, NamedPipeClientStream pipe)
        {
            _rpc = rpc;
            _pipe = pipe;
        }

        public static async Task<RawRpc> ConnectAsync(string pipeName)
        {
            var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3));
            var formatter = new SystemTextJsonFormatter { JsonSerializerOptions = ContractJson.CreateOptions() };
            var rpc = new JsonRpc(new HeaderDelimitedMessageHandler(pipe, formatter));
            rpc.StartListening();
            return new RawRpc(rpc, pipe);
        }

        public Task<T> InvokeAsync<T>(string method, object argument)
        {
            return _rpc.InvokeWithCancellationAsync<T>(method, [argument], CancellationToken.None);
        }

        public async ValueTask DisposeAsync()
        {
            _rpc.Dispose();
            await _pipe.DisposeAsync();
        }
    }
}
