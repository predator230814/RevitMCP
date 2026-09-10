using System.Diagnostics;
using RevitMCP.Contracts;
using Xunit;

namespace RevitMCP.Bridge.Tests;

public sealed class QueryElementsBridgeTests
{
    [Fact]
    public async Task Full_host_advertises_protocol_v3()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await using var context = await StartFullHostAsync();

        Assert.Equal(3, context.Host.Registration?.BridgeProtocolVersion);
        Assert.Equal(BridgeProtocol.SupportedVersions, context.Host.Metadata.SupportedProtocolVersions);
    }

    [Fact]
    public async Task Cap0001_only_host_still_advertises_protocol_v2()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await using var context = await StartHostAsync(new FakeCapabilityService());

        Assert.Equal(2, context.Host.Registration?.BridgeProtocolVersion);
        Assert.Equal(BridgeProtocol.GetContextVersions, context.Host.Metadata.SupportedProtocolVersions);
    }

    [Fact]
    public async Task Handshake_only_host_still_advertises_protocol_v1()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await using var context = await StartHostAsync();

        Assert.Equal(1, context.Host.Registration?.BridgeProtocolVersion);
        Assert.Equal(BridgeProtocol.HandshakeOnlyVersions, context.Host.Metadata.SupportedProtocolVersions);
    }

    [Fact]
    public async Task Query_only_configuration_never_advertises_v3()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await using var context = await StartHostAsync(capability: null, query: new FakeQueryElementsService());

        Assert.Equal(1, context.Host.Registration?.BridgeProtocolVersion);
        Assert.DoesNotContain(3, context.Host.Metadata.SupportedProtocolVersions);
    }

    [Fact]
    public async Task V3_handshake_selects_3_when_both_peers_support_full_set()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await using var context = await StartFullHostAsync();
        await using var client = await NamedPipeBridgeClient.ConnectAsync(context.Host.PipeName, TimeSpan.FromSeconds(3), CancellationToken.None);

        var result = await client.HandshakeAsync(CreateHandshakeRequest(context.Metadata.InstanceId, [3, 2, 1]), CancellationToken.None);

        Assert.Equal(3, result.SelectedProtocolVersion);
        Assert.Equal(new[] { 3, 2, 1 }, result.SupportedProtocolVersions);
    }

    [Fact]
    public async Task Get_context_is_allowed_after_v3()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var capability = new FakeCapabilityService { Result = FakeCapabilityService.CreateZeroDocumentResult() };
        await using var context = await StartFullHostAsync(capability, new FakeQueryElementsService());
        await using var client = await ConnectAndHandshakeAsync(context, [3, 2, 1]);

        var result = await client.GetContextAsync(new GetContextRequest(), TimeSpan.FromSeconds(3), CancellationToken.None);

        Assert.Equal(1, capability.InvokeCount);
        Assert.Null(result.Document);
    }

    [Fact]
    public void Unknown_v4_does_not_allow_get_context_or_query()
    {
        Assert.False(BridgeProtocol.SupportsGetContext(4));
        Assert.False(BridgeProtocol.SupportsQueryElements(4));
    }

    [Fact]
    public async Task Query_is_allowed_after_v3()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var query = new FakeQueryElementsService { Result = FakeQueryElementsService.CreateBoundedResult() };
        await using var context = await StartFullHostAsync(query: query);
        await using var client = await ConnectAndHandshakeAsync(context, [3, 2, 1]);

        var result = await client.QueryElementsAsync(FakeQueryElementsService.CreateValidRequest(), TimeSpan.FromSeconds(3), CancellationToken.None);

        Assert.Equal(1, query.InvokeCount);
        Assert.Equal(2, result.MatchedCount);
        Assert.True(result.Truncated);
        Assert.Equal(new[] { "ref-a" }, result.ElementRefs);
    }

    [Fact]
    public async Task Query_is_rejected_after_v1()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var query = new FakeQueryElementsService();
        await using var context = await StartFullHostAsync(query: query);
        await using var client = await ConnectAndHandshakeAsync(context, [1]);

        var exception = await Assert.ThrowsAsync<BridgeException>(() =>
            client.QueryElementsAsync(FakeQueryElementsService.CreateValidRequest(), TimeSpan.FromSeconds(3), CancellationToken.None));

        Assert.Equal(BridgeErrorCodes.ProtocolIncompatible, exception.ErrorCode);
        Assert.Equal(0, query.InvokeCount);
    }

    [Fact]
    public async Task Query_is_rejected_after_v2()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var query = new FakeQueryElementsService();
        await using var context = await StartFullHostAsync(query: query);
        await using var client = await ConnectAndHandshakeAsync(context, [2, 1]);

        var exception = await Assert.ThrowsAsync<BridgeException>(() =>
            client.QueryElementsAsync(FakeQueryElementsService.CreateValidRequest(), TimeSpan.FromSeconds(3), CancellationToken.None));

        Assert.Equal(BridgeErrorCodes.ProtocolIncompatible, exception.ErrorCode);
        Assert.Equal(0, query.InvokeCount);
    }

    [Fact]
    public async Task Named_pipe_query_returns_capability_result()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var expected = FakeQueryElementsService.CreateBoundedResult();
        var query = new FakeQueryElementsService { Result = expected };
        await using var context = await StartFullHostAsync(query: query);
        await using var client = await ConnectAndHandshakeAsync(context, [3, 2, 1]);
        var request = FakeQueryElementsService.CreateValidRequest();

        var result = await client.QueryElementsAsync(request, TimeSpan.FromSeconds(3), CancellationToken.None);

        Assert.Equal(1, query.InvokeCount);
        Assert.NotNull(query.LastRequest);
        Assert.Equal(request.Scope, query.LastRequest!.Scope);
        Assert.Equal(expected.Context.DocumentId, result.Context.DocumentId);
        Assert.Equal(expected.MatchedCount, result.MatchedCount);
        Assert.Equal(expected.Truncated, result.Truncated);
        Assert.Equal(expected.ElementRefs, result.ElementRefs);
    }

    [Fact]
    public async Task Query_capability_error_survives_streamjsonrpc_mapping()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var query = new FakeQueryElementsService
        {
            Error = new BridgeException(CapabilityErrorCodes.InvalidQuery, "The query request is invalid.")
        };
        await using var context = await StartFullHostAsync(query: query);
        await using var client = await ConnectAndHandshakeAsync(context, [3, 2, 1]);

        var exception = await Assert.ThrowsAsync<BridgeException>(() =>
            client.QueryElementsAsync(FakeQueryElementsService.CreateValidRequest(), TimeSpan.FromSeconds(3), CancellationToken.None));

        Assert.Equal(CapabilityErrorCodes.InvalidQuery, exception.ErrorCode);
        Assert.Equal("The query request is invalid.", exception.Message);
        Assert.DoesNotContain("stack", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Silent_query_peer_times_out_as_execution_timeout()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var query = new FakeQueryElementsService
        {
            Hold = new TaskCompletionSource<QueryElementsResult>(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        await using var context = await StartFullHostAsync(query: query);
        await using var client = await ConnectAndHandshakeAsync(context, [3, 2, 1]);

        var exception = await Assert.ThrowsAsync<BridgeException>(() =>
            client.QueryElementsAsync(FakeQueryElementsService.CreateValidRequest(), TimeSpan.FromMilliseconds(400), CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Equal(CapabilityErrorCodes.ExecutionTimeout, exception.ErrorCode);

        var reuse = await Assert.ThrowsAsync<BridgeException>(() =>
            client.QueryElementsAsync(FakeQueryElementsService.CreateValidRequest(), TimeSpan.FromSeconds(3), CancellationToken.None));
        Assert.Equal(CapabilityErrorCodes.ExecutionFailed, reuse.ErrorCode);
        Assert.Equal(1, query.InvokeCount);

        query.Hold.TrySetCanceled();
    }

    [Fact]
    public async Task Query_timeout_cancels_the_server_request_token()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var query = new FakeQueryElementsService
        {
            Hold = new TaskCompletionSource<QueryElementsResult>(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        await using var context = await StartFullHostAsync(query: query);
        await using var client = await ConnectAndHandshakeAsync(context, [3, 2, 1]);

        var exception = await Assert.ThrowsAsync<BridgeException>(() =>
            client.QueryElementsAsync(FakeQueryElementsService.CreateValidRequest(), TimeSpan.FromMilliseconds(400), CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Equal(CapabilityErrorCodes.ExecutionTimeout, exception.ErrorCode);
        await query.RequestTokenCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(query.RequestTokenCancelled.Task.IsCompletedSuccessfully);
        Assert.False(query.Hold!.Task.IsCompleted);
    }

    [Fact]
    public async Task Query_caller_cancellation_remains_cancellation()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var query = new FakeQueryElementsService
        {
            Hold = new TaskCompletionSource<QueryElementsResult>(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        await using var context = await StartFullHostAsync(query: query);
        await using var client = await ConnectAndHandshakeAsync(context, [3, 2, 1]);
        using var cts = new CancellationTokenSource();
        var pending = client.QueryElementsAsync(FakeQueryElementsService.CreateValidRequest(), TimeSpan.FromSeconds(10), cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
        query.Hold.TrySetCanceled();
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

    private static Task<HostContext> StartFullHostAsync(
        IRevitCapabilityService? capability = null,
        IRevitQueryElementsService? query = null)
    {
        return StartHostAsync(capability ?? new FakeCapabilityService(), query ?? new FakeQueryElementsService());
    }

    private static async Task<HostContext> StartHostAsync(
        IRevitCapabilityService? capability = null,
        IRevitQueryElementsService? query = null,
        BridgeInstanceMetadata? metadata = null)
    {
        metadata ??= CreateLiveMetadata();
        var root = Path.Combine(Path.GetTempPath(), "RevitMCP.Tests", Guid.NewGuid().ToString("N"));
        var store = new FileRegistrationStore(root);
        var host = await NamedPipeBridgeHost.StartAsync(metadata, store, capability, query, CancellationToken.None);
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
