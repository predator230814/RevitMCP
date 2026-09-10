using RevitMCP.Bridge;
using RevitMCP.Contracts;
using RevitMCP.Server;
using Xunit;

namespace RevitMCP.Server.Tests;

public sealed class GetContextBridgeOrchestrationTests
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
        Assert.Equal(new[] { 3, 2, 1 }, request.SupportedProtocolVersions);
        Assert.Equal("RevitMCP.Server", request.ClientName);
    }

    [Fact]
    public async Task Protocol_v2_permits_get_context()
    {
        var context = await InvokeReadyAsync("id-3", "revitmcp-pipe-3");

        Assert.True(context.Outcome.IsSuccess);
        Assert.Equal(1, context.Client.GetContextCalls);
    }

    [Fact]
    public async Task Protocol_v3_permits_get_context()
    {
        var context = await InvokeReadyAsync("id-v3", "revitmcp-pipe-v3", selectedProtocolVersion: 3);

        Assert.True(context.Outcome.IsSuccess);
        Assert.Equal(1, context.Client.GetContextCalls);
    }

    [Fact]
    public async Task Unknown_v4_does_not_permit_get_context()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("id-v4", "pipe-v4"));
        var factory = Factory((pipe, _, _) =>
        {
            var registration = TestSupport.CreateRegistration("id-v4", pipe);
            return Task.FromResult(new RecordingBridgeClient
            {
                Handshake = (_, _) => Task.FromResult(TestSupport.CreateHandshake(registration, selectedProtocolVersion: 4)),
                GetContext = (_, _, _) => throw new InvalidOperationException("GetContext must not run for protocol v4.")
            });
        });

        var outcome = await CreateService(discovery, factory).ExecuteAsync(null, CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceUnavailable, outcome.ErrorCode);
        Assert.Equal(0, factory.Clients[0].GetContextCalls);
    }

    [Fact]
    public async Task Fresh_invocation_protocol_v1_maps_to_unavailable()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("id-v1", "pipe-v1"));
        var factory = Factory((pipe, _, _) =>
        {
            var registration = TestSupport.CreateRegistration("id-v1", pipe);
            return Task.FromResult(new RecordingBridgeClient
            {
                Handshake = (_, _) => Task.FromResult(TestSupport.CreateHandshake(registration, selectedProtocolVersion: 1)),
                GetContext = (_, _, _) => throw new InvalidOperationException("GetContext must not run for protocol v1.")
            });
        });

        var outcome = await CreateService(discovery, factory).ExecuteAsync(null, CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceUnavailable, outcome.ErrorCode);
        Assert.Equal(0, factory.Clients[0].GetContextCalls);
    }

    [Fact]
    public async Task Bridge_connection_failure_maps_to_unavailable()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("id-4", "pipe-4"));
        var factory = Factory((_, _, _) => throw new IOException("connection refused"));

        var outcome = await CreateService(discovery, factory).ExecuteAsync(null, CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceUnavailable, outcome.ErrorCode);
    }

    [Fact]
    public async Task Identity_mismatch_maps_to_unavailable()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("expected", "pipe-expected"));
        var factory = Factory((_, _, _) => Task.FromResult(new RecordingBridgeClient
        {
            Handshake = (_, _) => Task.FromException<BridgeHandshakeResult>(
                new BridgeException(BridgeErrorCodes.IdentityMismatch, "identity mismatch")),
            GetContext = (_, _, _) => throw new InvalidOperationException("GetContext must not run.")
        }));

        var outcome = await CreateService(discovery, factory).ExecuteAsync("expected", CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceUnavailable, outcome.ErrorCode);
        Assert.DoesNotContain("BRIDGE_IDENTITY_MISMATCH", outcome.ErrorCode);
        Assert.DoesNotContain("identity mismatch", outcome.ErrorMessage);
    }

    [Fact]
    public async Task Handshake_timeout_and_failure_map_to_unavailable()
    {
        foreach (var code in new[] { BridgeErrorCodes.HandshakeTimeout, BridgeErrorCodes.HandshakeFailed })
        {
            var discovery = new FakeDiscovery();
            discovery.Instances.Add(TestSupport.Ready("id-hs", "pipe-hs"));
            var factory = Factory((_, _, _) => Task.FromResult(new RecordingBridgeClient
            {
                Handshake = (_, _) => Task.FromException<BridgeHandshakeResult>(
                    new BridgeException(code, "handshake diagnostic")),
                GetContext = (_, _, _) => throw new InvalidOperationException("GetContext must not run.")
            }));

            var outcome = await CreateService(discovery, factory).ExecuteAsync(null, CancellationToken.None);

            Assert.Equal(McpToolErrorCodes.InstanceUnavailable, outcome.ErrorCode);
            Assert.DoesNotContain("BRIDGE_", outcome.ErrorCode);
            Assert.DoesNotContain("handshake diagnostic", outcome.ErrorMessage);
        }
    }

    [Fact]
    public async Task Capability_timeout_survives_as_revit_execution_timeout()
    {
        var outcome = await InvokeGetContextExceptionAsync(
            new BridgeException(CapabilityErrorCodes.ExecutionTimeout, "The Revit context request timed out."));

        Assert.Equal(McpToolErrorCodes.ExecutionTimeout, outcome.ErrorCode);
        Assert.Equal(ToolErrorMessages.ExecutionTimeout, outcome.ErrorMessage);
    }

    [Fact]
    public async Task Capability_failure_survives_as_revit_execution_failed()
    {
        var outcome = await InvokeGetContextExceptionAsync(
            new BridgeException(CapabilityErrorCodes.ExecutionFailed, "The Revit context could not be collected."));

        Assert.Equal(McpToolErrorCodes.ExecutionFailed, outcome.ErrorCode);
        Assert.Equal(ToolErrorMessages.ExecutionFailed, outcome.ErrorMessage);
    }

    [Fact]
    public async Task Caller_cancellation_remains_cancellation()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("id-cancel", "pipe-cancel"));
        using var cts = new CancellationTokenSource();
        var factory = Factory((_, _, _) => Task.FromResult(new RecordingBridgeClient
        {
            Handshake = async (_, token) =>
            {
                await cts.CancelAsync();
                token.ThrowIfCancellationRequested();
                return TestSupport.CreateHandshake(TestSupport.CreateRegistration("id-cancel", "pipe-cancel"));
            },
            GetContext = (_, _, _) => throw new InvalidOperationException("GetContext must not run after cancellation.")
        }));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateService(discovery, factory).ExecuteAsync(null, cts.Token));

        Assert.True(cts.IsCancellationRequested);
    }

    [Fact]
    public async Task Client_is_disposed_after_successful_invocation()
    {
        var context = await InvokeReadyAsync("id-dispose", "pipe-dispose");

        Assert.True(context.Outcome.IsSuccess);
        Assert.True(context.Client.Disposed);
    }

    [Fact]
    public async Task Client_is_disposed_after_failed_handshake()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("id-fail", "pipe-fail"));
        var factory = Factory((_, _, _) => Task.FromResult(new RecordingBridgeClient
        {
            Handshake = (_, _) => Task.FromException<BridgeHandshakeResult>(
                new BridgeException(BridgeErrorCodes.HandshakeFailed, "failed")),
            GetContext = (_, _, _) => throw new InvalidOperationException("GetContext must not run.")
        }));

        await CreateService(discovery, factory).ExecuteAsync(null, CancellationToken.None);

        Assert.True(factory.Clients[0].Disposed);
    }

    private static async Task<InvocationContext> InvokeReadyAsync(
        string instanceId,
        string pipeName,
        int selectedProtocolVersion = BridgeProtocol.GetContextVersion)
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready(instanceId, pipeName, selectedProtocolVersion));
        var factory = Factory((pipe, _, _) =>
        {
            var registration = TestSupport.CreateRegistration(instanceId, pipe);
            return Task.FromResult(new RecordingBridgeClient
            {
                Handshake = (request, _) =>
                {
                    Assert.Equal(instanceId, request.ExpectedInstanceId);
                    return Task.FromResult(TestSupport.CreateHandshake(registration, selectedProtocolVersion));
                },
                GetContext = (_, _, _) => Task.FromResult(TestSupport.ZeroDocument(instanceId))
            });
        });

        var outcome = await CreateService(discovery, factory).ExecuteAsync(null, CancellationToken.None);
        return new InvocationContext(outcome, factory, factory.Clients[0]);
    }

    private static async Task<GetContextOutcome> InvokeGetContextExceptionAsync(BridgeException exception)
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("id-cap", "pipe-cap"));
        var factory = Factory((_, _, _) => Task.FromResult(new RecordingBridgeClient
        {
            Handshake = (_, _) => Task.FromResult(TestSupport.CreateHandshake(TestSupport.CreateRegistration("id-cap", "pipe-cap"))),
            GetContext = (_, _, _) => Task.FromException<GetContextResult>(exception)
        }));

        return await CreateService(discovery, factory).ExecuteAsync(null, CancellationToken.None);
    }

    private static GetContextApplicationService CreateService(
        FakeDiscovery discovery,
        RecordingBridgeClientFactory factory)
    {
        return new GetContextApplicationService(discovery, factory, new ServerTimeouts
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

    private sealed record InvocationContext(
        GetContextOutcome Outcome,
        RecordingBridgeClientFactory Factory,
        RecordingBridgeClient Client);
}
