using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;
using BimIt.Revit.Server;
using Xunit;

namespace BimIt.Revit.Server.Tests;

public sealed class GetSelectionBridgeOrchestrationTests
{
    [Fact]
    public async Task Fresh_discovery_connects_only_the_selected_pipe_and_preserves_the_result()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("id-1", "pipe-1", protocolVersion: 13));
        var returned = GetSelectionRoutingTests.SelectionResult("id-1", "z-ref", "a-ref");
        returned = new GetSelectionResult
        {
            Context = returned.Context,
            SelectedCount = 4,
            Truncated = true,
            ElementRefs = ["z-ref", "a-ref"]
        };
        var factory = Factory("id-1", "pipe-1", returned);
        var service = CreateService(discovery, factory);
        var request = new GetSelectionRequest
        {
            DocumentId = "  ",
            Limit = null
        };

        var first = await service.ExecuteAsync(null, request, CancellationToken.None);
        var second = await service.ExecuteAsync("id-1", request, CancellationToken.None);

        Assert.Equal(2, discovery.CallCount);
        Assert.Equal(["pipe-1", "pipe-1"], factory.RequestedPipes);
        Assert.Equal(2, factory.Clients.Count);
        Assert.All(factory.Clients, client =>
        {
            Assert.Equal(BridgeProtocol.SupportedVersions, client.HandshakeRequest!.SupportedProtocolVersions);
            Assert.Equal("id-1", client.HandshakeRequest.ExpectedInstanceId);
            Assert.Equal(1, client.GetSelectionCalls);
            Assert.Equal("  ", client.LastGetSelectionRequest!.DocumentId);
            Assert.Null(client.LastGetSelectionRequest.Limit);
            Assert.Equal(TimeSpan.FromMilliseconds(75), client.LastGetSelectionTimeout);
            Assert.True(client.Disposed);
        });
        Assert.Same(returned, first.Result);
        Assert.Equal(["z-ref", "a-ref"], second.Result!.ElementRefs);
        Assert.Equal(4, second.Result.SelectedCount);
        Assert.True(second.Result.Truncated);
    }

    [Fact]
    public async Task Selected_protocol_must_support_get_selection()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("id-1", "pipe-1", protocolVersion: 13));
        var factory = Factory("id-1", "pipe-1", GetSelectionRoutingTests.SelectionResult("id-1"), selectedProtocolVersion: 12);

        var outcome = await CreateService(discovery, factory).ExecuteAsync(null, new GetSelectionRequest(), CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceUnavailable, outcome.ErrorCode);
        Assert.Equal(0, factory.Clients[0].GetSelectionCalls);
    }

    [Fact]
    public async Task Identity_mismatch_maps_to_unavailable()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("expected", "pipe-expected", protocolVersion: 13));
        var factory = new RecordingBridgeClientFactory
        {
            Connect = (_, _, _) => Task.FromResult(new RecordingBridgeClient
            {
                Handshake = (_, _) => Task.FromResult(TestSupport.CreateHandshake(
                    TestSupport.CreateRegistration("other", "pipe-expected"),
                    selectedProtocolVersion: 13))
            })
        };

        var outcome = await CreateService(discovery, factory).ExecuteAsync(null, new GetSelectionRequest(), CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceUnavailable, outcome.ErrorCode);
        Assert.Equal(0, factory.Clients[0].GetSelectionCalls);
    }

    [Theory]
    [InlineData(CapabilityErrorCodes.NoActiveDocument, "No active Revit document is available.")]
    [InlineData(CapabilityErrorCodes.DocumentContextChanged, "The supplied document_id does not match the active document.")]
    [InlineData(CapabilityErrorCodes.InvalidSelection, "The selection request is invalid.")]
    [InlineData(CapabilityErrorCodes.ExecutionTimeout, "The Revit selection request timed out.")]
    [InlineData(CapabilityErrorCodes.ExecutionFailed, "The Revit selection request could not be executed.")]
    public async Task Accepted_capability_errors_survive(string code, string message)
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("id-1", "pipe-1", protocolVersion: 13));
        var factory = new RecordingBridgeClientFactory
        {
            Connect = (pipe, _, _) =>
            {
                var registration = TestSupport.CreateRegistration("id-1", pipe);
                return Task.FromResult(new RecordingBridgeClient
                {
                    Handshake = (_, _) => Task.FromResult(TestSupport.CreateHandshake(registration, selectedProtocolVersion: 13)),
                    GetSelection = (_, _, _) => throw new BridgeException(code, message)
                });
            }
        };

        var outcome = await CreateService(discovery, factory).ExecuteAsync(null, new GetSelectionRequest(), CancellationToken.None);

        Assert.Equal(code, outcome.ErrorCode);
        Assert.Equal(message, outcome.ErrorMessage);
    }

    [Fact]
    public async Task Io_failure_maps_to_unavailable()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("id-1", "pipe-1", protocolVersion: 13));
        var factory = new RecordingBridgeClientFactory
        {
            Connect = (_, _, _) => throw new IOException("connection refused")
        };

        var outcome = await CreateService(discovery, factory).ExecuteAsync(null, new GetSelectionRequest(), CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceUnavailable, outcome.ErrorCode);
        Assert.Equal(ToolErrorMessages.InstanceUnavailable, outcome.ErrorMessage);
    }

    [Fact]
    public async Task Caller_cancellation_remains_cancellation()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("id-1", "pipe-1", protocolVersion: 13));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var factory = new RecordingBridgeClientFactory
        {
            Connect = (_, _, token) => Task.FromCanceled<RecordingBridgeClient>(token)
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateService(discovery, factory).ExecuteAsync(null, new GetSelectionRequest(), cts.Token));
    }

    private static GetSelectionApplicationService CreateService(FakeDiscovery discovery, RecordingBridgeClientFactory factory)
    {
        return new GetSelectionApplicationService(discovery, factory, new ServerTimeouts
        {
            BootstrapTimeout = TimeSpan.FromMilliseconds(50),
            CapabilityTimeout = TimeSpan.FromMilliseconds(75)
        });
    }

    private static RecordingBridgeClientFactory Factory(
        string instanceId,
        string pipeName,
        GetSelectionResult result,
        int selectedProtocolVersion = 13)
    {
        return new RecordingBridgeClientFactory
        {
            Connect = (pipe, _, _) =>
            {
                Assert.Equal(pipeName, pipe);
                var registration = TestSupport.CreateRegistration(instanceId, pipe);
                return Task.FromResult(new RecordingBridgeClient
                {
                    Handshake = (_, _) => Task.FromResult(TestSupport.CreateHandshake(registration, selectedProtocolVersion)),
                    GetSelection = (_, _, _) => Task.FromResult(result)
                });
            }
        };
    }
}
