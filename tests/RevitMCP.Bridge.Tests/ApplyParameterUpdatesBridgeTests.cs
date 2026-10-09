using System.Diagnostics;
using System.IO.Pipes;
using RevitMCP.Bridge.Implementation;
using RevitMCP.Contracts;
using StreamJsonRpc;
using Xunit;

namespace RevitMCP.Bridge.Tests;

public sealed class ApplyParameterUpdatesBridgeTests
{
    [Fact]
    public void V10_is_advertised_only_with_review_and_apply()
    {
        var metadata = TestSupport.CreateMetadata();
        var services = Full();
        var v10 = NamedPipeBridgeHost.WithAdvertisedProtocol(
            metadata, services.Capability, services.Query, services.Elements, services.Describe, services.Values, services.Topology, services.Preview, services.Review, services.Apply);
        var v9 = NamedPipeBridgeHost.WithAdvertisedProtocol(
            metadata, services.Capability, services.Query, services.Elements, services.Describe, services.Values, services.Topology, services.Preview, services.Review);
        var applyWithoutReview = NamedPipeBridgeHost.WithAdvertisedProtocol(
            metadata, services.Capability, services.Query, services.Elements, services.Describe, services.Values, services.Topology, services.Preview, requestParameterUpdateReview: null, services.Apply);

        Assert.Equal(BridgeProtocol.ApplyParameterUpdatesVersions, v10.SupportedProtocolVersions);
        Assert.Equal(10, v10.SupportedProtocolVersions.Max());
        Assert.Equal(BridgeProtocol.RequestParameterUpdateReviewVersions, v9.SupportedProtocolVersions);
        Assert.Equal(8, applyWithoutReview.SupportedProtocolVersions.Max());
        Assert.DoesNotContain(10, applyWithoutReview.SupportedProtocolVersions);
        Assert.DoesNotContain(9, applyWithoutReview.SupportedProtocolVersions);
    }

    [Fact]
    public async Task V9_cannot_call_apply_and_v10_can()
    {
        var apply = new FakeApplyParameterUpdatesService();
        var v9 = CreateAdapter(apply, 9);
        await v9.HandshakeAsync(Handshake("any", [9]), CancellationToken.None);
        var rejected = await Assert.ThrowsAsync<LocalRpcException>(() =>
            v9.ApplyParameterUpdatesAsync(Request(), CancellationToken.None));
        Assert.Equal(BridgeErrorCodes.ProtocolIncompatible, ErrorCode(rejected));
        Assert.Equal(0, apply.InvokeCount);

        var v10 = CreateAdapter(apply, 10);
        await v10.HandshakeAsync(Handshake("any", [10]), CancellationToken.None);
        var result = await v10.ApplyParameterUpdatesAsync(Request(), CancellationToken.None);
        Assert.Equal(ApplyParameterUpdatesStatus.Applied, result.Status);
        Assert.Equal(1, apply.InvokeCount);
    }

    [Fact]
    public async Task Unknown_v12_is_rejected_and_capability_errors_are_preserved()
    {
        var apply = new FakeApplyParameterUpdatesService();
        var unknown = CreateAdapter(apply, 12);
        await unknown.HandshakeAsync(Handshake("any", [12]), CancellationToken.None);
        var future = await Assert.ThrowsAsync<LocalRpcException>(() =>
            unknown.ApplyParameterUpdatesAsync(Request(), CancellationToken.None));
        Assert.Equal(BridgeErrorCodes.ProtocolIncompatible, ErrorCode(future));

        var adapter = CreateAdapter(apply, 10);
        await adapter.HandshakeAsync(Handshake("any", [10]), CancellationToken.None);
        apply.Error = new BridgeException(CapabilityErrorCodes.InvalidApplyRequest, "The apply request is invalid.");
        var invalid = await Assert.ThrowsAsync<LocalRpcException>(() =>
            adapter.ApplyParameterUpdatesAsync(Request(), CancellationToken.None));
        Assert.Equal(CapabilityErrorCodes.InvalidApplyRequest, ErrorCode(invalid));
        Assert.Equal(1, apply.InvokeCount);
    }

    [Fact]
    public async Task Typed_v10_client_can_apply_and_v9_client_cannot()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var apply = new FakeApplyParameterUpdatesService();
        await using var context = await StartHostAsync(apply);
        await using var client = await NamedPipeBridgeClient.ConnectAsync(context.Host.PipeName, TimeSpan.FromSeconds(3), CancellationToken.None);
        var handshake = await client.HandshakeAsync(Handshake(context.Metadata.InstanceId, BridgeProtocol.SupportedVersions), CancellationToken.None);
        Assert.Equal(10, handshake.SelectedProtocolVersion);
        var result = await client.ApplyParameterUpdatesAsync(Request(), TimeSpan.FromSeconds(3), CancellationToken.None);
        Assert.Equal(ApplyParameterUpdatesStatus.Applied, result.Status);

        await using var v9Client = await NamedPipeBridgeClient.ConnectAsync(context.Host.PipeName, TimeSpan.FromSeconds(3), CancellationToken.None);
        await v9Client.HandshakeAsync(Handshake(context.Metadata.InstanceId, BridgeProtocol.RequestParameterUpdateReviewVersions), CancellationToken.None);
        var rejected = await Assert.ThrowsAsync<BridgeException>(() =>
            v9Client.ApplyParameterUpdatesAsync(Request(), TimeSpan.FromSeconds(3), CancellationToken.None));
        Assert.Equal(BridgeErrorCodes.ProtocolIncompatible, rejected.ErrorCode);
        Assert.Equal(1, apply.InvokeCount);
    }

    [Fact]
    public async Task Timeout_does_not_retry_apply()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var apply = new FakeApplyParameterUpdatesService
        {
            Hold = new TaskCompletionSource<ApplyParameterUpdatesResult>(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        await using var context = await StartHostAsync(apply);
        await using var client = await NamedPipeBridgeClient.ConnectAsync(context.Host.PipeName, TimeSpan.FromSeconds(3), CancellationToken.None);
        await client.HandshakeAsync(Handshake(context.Metadata.InstanceId, BridgeProtocol.SupportedVersions), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<BridgeException>(() =>
            client.ApplyParameterUpdatesAsync(Request(), TimeSpan.FromMilliseconds(400), CancellationToken.None));
        Assert.Equal(CapabilityErrorCodes.ExecutionTimeout, exception.ErrorCode);
        await apply.RequestTokenCancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var unusable = await Assert.ThrowsAsync<BridgeException>(() =>
            client.ApplyParameterUpdatesAsync(Request(), TimeSpan.FromSeconds(3), CancellationToken.None));
        Assert.Equal(CapabilityErrorCodes.ExecutionFailed, unusable.ErrorCode);
        Assert.Equal(1, apply.InvokeCount);
        apply.Hold.TrySetCanceled();
    }

    [Fact]
    public async Task Raw_v9_peer_cannot_call_apply()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var apply = new FakeApplyParameterUpdatesService();
        await using var context = await StartHostAsync(apply);
        await using var raw = await RawRpc.ConnectAsync(context.Host.PipeName);
        await raw.InvokeAsync<BridgeHandshakeResult>(
            "bridge.handshake",
            Handshake(context.Metadata.InstanceId, BridgeProtocol.RequestParameterUpdateReviewVersions));
        var rejected = await Assert.ThrowsAsync<RemoteInvocationException>(() =>
            raw.InvokeAsync<ApplyParameterUpdatesResult>("revit.apply_parameter_updates", Request()));
        Assert.Equal(BridgeErrorCodes.ProtocolIncompatible, StreamJsonRpcExceptionMapper.FromRemote(rejected).ErrorCode);
        Assert.Equal(0, apply.InvokeCount);
    }

    private static StreamJsonRpcBridgeAdapter CreateAdapter(FakeApplyParameterUpdatesService apply, int version)
    {
        return new StreamJsonRpcBridgeAdapter(
            new SelectedHandshake(version),
            new FakeCapabilityService(),
            new FakeQueryElementsService(),
            new FakeGetElementsService(),
            new FakeDescribeParametersService(),
            new FakeGetParameterValuesService(),
            new FakeGetMepTopologyService(),
            new FakePreviewParameterUpdatesService(),
            new FakeRequestParameterUpdateReviewService(),
            apply);
    }

    private static async Task<HostContext> StartHostAsync(FakeApplyParameterUpdatesService apply)
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
            new FakeGetMepTopologyService(),
            new FakePreviewParameterUpdatesService(),
            new FakeRequestParameterUpdateReviewService(),
            apply,
            CancellationToken.None);
        return new HostContext(host, metadata);
    }

    private static ApplyParameterUpdatesRequest Request()
        => new() { IntentRef = "intent-ref" };

    private static BridgeHandshakeRequest Handshake(string instanceId, IReadOnlyList<int> versions)
        => new()
        {
            ExpectedInstanceId = instanceId,
            SupportedProtocolVersions = versions,
            ClientName = "RevitMCP.Tests"
        };

    private static string ErrorCode(LocalRpcException exception)
        => Assert.IsType<BridgeError>(exception.ErrorData).Code;

    private static FullServices Full()
        => new(
            new FakeCapabilityService(),
            new FakeQueryElementsService(),
            new FakeGetElementsService(),
            new FakeDescribeParametersService(),
            new FakeGetParameterValuesService(),
            new FakeGetMepTopologyService(),
            new FakePreviewParameterUpdatesService(),
            new FakeRequestParameterUpdateReviewService(),
            new FakeApplyParameterUpdatesService());

    private sealed class SelectedHandshake : IRevitBridgeService
    {
        private readonly int _selected;

        public SelectedHandshake(int selected) => _selected = selected;

        public Task<BridgeHandshakeResult> HandshakeAsync(BridgeHandshakeRequest request, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            return Task.FromResult(new BridgeHandshakeResult
            {
                InstanceId = request.ExpectedInstanceId,
                ProcessId = 1,
                ProcessStartTimeUtc = DateTimeOffset.UnixEpoch,
                WindowsSessionId = 1,
                RevitVersion = "2026",
                RevitBuild = "test",
                AddinVersion = "test",
                SelectedProtocolVersion = _selected,
                SupportedProtocolVersions = [_selected]
            });
        }
    }

    private readonly record struct FullServices(
        IRevitCapabilityService Capability,
        IRevitQueryElementsService Query,
        IRevitGetElementsService Elements,
        IRevitDescribeParametersService Describe,
        IRevitGetParameterValuesService Values,
        IRevitGetMepTopologyService Topology,
        IRevitPreviewParameterUpdatesService Preview,
        IRevitRequestParameterUpdateReviewService Review,
        IRevitApplyParameterUpdatesService Apply);

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
            => _rpc.InvokeWithCancellationAsync<T>(method, [argument], CancellationToken.None);

        public async ValueTask DisposeAsync()
        {
            _rpc.Dispose();
            await _pipe.DisposeAsync();
        }
    }
}

internal sealed class FakeApplyParameterUpdatesService : IRevitApplyParameterUpdatesService
{
    public int InvokeCount { get; private set; }

    public BridgeException? Error { get; set; }

    public TaskCompletionSource<ApplyParameterUpdatesResult>? Hold { get; set; }

    public TaskCompletionSource RequestTokenCancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<ApplyParameterUpdatesResult> ApplyParameterUpdatesAsync(
        ApplyParameterUpdatesRequest request,
        CancellationToken cancellationToken)
    {
        _ = request;
        InvokeCount++;
        cancellationToken.Register(() => RequestTokenCancelled.TrySetResult());
        if (Hold is not null)
        {
            return await Hold.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        if (Error is not null)
        {
            throw Error;
        }

        return new ApplyParameterUpdatesResult { Status = ApplyParameterUpdatesStatus.Applied };
    }
}
