using RevitMCP.Bridge;
using RevitMCP.Contracts;
using RevitMCP.Server;
using Xunit;

namespace RevitMCP.Server.Tests;

public sealed class QueryElementsBridgeOrchestrationTests
{
    [Fact]
    public async Task Selected_target_connects_to_its_registered_pipe()
    {
        var context = await InvokeReadyAsync("id-1", "revitmcp-pipe-1");

        Assert.True(context.Outcome.IsSuccess);
        Assert.Equal(new[] { "revitmcp-pipe-1" }, context.Factory.RequestedPipes);
    }

    [Fact]
    public async Task Handshake_sends_expected_instance_id_and_supported_versions()
    {
        var context = await InvokeReadyAsync("id-2", "revitmcp-pipe-2");

        var request = context.Client.HandshakeRequest;
        Assert.NotNull(request);
        Assert.Equal("id-2", request.ExpectedInstanceId);
        Assert.Equal(BridgeProtocol.SupportedVersions, request.SupportedProtocolVersions);
        Assert.Equal(new[] { 5, 4, 3, 2, 1 }, request.SupportedProtocolVersions);
        Assert.Equal("RevitMCP.Server", request.ClientName);
    }

    [Fact]
    public async Task Handshake_v4_executes_query()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("id-v4", "pipe-v4", protocolVersion: 4));
        var factory = Factory((pipe, _, _) =>
        {
            var registration = TestSupport.CreateRegistration("id-v4", pipe);
            return Task.FromResult(new RecordingBridgeClient
            {
                Handshake = (handshake, _) =>
                {
                    Assert.Equal(BridgeProtocol.SupportedVersions, handshake.SupportedProtocolVersions);
                    return Task.FromResult(TestSupport.CreateHandshake(registration, selectedProtocolVersion: 4));
                },
                QueryElements = (_, _, _) => Task.FromResult(TestSupport.CreateQueryResult("id-v4", "doc", 0, false))
            });
        });

        var outcome = await CreateService(discovery, factory).ExecuteAsync(null, TestSupport.CreateQueryRequest(), CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(1, factory.Clients[0].QueryElementsCalls);
    }

    [Fact]
    public async Task Handshake_v3_executes_query_with_exact_request()
    {
        var request = TestSupport.CreateQueryRequest(
            QueryScope.ActiveView,
            documentId: "doc-a",
            limit: 7,
            filters: new QueryElementFilters { CategoryNames = ["Ducts"], LevelNames = ["L2"] });
        var context = await InvokeReadyAsync("id-3", "pipe-3", request);

        Assert.True(context.Outcome.IsSuccess);
        Assert.Equal(1, context.Client.QueryElementsCalls);
        Assert.Equal(QueryScope.ActiveView, context.Client.LastQueryRequest!.Scope);
        Assert.Equal("doc-a", context.Client.LastQueryRequest.DocumentId);
        Assert.Equal(7, context.Client.LastQueryRequest.Limit);
        Assert.Equal(new[] { "Ducts" }, context.Client.LastQueryRequest.Filters.CategoryNames);
        Assert.Equal(new[] { "L2" }, context.Client.LastQueryRequest.Filters.LevelNames);
    }

    [Fact]
    public async Task Handshake_identity_mismatch_maps_to_unavailable()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("expected", "pipe-expected", protocolVersion: 3));
        var factory = Factory((_, _, _) => Task.FromResult(new RecordingBridgeClient
        {
            Handshake = (_, _) => Task.FromException<BridgeHandshakeResult>(
                new BridgeException(BridgeErrorCodes.IdentityMismatch, "identity mismatch")),
            QueryElements = (_, _, _) => throw new InvalidOperationException("Query must not run.")
        }));

        var outcome = await CreateService(discovery, factory).ExecuteAsync(
            "expected",
            TestSupport.CreateQueryRequest(),
            CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceUnavailable, outcome.ErrorCode);
        Assert.DoesNotContain("BRIDGE_IDENTITY_MISMATCH", outcome.ErrorCode);
        Assert.Equal(0, factory.Clients[0].QueryElementsCalls);
    }

    [Fact]
    public async Task Handshake_v2_maps_to_unavailable_for_query()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("id-v2", "pipe-v2", protocolVersion: 3));
        var factory = Factory((pipe, _, _) =>
        {
            var registration = TestSupport.CreateRegistration("id-v2", pipe);
            return Task.FromResult(new RecordingBridgeClient
            {
                Handshake = (_, _) => Task.FromResult(TestSupport.CreateHandshake(registration, selectedProtocolVersion: 2)),
                QueryElements = (_, _, _) => throw new InvalidOperationException("Query must not run for protocol v2.")
            });
        });

        var outcome = await CreateService(discovery, factory).ExecuteAsync(null, TestSupport.CreateQueryRequest(), CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceUnavailable, outcome.ErrorCode);
        Assert.Equal(0, factory.Clients[0].QueryElementsCalls);
    }

    [Fact]
    public async Task Explicit_target_failure_does_not_select_an_alternate_instance()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("alpha", "pipe-alpha", protocolVersion: 3));
        discovery.Instances.Add(TestSupport.Ready("beta", "pipe-beta", protocolVersion: 3));
        var factory = new RecordingBridgeClientFactory
        {
            Connect = (_, _, _) => throw new IOException("pipe closed")
        };

        var outcome = await CreateService(discovery, factory).ExecuteAsync(
            "alpha",
            TestSupport.CreateQueryRequest(),
            CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceUnavailable, outcome.ErrorCode);
        Assert.Equal(new[] { "pipe-alpha" }, factory.RequestedPipes);
    }

    [Fact]
    public async Task Capability_timeout_preserves_accepted_code()
    {
        var outcome = await InvokeQueryExceptionAsync(
            new BridgeException(CapabilityErrorCodes.ExecutionTimeout, "The Revit query timed out."));

        Assert.Equal(CapabilityErrorCodes.ExecutionTimeout, outcome.ErrorCode);
        Assert.Equal("The Revit query timed out.", outcome.ErrorMessage);
    }

    [Theory]
    [InlineData(CapabilityErrorCodes.DocumentContextChanged, "The active document no longer matches document_id.")]
    [InlineData(CapabilityErrorCodes.InvalidQuery, "The query request is invalid.")]
    [InlineData(CapabilityErrorCodes.NoActiveDocument, "No active document is available.")]
    [InlineData(CapabilityErrorCodes.NoActiveView, "The active view cannot be queried.")]
    [InlineData(CapabilityErrorCodes.ExecutionFailed, "The Revit query could not be executed.")]
    public async Task Accepted_capability_errors_are_preserved(string code, string message)
    {
        var outcome = await InvokeQueryExceptionAsync(new BridgeException(code, message));

        Assert.Equal(code, outcome.ErrorCode);
        Assert.Equal(message, outcome.ErrorMessage);
    }

    [Fact]
    public async Task Caller_cancellation_remains_cancellation()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("id-cancel", "pipe-cancel", protocolVersion: 3));
        using var cts = new CancellationTokenSource();
        var factory = Factory((_, _, _) => Task.FromResult(new RecordingBridgeClient
        {
            Handshake = async (_, token) =>
            {
                await cts.CancelAsync();
                token.ThrowIfCancellationRequested();
                return TestSupport.CreateHandshake(TestSupport.CreateRegistration("id-cancel", "pipe-cancel"), 3);
            },
            QueryElements = (_, _, _) => throw new InvalidOperationException("Query must not run after cancellation.")
        }));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateService(discovery, factory).ExecuteAsync(null, TestSupport.CreateQueryRequest(), cts.Token));
    }

    [Fact]
    public async Task Client_is_disposed_after_success_and_failure()
    {
        var success = await InvokeReadyAsync("id-ok", "pipe-ok");
        Assert.True(success.Client.Disposed);

        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("id-fail", "pipe-fail", protocolVersion: 3));
        var factory = Factory((_, _, _) => Task.FromResult(new RecordingBridgeClient
        {
            Handshake = (_, _) => Task.FromException<BridgeHandshakeResult>(
                new BridgeException(BridgeErrorCodes.HandshakeFailed, "failed")),
            QueryElements = (_, _, _) => throw new InvalidOperationException("Query must not run.")
        }));

        await CreateService(discovery, factory).ExecuteAsync(null, TestSupport.CreateQueryRequest(), CancellationToken.None);
        Assert.True(factory.Clients[0].Disposed);
    }

    [Fact]
    public void Application_service_does_not_reference_streamjsonrpc()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "RevitMCP.Server", "QueryElementsApplicationService.cs"));
        Assert.DoesNotContain("StreamJsonRpc", source, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonRpc", source, StringComparison.Ordinal);
    }

    private static async Task<InvocationContext> InvokeReadyAsync(
        string instanceId,
        string pipeName,
        QueryElementsRequest? request = null)
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready(instanceId, pipeName, protocolVersion: 3));
        var factory = Factory((pipe, _, _) =>
        {
            var registration = TestSupport.CreateRegistration(instanceId, pipe);
            return Task.FromResult(new RecordingBridgeClient
            {
                Handshake = (handshake, _) =>
                {
                    Assert.Equal(instanceId, handshake.ExpectedInstanceId);
                    return Task.FromResult(TestSupport.CreateHandshake(registration, selectedProtocolVersion: 3));
                },
                QueryElements = (_, _, _) => Task.FromResult(TestSupport.CreateQueryResult(instanceId, "doc", 0, false))
            });
        });

        var outcome = await CreateService(discovery, factory).ExecuteAsync(
            null,
            request ?? TestSupport.CreateQueryRequest(),
            CancellationToken.None);
        return new InvocationContext(outcome, factory, factory.Clients[0]);
    }

    private static async Task<QueryElementsOutcome> InvokeQueryExceptionAsync(BridgeException exception)
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("id-cap", "pipe-cap", protocolVersion: 3));
        var factory = Factory((_, _, _) => Task.FromResult(new RecordingBridgeClient
        {
            Handshake = (_, _) => Task.FromResult(TestSupport.CreateHandshake(TestSupport.CreateRegistration("id-cap", "pipe-cap"), 3)),
            QueryElements = (_, _, _) => Task.FromException<QueryElementsResult>(exception)
        }));

        return await CreateService(discovery, factory).ExecuteAsync(null, TestSupport.CreateQueryRequest(), CancellationToken.None);
    }

    private static QueryElementsApplicationService CreateService(
        FakeDiscovery discovery,
        RecordingBridgeClientFactory factory)
    {
        return new QueryElementsApplicationService(discovery, factory, new ServerTimeouts
        {
            BootstrapTimeout = TimeSpan.FromMilliseconds(40),
            CapabilityTimeout = TimeSpan.FromMilliseconds(60)
        });
    }

    private static RecordingBridgeClientFactory Factory(
        Func<string, TimeSpan, CancellationToken, Task<RecordingBridgeClient>> connect)
    {
        return new RecordingBridgeClientFactory { Connect = connect };
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "RevitMCP.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate RevitMCP.sln.");
    }

    private sealed record InvocationContext(
        QueryElementsOutcome Outcome,
        RecordingBridgeClientFactory Factory,
        RecordingBridgeClient Client);
}
