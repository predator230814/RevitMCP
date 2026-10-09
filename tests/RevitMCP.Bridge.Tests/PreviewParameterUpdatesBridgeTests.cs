using System.Diagnostics;
using System.IO.Pipes;
using RevitMCP.Bridge.Implementation;
using RevitMCP.Contracts;
using StreamJsonRpc;
using Xunit;

namespace RevitMCP.Bridge.Tests;

public sealed class PreviewParameterUpdatesBridgeTests
{
    [Fact]
    public void Advertisement_requires_the_complete_prefix_plus_preview()
    {
        var metadata = TestSupport.CreateMetadata();
        var full = NamedPipeBridgeHost.WithAdvertisedProtocol(
            metadata,
            new FakeCapabilityService(),
            new FakeQueryElementsService(),
            new FakeGetElementsService(),
            new FakeDescribeParametersService(),
            new FakeGetParameterValuesService(),
            new FakeGetMepTopologyService(),
            new FakePreviewParameterUpdatesService());
        var v7 = NamedPipeBridgeHost.WithAdvertisedProtocol(
            metadata,
            new FakeCapabilityService(),
            new FakeQueryElementsService(),
            new FakeGetElementsService(),
            new FakeDescribeParametersService(),
            new FakeGetParameterValuesService(),
            new FakeGetMepTopologyService());
        var previewOnly = NamedPipeBridgeHost.WithAdvertisedProtocol(
            metadata,
            previewParameterUpdates: new FakePreviewParameterUpdatesService());
        var incomplete = NamedPipeBridgeHost.WithAdvertisedProtocol(
            metadata,
            new FakeCapabilityService(),
            new FakeQueryElementsService(),
            new FakeGetElementsService(),
            new FakeDescribeParametersService(),
            new FakeGetParameterValuesService(),
            getMepTopology: null,
            previewParameterUpdates: new FakePreviewParameterUpdatesService());

        Assert.Equal(11, BridgeProtocol.CurrentVersion);
        Assert.Equal(BridgeProtocol.PreviewParameterUpdatesVersions, full.SupportedProtocolVersions);
        Assert.Equal(new[] { 8, 7, 6, 5, 4, 3, 2, 1 }, full.SupportedProtocolVersions);
        Assert.DoesNotContain(9, full.SupportedProtocolVersions);
        Assert.Equal(BridgeProtocol.GetMepTopologyVersions, v7.SupportedProtocolVersions);
        Assert.Equal(new[] { 7, 6, 5, 4, 3, 2, 1 }, v7.SupportedProtocolVersions);
        Assert.DoesNotContain(8, v7.SupportedProtocolVersions);
        Assert.Equal(BridgeProtocol.HandshakeOnlyVersions, previewOnly.SupportedProtocolVersions);
        Assert.Equal(BridgeProtocol.GetParameterValuesVersions, incomplete.SupportedProtocolVersions);
        Assert.DoesNotContain(8, incomplete.SupportedProtocolVersions);
    }

    [Fact]
    public void Capability_support_sets_include_v9_and_reject_v10()
    {
        Assert.Equal(11, BridgeProtocol.CurrentVersion);
        Assert.True(BridgeProtocol.SupportsGetContext(9));
        Assert.True(BridgeProtocol.SupportsQueryElements(9));
        Assert.True(BridgeProtocol.SupportsGetElements(9));
        Assert.True(BridgeProtocol.SupportsDescribeParameters(9));
        Assert.True(BridgeProtocol.SupportsGetParameterValues(9));
        Assert.True(BridgeProtocol.SupportsGetMepTopology(9));
        Assert.True(BridgeProtocol.SupportsPreviewParameterUpdates(9));
        Assert.True(BridgeProtocol.SupportsPreviewParameterUpdates(8));
        Assert.False(BridgeProtocol.SupportsRequestParameterUpdateReview(8));

        foreach (var version in new[] { 1, 2, 3, 4, 5, 6, 7, 12 })
        {
            Assert.False(BridgeProtocol.SupportsPreviewParameterUpdates(version));
        }

        Assert.True(BridgeProtocol.SupportsGetContext(11)); Assert.False(BridgeProtocol.SupportsGetContext(12));
        Assert.True(BridgeProtocol.SupportsQueryElements(11)); Assert.False(BridgeProtocol.SupportsQueryElements(12));
        Assert.True(BridgeProtocol.SupportsGetElements(11)); Assert.False(BridgeProtocol.SupportsGetElements(12));
        Assert.True(BridgeProtocol.SupportsDescribeParameters(11)); Assert.False(BridgeProtocol.SupportsDescribeParameters(12));
        Assert.True(BridgeProtocol.SupportsGetParameterValues(11)); Assert.False(BridgeProtocol.SupportsGetParameterValues(12));
        Assert.True(BridgeProtocol.SupportsGetMepTopology(11)); Assert.False(BridgeProtocol.SupportsGetMepTopology(12));
        Assert.True(BridgeProtocol.SupportsPreviewParameterUpdates(11)); Assert.False(BridgeProtocol.SupportsPreviewParameterUpdates(12));
        Assert.True(BridgeProtocol.SupportsRequestParameterUpdateReview(11)); Assert.False(BridgeProtocol.SupportsRequestParameterUpdateReview(12));
    }

    [Fact]
    public async Task Typed_client_round_trips_preview_on_v8_and_keeps_inherited_capabilities()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var preview = new FakePreviewParameterUpdatesService();
        var capability = new FakeCapabilityService { Result = FakeCapabilityService.CreateZeroDocumentResult() };
        var query = new FakeQueryElementsService { Result = FakeQueryElementsService.CreateBoundedResult() };
        var getElements = new FakeGetElementsService { Result = FakeGetElementsService.CreateOkResult() };
        var describe = new FakeDescribeParametersService { Result = FakeDescribeParametersService.CreateOkResult() };
        var values = new FakeGetParameterValuesService { Result = FakeGetParameterValuesService.CreateOkResult() };
        var topology = new FakeGetMepTopologyService { Result = FakeGetMepTopologyService.CreateResult() };
        await using var context = await StartV8HostAsync(preview, capability, query, getElements, describe, values, topology);
        await using var client = await ConnectAndHandshakeAsync(context, BridgeProtocol.SupportedVersions);

        var result = await client.PreviewParameterUpdatesAsync(
            FakePreviewParameterUpdatesService.CreateRequest(),
            TimeSpan.FromSeconds(3),
            CancellationToken.None);
        var contextResult = await client.GetContextAsync(new GetContextRequest(), TimeSpan.FromSeconds(3), CancellationToken.None);
        var queryResult = await client.QueryElementsAsync(
            FakeQueryElementsService.CreateValidRequest(),
            TimeSpan.FromSeconds(3),
            CancellationToken.None);
        var elements = await client.GetElementsAsync(
            FakeGetElementsService.CreateValidRequest(),
            TimeSpan.FromSeconds(3),
            CancellationToken.None);
        var described = await client.DescribeParametersAsync(
            FakeDescribeParametersService.CreateValidRequest(),
            TimeSpan.FromSeconds(3),
            CancellationToken.None);
        var read = await client.GetParameterValuesAsync(
            FakeGetParameterValuesService.CreateValidRequest(),
            TimeSpan.FromSeconds(3),
            CancellationToken.None);
        var mep = await client.GetMepTopologyAsync(
            FakeGetMepTopologyService.CreateRequest(),
            TimeSpan.FromSeconds(3),
            CancellationToken.None);

        Assert.True(result.Ready);
        Assert.Equal("intent-ref", result.IntentRef);
        Assert.Equal(1, preview.InvokeCount);
        Assert.Null(contextResult.Document);
        Assert.Equal(2, queryResult.MatchedCount);
        Assert.Equal(GetElementResultStatus.Ok, Assert.Single(elements.Elements).Status);
        Assert.Equal("opaque-parameter-ref", Assert.Single(described.Parameters).ParameterRef);
        Assert.Equal(GetParameterValueStatus.Ok, Assert.Single(read.Items).Status);
        Assert.Equal(MepTopologySeedStatus.Ok, Assert.Single(mep.Seeds).Status);
        Assert.Equal(1, capability.InvokeCount);
        Assert.Equal(1, query.InvokeCount);
        Assert.Equal(1, getElements.InvokeCount);
        Assert.Equal(1, describe.InvokeCount);
        Assert.Equal(1, values.InvokeCount);
        Assert.Equal(1, topology.InvokeCount);
    }

    [Fact]
    public async Task Ready_false_remains_a_successful_typed_result()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var preview = new FakePreviewParameterUpdatesService
        {
            Result = FakePreviewParameterUpdatesService.CreateNotReadyResult()
        };
        await using var context = await StartV8HostAsync(preview);
        await using var client = await ConnectAndHandshakeAsync(context, BridgeProtocol.SupportedVersions);

        var result = await client.PreviewParameterUpdatesAsync(
            FakePreviewParameterUpdatesService.CreateRequest(),
            TimeSpan.FromSeconds(3),
            CancellationToken.None);

        Assert.False(result.Ready);
        Assert.Equal(PreviewParameterUpdateStatus.ParameterNotWritable, Assert.Single(result.Items).Status);
        Assert.Null(result.IntentRef);
        Assert.Equal(1, preview.InvokeCount);
    }

    [Theory]
    [InlineData(new[] { 1 })]
    [InlineData(new[] { 2, 1 })]
    [InlineData(new[] { 3, 2, 1 })]
    [InlineData(new[] { 4, 3, 2, 1 })]
    [InlineData(new[] { 5, 4, 3, 2, 1 })]
    [InlineData(new[] { 6, 5, 4, 3, 2, 1 })]
    [InlineData(new[] { 7, 6, 5, 4, 3, 2, 1 })]
    public async Task Typed_preview_is_rejected_on_v1_through_v7_before_rpc(int[] versions)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var preview = new FakePreviewParameterUpdatesService();
        await using var context = await StartV8HostAsync(preview);
        await using var client = await ConnectAndHandshakeAsync(context, versions);

        var exception = await Assert.ThrowsAsync<BridgeException>(() =>
            client.PreviewParameterUpdatesAsync(
                FakePreviewParameterUpdatesService.CreateRequest(),
                TimeSpan.FromSeconds(3),
                CancellationToken.None));

        Assert.Equal(BridgeErrorCodes.ProtocolIncompatible, exception.ErrorCode);
        Assert.Equal(0, preview.InvokeCount);
    }

    [Fact]
    public async Task Intent_capacity_reached_survives_the_typed_bridge()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var preview = new FakePreviewParameterUpdatesService
        {
            Error = new BridgeException(CapabilityErrorCodes.IntentCapacityReached, "The intent store is full.")
        };
        await using var context = await StartV8HostAsync(preview);
        await using var client = await ConnectAndHandshakeAsync(context, BridgeProtocol.SupportedVersions);

        var exception = await Assert.ThrowsAsync<BridgeException>(() =>
            client.PreviewParameterUpdatesAsync(
                FakePreviewParameterUpdatesService.CreateRequest(),
                TimeSpan.FromSeconds(3),
                CancellationToken.None));

        Assert.Equal(CapabilityErrorCodes.IntentCapacityReached, exception.ErrorCode);
        Assert.Equal(1, preview.InvokeCount);
    }

    [Fact]
    public async Task Timeout_cancels_the_preview_request_and_does_not_retry()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var preview = new FakePreviewParameterUpdatesService
        {
            Hold = new TaskCompletionSource<PreviewParameterUpdatesResult>(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        await using var context = await StartV8HostAsync(preview);
        await using var client = await ConnectAndHandshakeAsync(context, BridgeProtocol.SupportedVersions);

        var exception = await Assert.ThrowsAsync<BridgeException>(() =>
            client.PreviewParameterUpdatesAsync(
                FakePreviewParameterUpdatesService.CreateRequest(),
                TimeSpan.FromMilliseconds(400),
                CancellationToken.None));

        Assert.Equal(CapabilityErrorCodes.ExecutionTimeout, exception.ErrorCode);
        await preview.RequestTokenCancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var unusable = await Assert.ThrowsAsync<BridgeException>(() =>
            client.PreviewParameterUpdatesAsync(
                FakePreviewParameterUpdatesService.CreateRequest(),
                TimeSpan.FromSeconds(3),
                CancellationToken.None));
        Assert.Equal(CapabilityErrorCodes.ExecutionFailed, unusable.ErrorCode);
        Assert.Equal(1, preview.InvokeCount);
        preview.Hold.TrySetCanceled();
    }

    [Fact]
    public async Task Adapter_rejects_preview_before_handshake_and_on_v1_through_v7()
    {
        var preview = new FakePreviewParameterUpdatesService();
        var fixture = CreateAdapter(preview);

        var before = await Assert.ThrowsAsync<LocalRpcException>(() =>
            fixture.Adapter.PreviewParameterUpdatesAsync(
                FakePreviewParameterUpdatesService.CreateRequest(),
                CancellationToken.None));
        Assert.Equal(BridgeErrorCodes.HandshakeFailed, ErrorCode(before));
        Assert.Equal(0, preview.InvokeCount);

        foreach (var versions in new[]
        {
            new[] { 1 },
            new[] { 2, 1 },
            new[] { 3, 2, 1 },
            new[] { 4, 3, 2, 1 },
            new[] { 5, 4, 3, 2, 1 },
            new[] { 6, 5, 4, 3, 2, 1 },
            new[] { 7, 6, 5, 4, 3, 2, 1 }
        })
        {
            await fixture.Adapter.HandshakeAsync(CreateHandshakeRequest(fixture.InstanceId, versions), CancellationToken.None);
            var rejected = await Assert.ThrowsAsync<LocalRpcException>(() =>
                fixture.Adapter.PreviewParameterUpdatesAsync(
                    FakePreviewParameterUpdatesService.CreateRequest(),
                    CancellationToken.None));
            Assert.Equal(BridgeErrorCodes.ProtocolIncompatible, ErrorCode(rejected));
        }

        Assert.Equal(0, preview.InvokeCount);
    }

    [Fact]
    public async Task Adapter_permits_v8_and_preserves_capability_errors()
    {
        var preview = new FakePreviewParameterUpdatesService();
        var fixture = CreateAdapter(preview);
        await fixture.Adapter.HandshakeAsync(
            CreateHandshakeRequest(fixture.InstanceId, BridgeProtocol.SupportedVersions),
            CancellationToken.None);

        var ready = await fixture.Adapter.PreviewParameterUpdatesAsync(
            FakePreviewParameterUpdatesService.CreateRequest(),
            CancellationToken.None);
        Assert.True(ready.Ready);
        Assert.Equal(1, preview.InvokeCount);

        preview.Result = FakePreviewParameterUpdatesService.CreateNotReadyResult();
        var notReady = await fixture.Adapter.PreviewParameterUpdatesAsync(
            FakePreviewParameterUpdatesService.CreateRequest(),
            CancellationToken.None);
        Assert.False(notReady.Ready);

        preview.Result = null;
        preview.Error = new BridgeException(CapabilityErrorCodes.IntentCapacityReached, "The intent store is full.");
        var capacity = await Assert.ThrowsAsync<LocalRpcException>(() =>
            fixture.Adapter.PreviewParameterUpdatesAsync(
                FakePreviewParameterUpdatesService.CreateRequest(),
                CancellationToken.None));
        Assert.Equal(CapabilityErrorCodes.IntentCapacityReached, ErrorCode(capacity));

        preview.Error = new InvalidOperationException("boom");
        var failed = await Assert.ThrowsAsync<LocalRpcException>(() =>
            fixture.Adapter.PreviewParameterUpdatesAsync(
                FakePreviewParameterUpdatesService.CreateRequest(),
                CancellationToken.None));
        Assert.Equal(CapabilityErrorCodes.ExecutionFailed, ErrorCode(failed));

        preview.Error = new OperationCanceledException();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            fixture.Adapter.PreviewParameterUpdatesAsync(
                FakePreviewParameterUpdatesService.CreateRequest(),
                CancellationToken.None));
    }

    [Fact]
    public async Task Adapter_rejects_unknown_v10_and_a_missing_preview_service()
    {
        var preview = new FakePreviewParameterUpdatesService();
        var unknown = new StreamJsonRpcBridgeAdapter(new SelectedVersionHandshake(12), capability: null, previewParameterUpdates: preview);
        await unknown.HandshakeAsync(CreateHandshakeRequest("any", [12]), CancellationToken.None);
        var future = await Assert.ThrowsAsync<LocalRpcException>(() =>
            unknown.PreviewParameterUpdatesAsync(
                FakePreviewParameterUpdatesService.CreateRequest(),
                CancellationToken.None));
        Assert.Equal(BridgeErrorCodes.ProtocolIncompatible, ErrorCode(future));
        Assert.Equal(0, preview.InvokeCount);

        var missing = new StreamJsonRpcBridgeAdapter(new SelectedVersionHandshake(8), capability: null);
        await missing.HandshakeAsync(CreateHandshakeRequest("any", [8]), CancellationToken.None);
        var incompatible = await Assert.ThrowsAsync<LocalRpcException>(() =>
            missing.PreviewParameterUpdatesAsync(
                FakePreviewParameterUpdatesService.CreateRequest(),
                CancellationToken.None));
        Assert.Equal(BridgeErrorCodes.ProtocolIncompatible, ErrorCode(incompatible));
    }

    [Fact]
    public async Task Raw_peer_cannot_bypass_the_preview_gate()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var preview = new FakePreviewParameterUpdatesService();
        await using var context = await StartV8HostAsync(preview);
        await using var raw = await RawRpc.ConnectAsync(context.Host.PipeName);

        var before = await Assert.ThrowsAsync<RemoteInvocationException>(() =>
            raw.InvokeWithCancellationAsync<PreviewParameterUpdatesResult>(
                "revit.preview_parameter_updates",
                [FakePreviewParameterUpdatesService.CreateRequest()],
                CancellationToken.None));
        Assert.Equal(BridgeErrorCodes.HandshakeFailed, StreamJsonRpcExceptionMapper.FromRemote(before).ErrorCode);

        await raw.InvokeWithCancellationAsync<BridgeHandshakeResult>(
            "bridge.handshake",
            [CreateHandshakeRequest(context.Metadata.InstanceId, BridgeProtocol.GetMepTopologyVersions)],
            CancellationToken.None);
        var v7 = await Assert.ThrowsAsync<RemoteInvocationException>(() =>
            raw.InvokeWithCancellationAsync<PreviewParameterUpdatesResult>(
                "revit.preview_parameter_updates",
                [FakePreviewParameterUpdatesService.CreateRequest()],
                CancellationToken.None));
        Assert.Equal(BridgeErrorCodes.ProtocolIncompatible, StreamJsonRpcExceptionMapper.FromRemote(v7).ErrorCode);
        Assert.Equal(0, preview.InvokeCount);

        await using var permitted = await RawRpc.ConnectAsync(context.Host.PipeName);
        await permitted.InvokeWithCancellationAsync<BridgeHandshakeResult>(
            "bridge.handshake",
            [CreateHandshakeRequest(context.Metadata.InstanceId, BridgeProtocol.SupportedVersions)],
            CancellationToken.None);
        var result = await permitted.InvokeWithCancellationAsync<PreviewParameterUpdatesResult>(
            "revit.preview_parameter_updates",
            [FakePreviewParameterUpdatesService.CreateRequest()],
            CancellationToken.None);

        Assert.True(result.Ready);
        Assert.Equal(1, preview.InvokeCount);
    }

    private static AdapterFixture CreateAdapter(IRevitPreviewParameterUpdatesService preview)
    {
        var metadata = TestSupport.CreateMetadata();
        return new AdapterFixture(
            new StreamJsonRpcBridgeAdapter(
                new BridgeHandshakeService(metadata),
                new FakeCapabilityService(),
                new FakeQueryElementsService(),
                new FakeGetElementsService(),
                new FakeDescribeParametersService(),
                new FakeGetParameterValuesService(),
                new FakeGetMepTopologyService(),
                preview),
            metadata.InstanceId);
    }

    private static async Task<NamedPipeBridgeClient> ConnectAndHandshakeAsync(HostContext context, IReadOnlyList<int> versions)
    {
        var client = await NamedPipeBridgeClient.ConnectAsync(context.Host.PipeName, TimeSpan.FromSeconds(3), CancellationToken.None);
        await client.HandshakeAsync(CreateHandshakeRequest(context.Metadata.InstanceId, versions), CancellationToken.None);
        return client;
    }

    private static async Task<HostContext> StartV8HostAsync(
        IRevitPreviewParameterUpdatesService preview,
        IRevitCapabilityService? capability = null,
        IRevitQueryElementsService? query = null,
        IRevitGetElementsService? getElements = null,
        IRevitDescribeParametersService? describe = null,
        IRevitGetParameterValuesService? values = null,
        IRevitGetMepTopologyService? topology = null)
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
            getElements ?? new FakeGetElementsService(),
            describe ?? new FakeDescribeParametersService(),
            values ?? new FakeGetParameterValuesService(),
            topology ?? new FakeGetMepTopologyService(),
            preview,
            CancellationToken.None);
        return new HostContext(host, metadata);
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

    private static string ErrorCode(LocalRpcException exception)
    {
        return Assert.IsType<BridgeError>(exception.ErrorData).Code;
    }

    private sealed record AdapterFixture(StreamJsonRpcBridgeAdapter Adapter, string InstanceId);

    private sealed class SelectedVersionHandshake : IRevitBridgeService
    {
        private readonly int _selected;

        public SelectedVersionHandshake(int selected)
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

    private sealed class HostContext(NamedPipeBridgeHost host, BridgeInstanceMetadata metadata) : IAsyncDisposable
    {
        public NamedPipeBridgeHost Host { get; } = host;

        public BridgeInstanceMetadata Metadata { get; } = metadata;

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
            var pipe = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3));
            var formatter = new SystemTextJsonFormatter
            {
                JsonSerializerOptions = ContractJson.CreateOptions()
            };
            var rpc = new JsonRpc(new HeaderDelimitedMessageHandler(pipe, formatter));
            rpc.StartListening();
            return new RawRpc(rpc, pipe);
        }

        public Task<T> InvokeWithCancellationAsync<T>(string method, object?[] arguments, CancellationToken cancellationToken)
        {
            return _rpc.InvokeWithCancellationAsync<T>(method, arguments, cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            _rpc.Dispose();
            await _pipe.DisposeAsync();
        }
    }
}
