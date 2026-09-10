using System.Diagnostics;
using System.IO.Pipes;
using RevitMCP.Bridge.Implementation;
using RevitMCP.Contracts;
using StreamJsonRpc;
using Xunit;

namespace RevitMCP.Bridge.Tests;

public sealed class BridgeEndpointGatingTests
{
    [Fact]
    public async Task Query_before_handshake_does_not_invoke_the_service()
    {
        var query = new FakeQueryElementsService();
        var fixture = CreateFullAdapter(query: query);

        var exception = await Assert.ThrowsAsync<LocalRpcException>(() =>
            fixture.Adapter.QueryElementsAsync(FakeQueryElementsService.CreateValidRequest(), CancellationToken.None));

        AssertHandshakeRequired(exception);
        Assert.Equal(0, query.InvokeCount);
    }

    [Fact]
    public async Task Query_after_negotiated_v1_does_not_invoke_the_service()
    {
        var query = new FakeQueryElementsService();
        var fixture = CreateFullAdapter(query: query);
        await HandshakeAsync(fixture, [1]);

        var exception = await Assert.ThrowsAsync<LocalRpcException>(() =>
            fixture.Adapter.QueryElementsAsync(FakeQueryElementsService.CreateValidRequest(), CancellationToken.None));

        AssertProtocolIncompatible(exception);
        Assert.Equal(0, query.InvokeCount);
    }

    [Fact]
    public async Task Query_after_negotiated_v2_does_not_invoke_the_service()
    {
        var query = new FakeQueryElementsService();
        var fixture = CreateFullAdapter(query: query);
        await HandshakeAsync(fixture, [2, 1]);

        var exception = await Assert.ThrowsAsync<LocalRpcException>(() =>
            fixture.Adapter.QueryElementsAsync(FakeQueryElementsService.CreateValidRequest(), CancellationToken.None));

        AssertProtocolIncompatible(exception);
        Assert.Equal(0, query.InvokeCount);
    }

    [Fact]
    public async Task Get_context_before_handshake_does_not_invoke_the_service()
    {
        var capability = new FakeCapabilityService();
        var fixture = CreateFullAdapter(capability);

        var exception = await Assert.ThrowsAsync<LocalRpcException>(() =>
            fixture.Adapter.GetContextAsync(new GetContextRequest(), CancellationToken.None));

        AssertHandshakeRequired(exception);
        Assert.Equal(0, capability.InvokeCount);
    }

    [Fact]
    public async Task Get_context_after_v1_does_not_invoke_the_service()
    {
        var capability = new FakeCapabilityService();
        var fixture = CreateFullAdapter(capability);
        await HandshakeAsync(fixture, [1]);

        var exception = await Assert.ThrowsAsync<LocalRpcException>(() =>
            fixture.Adapter.GetContextAsync(new GetContextRequest(), CancellationToken.None));

        AssertProtocolIncompatible(exception);
        Assert.Equal(0, capability.InvokeCount);
    }

    [Fact]
    public async Task Query_only_host_cannot_execute_query_after_v1()
    {
        var query = new FakeQueryElementsService();
        var metadata = TestSupport.CreateMetadata(protocolVersions: BridgeProtocol.HandshakeOnlyVersions);
        var fixture = new AdapterFixture(
            new StreamJsonRpcBridgeAdapter(new BridgeHandshakeService(metadata), capability: null, query),
            metadata.InstanceId);
        await HandshakeAsync(fixture, [3, 2, 1]);

        var exception = await Assert.ThrowsAsync<LocalRpcException>(() =>
            fixture.Adapter.QueryElementsAsync(FakeQueryElementsService.CreateValidRequest(), CancellationToken.None));

        AssertProtocolIncompatible(exception);
        Assert.Equal(0, query.InvokeCount);
    }

    [Fact]
    public async Task Full_host_after_v3_still_executes_query()
    {
        var query = new FakeQueryElementsService { Result = FakeQueryElementsService.CreateBoundedResult() };
        var fixture = CreateFullAdapter(query: query);
        await HandshakeAsync(fixture, [3, 2, 1]);

        var result = await fixture.Adapter.QueryElementsAsync(FakeQueryElementsService.CreateValidRequest(), CancellationToken.None);

        Assert.Equal(1, query.InvokeCount);
        Assert.Equal(2, result.MatchedCount);
    }

    [Fact]
    public async Task Get_context_after_v2_and_v3_still_executes()
    {
        var capability = new FakeCapabilityService { Result = FakeCapabilityService.CreateZeroDocumentResult() };
        var fixture = CreateFullAdapter(capability);
        await HandshakeAsync(fixture, [2, 1]);
        Assert.Null((await fixture.Adapter.GetContextAsync(new GetContextRequest(), CancellationToken.None)).Document);
        Assert.Equal(1, capability.InvokeCount);

        await HandshakeAsync(fixture, [3, 2, 1]);
        Assert.Null((await fixture.Adapter.GetContextAsync(new GetContextRequest(), CancellationToken.None)).Document);
        Assert.Equal(2, capability.InvokeCount);
    }

    [Fact]
    public async Task Failed_handshake_does_not_unlock_query()
    {
        var query = new FakeQueryElementsService();
        var fixture = CreateFullAdapter(query: query);

        await Assert.ThrowsAsync<LocalRpcException>(() =>
            fixture.Adapter.HandshakeAsync(
                new BridgeHandshakeRequest
                {
                    ExpectedInstanceId = "wrong-instance",
                    SupportedProtocolVersions = [3, 2, 1],
                    ClientName = "RevitMCP.Tests"
                },
                CancellationToken.None));

        var exception = await Assert.ThrowsAsync<LocalRpcException>(() =>
            fixture.Adapter.QueryElementsAsync(FakeQueryElementsService.CreateValidRequest(), CancellationToken.None));

        AssertHandshakeRequired(exception);
        Assert.Equal(0, query.InvokeCount);
    }

    [Fact]
    public async Task Unknown_selected_version_does_not_invoke_query_or_get_context()
    {
        var capability = new FakeCapabilityService();
        var query = new FakeQueryElementsService();
        var adapter = new StreamJsonRpcBridgeAdapter(new SelectedVersionHandshake(4), capability, query);
        await adapter.HandshakeAsync(
            new BridgeHandshakeRequest
            {
                ExpectedInstanceId = "any",
                SupportedProtocolVersions = [4],
                ClientName = "RevitMCP.Tests"
            },
            CancellationToken.None);

        var queryException = await Assert.ThrowsAsync<LocalRpcException>(() =>
            adapter.QueryElementsAsync(FakeQueryElementsService.CreateValidRequest(), CancellationToken.None));
        var contextException = await Assert.ThrowsAsync<LocalRpcException>(() =>
            adapter.GetContextAsync(new GetContextRequest(), CancellationToken.None));

        AssertProtocolIncompatible(queryException);
        AssertProtocolIncompatible(contextException);
        Assert.Equal(0, query.InvokeCount);
        Assert.Equal(0, capability.InvokeCount);
    }

    [Fact]
    public async Task Raw_rpc_query_before_handshake_does_not_invoke_the_service()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var query = new FakeQueryElementsService();
        await using var context = await StartFullHostAsync(query);
        await using var raw = await RawRpc.ConnectAsync(context.Host.PipeName);

        var exception = await Assert.ThrowsAsync<RemoteInvocationException>(() =>
            raw.InvokeWithCancellationAsync<QueryElementsResult>(
                "revit.query_elements",
                [FakeQueryElementsService.CreateValidRequest()],
                CancellationToken.None));

        var mapped = StreamJsonRpcExceptionMapper.FromRemote(exception);
        Assert.Equal(BridgeErrorCodes.HandshakeFailed, mapped.ErrorCode);
        Assert.Equal(0, query.InvokeCount);
    }

    [Fact]
    public async Task Raw_rpc_query_after_v2_does_not_invoke_the_service()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var query = new FakeQueryElementsService();
        await using var context = await StartFullHostAsync(query);
        await using var raw = await RawRpc.ConnectAsync(context.Host.PipeName);
        await raw.InvokeWithCancellationAsync<BridgeHandshakeResult>(
            "bridge.handshake",
            [CreateHandshakeRequest(context.Metadata.InstanceId, [2, 1])],
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<RemoteInvocationException>(() =>
            raw.InvokeWithCancellationAsync<QueryElementsResult>(
                "revit.query_elements",
                [FakeQueryElementsService.CreateValidRequest()],
                CancellationToken.None));

        var mapped = StreamJsonRpcExceptionMapper.FromRemote(exception);
        Assert.Equal(BridgeErrorCodes.ProtocolIncompatible, mapped.ErrorCode);
        Assert.Equal(0, query.InvokeCount);
    }

    private static AdapterFixture CreateFullAdapter(
        IRevitCapabilityService? capability = null,
        IRevitQueryElementsService? query = null)
    {
        var metadata = TestSupport.CreateMetadata();
        return new AdapterFixture(
            new StreamJsonRpcBridgeAdapter(
                new BridgeHandshakeService(metadata),
                capability ?? new FakeCapabilityService(),
                query ?? new FakeQueryElementsService()),
            metadata.InstanceId);
    }

    private static Task<BridgeHandshakeResult> HandshakeAsync(AdapterFixture fixture, IReadOnlyList<int> versions)
    {
        return fixture.Adapter.HandshakeAsync(CreateHandshakeRequest(fixture.InstanceId, versions), CancellationToken.None);
    }

    private sealed record AdapterFixture(StreamJsonRpcBridgeAdapter Adapter, string InstanceId);

    private static BridgeHandshakeRequest CreateHandshakeRequest(string instanceId, IReadOnlyList<int> versions)
    {
        return new BridgeHandshakeRequest
        {
            ExpectedInstanceId = instanceId,
            SupportedProtocolVersions = versions,
            ClientName = "RevitMCP.Tests"
        };
    }

    private static void AssertHandshakeRequired(LocalRpcException exception)
    {
        var error = Assert.IsType<BridgeError>(exception.ErrorData);
        Assert.Equal(BridgeErrorCodes.HandshakeFailed, error.Code);
    }

    private static void AssertProtocolIncompatible(LocalRpcException exception)
    {
        var error = Assert.IsType<BridgeError>(exception.ErrorData);
        Assert.Equal(BridgeErrorCodes.ProtocolIncompatible, error.Code);
    }

    private static async Task<HostContext> StartFullHostAsync(IRevitQueryElementsService query)
    {
        var metadata = CreateLiveMetadata();
        var root = Path.Combine(Path.GetTempPath(), "RevitMCP.Tests", Guid.NewGuid().ToString("N"));
        var store = new FileRegistrationStore(root);
        var host = await NamedPipeBridgeHost.StartAsync(
            metadata,
            store,
            new FakeCapabilityService(),
            query,
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
