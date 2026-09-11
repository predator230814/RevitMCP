using RevitMCP.Bridge;
using RevitMCP.Contracts;
using RevitMCP.Server;
using Xunit;

namespace RevitMCP.Server.Tests;

public sealed class GetElementsBridgeOrchestrationTests
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
        Assert.Equal(new[] { 4, 3, 2, 1 }, request.SupportedProtocolVersions);
        Assert.Equal("RevitMCP.Server", request.ClientName);
    }

    [Fact]
    public async Task Handshake_v4_executes_get_elements_with_exact_request()
    {
        var request = TestSupport.CreateGetElementsRequest(
            "doc-a",
            ["ref-b", "ref-a"],
            new GetElementsProjection
            {
                Fields = [GetElementField.Name, GetElementField.LevelName],
                ParameterNames = ["Mark"]
            });
        var context = await InvokeReadyAsync("id-v4", "pipe-v4", request);

        Assert.True(context.Outcome.IsSuccess);
        Assert.Equal(1, context.Client.GetElementsCalls);
        Assert.Same(request, context.Client.LastGetElementsRequest);
        Assert.Equal("doc-a", context.Client.LastGetElementsRequest!.DocumentId);
        Assert.Equal(new[] { "ref-b", "ref-a" }, context.Client.LastGetElementsRequest.ElementRefs);
        Assert.Equal(
            new[] { GetElementField.Name, GetElementField.LevelName },
            context.Client.LastGetElementsRequest.Projection.Fields);
        Assert.Equal(new[] { "Mark" }, context.Client.LastGetElementsRequest.Projection.ParameterNames);
        Assert.Null(typeof(GetElementsRequest).GetProperty("InstanceId"));
    }

    [Fact]
    public async Task Handshake_v3_maps_to_unavailable_for_get_elements()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("id-v3", "pipe-v3", protocolVersion: 4));
        var factory = Factory((pipe, _, _) =>
        {
            var registration = TestSupport.CreateRegistration("id-v3", pipe);
            return Task.FromResult(new RecordingBridgeClient
            {
                Handshake = (_, _) => Task.FromResult(TestSupport.CreateHandshake(registration, selectedProtocolVersion: 3)),
                GetElements = (_, _, _) => throw new InvalidOperationException("GetElements must not run for protocol v3.")
            });
        });

        var outcome = await CreateService(discovery, factory).ExecuteAsync(null, TestSupport.CreateGetElementsRequest(), CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceUnavailable, outcome.ErrorCode);
        Assert.Equal(0, factory.Clients[0].GetElementsCalls);
    }

    [Fact]
    public async Task Handshake_unknown_v5_maps_to_unavailable()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("id-v5", "pipe-v5", protocolVersion: 4));
        var factory = Factory((pipe, _, _) =>
        {
            var registration = TestSupport.CreateRegistration("id-v5", pipe);
            return Task.FromResult(new RecordingBridgeClient
            {
                Handshake = (_, _) => Task.FromResult(TestSupport.CreateHandshake(registration, selectedProtocolVersion: 5)),
                GetElements = (_, _, _) => throw new InvalidOperationException("GetElements must not run for unknown v5.")
            });
        });

        var outcome = await CreateService(discovery, factory).ExecuteAsync(null, TestSupport.CreateGetElementsRequest(), CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceUnavailable, outcome.ErrorCode);
        Assert.Equal(0, factory.Clients[0].GetElementsCalls);
    }

    [Fact]
    public async Task Handshake_identity_mismatch_maps_to_unavailable()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("expected", "pipe-expected", protocolVersion: 4));
        var factory = Factory((_, _, _) => Task.FromResult(new RecordingBridgeClient
        {
            Handshake = (_, _) => Task.FromException<BridgeHandshakeResult>(
                new BridgeException(BridgeErrorCodes.IdentityMismatch, "identity mismatch")),
            GetElements = (_, _, _) => throw new InvalidOperationException("GetElements must not run.")
        }));

        var outcome = await CreateService(discovery, factory).ExecuteAsync(
            "expected",
            TestSupport.CreateGetElementsRequest(),
            CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceUnavailable, outcome.ErrorCode);
        Assert.DoesNotContain("BRIDGE_IDENTITY_MISMATCH", outcome.ErrorCode);
        Assert.Equal(0, factory.Clients[0].GetElementsCalls);
    }

    [Fact]
    public async Task Explicit_target_failure_does_not_select_an_alternate_instance()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("alpha", "pipe-alpha", protocolVersion: 4));
        discovery.Instances.Add(TestSupport.Ready("beta", "pipe-beta", protocolVersion: 4));
        var factory = new RecordingBridgeClientFactory
        {
            Connect = (_, _, _) => throw new IOException("pipe closed")
        };

        var outcome = await CreateService(discovery, factory).ExecuteAsync(
            "alpha",
            TestSupport.CreateGetElementsRequest(),
            CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceUnavailable, outcome.ErrorCode);
        Assert.Equal(new[] { "pipe-alpha" }, factory.RequestedPipes);
    }

    [Fact]
    public async Task Partial_not_found_result_is_preserved_in_order()
    {
        var request = TestSupport.CreateGetElementsRequest("doc-1", ["valid-ref", "not-a-revit-element-ref"]);
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("id-partial", "pipe-partial", protocolVersion: 4));
        var factory = Factory((_, _, _) => Task.FromResult(new RecordingBridgeClient
        {
            Handshake = (_, _) => Task.FromResult(
                TestSupport.CreateHandshake(TestSupport.CreateRegistration("id-partial", "pipe-partial"), 4)),
            GetElements = (_, _, _) => Task.FromResult(TestSupport.CreateGetElementsResult(
                "id-partial",
                "doc-1",
                TestSupport.CreateOkElement("valid-ref", ProjectedString.Of("HRU")),
                TestSupport.CreateNotFoundElement("not-a-revit-element-ref")))
        }));

        var outcome = await CreateService(discovery, factory).ExecuteAsync(null, request, CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(2, outcome.Result!.Elements.Count);
        Assert.Equal("valid-ref", outcome.Result.Elements[0].ElementRef);
        Assert.Equal(GetElementResultStatus.Ok, outcome.Result.Elements[0].Status);
        Assert.Equal("HRU", outcome.Result.Elements[0].Name.Value);
        Assert.False(outcome.Result.Elements[0].CategoryName.IsRequested);
        Assert.Equal("not-a-revit-element-ref", outcome.Result.Elements[1].ElementRef);
        Assert.Equal(GetElementResultStatus.NotFound, outcome.Result.Elements[1].Status);
        Assert.False(outcome.Result.Elements[1].Name.IsRequested);
        Assert.Null(outcome.Result.Elements[1].Parameters);
    }

    [Fact]
    public async Task Capability_timeout_preserves_accepted_code()
    {
        var outcome = await InvokeGetElementsExceptionAsync(
            new BridgeException(CapabilityErrorCodes.ExecutionTimeout, "The Revit inspection request timed out."));

        Assert.Equal(CapabilityErrorCodes.ExecutionTimeout, outcome.ErrorCode);
        Assert.Equal("The Revit inspection request timed out.", outcome.ErrorMessage);
    }

    [Theory]
    [InlineData(CapabilityErrorCodes.DocumentContextChanged, "The supplied document_id does not match the active document.")]
    [InlineData(CapabilityErrorCodes.InvalidInspection, "The element inspection request is invalid.")]
    [InlineData(CapabilityErrorCodes.NoActiveDocument, "No active Revit document is available.")]
    [InlineData(CapabilityErrorCodes.ExecutionFailed, "The Revit inspection could not be executed.")]
    public async Task Accepted_capability_errors_are_preserved(string code, string message)
    {
        var outcome = await InvokeGetElementsExceptionAsync(new BridgeException(code, message));

        Assert.Equal(code, outcome.ErrorCode);
        Assert.Equal(message, outcome.ErrorMessage);
    }

    [Fact]
    public async Task Caller_cancellation_remains_cancellation()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("id-cancel", "pipe-cancel", protocolVersion: 4));
        using var cts = new CancellationTokenSource();
        var factory = Factory((_, _, _) => Task.FromResult(new RecordingBridgeClient
        {
            Handshake = async (_, token) =>
            {
                await cts.CancelAsync();
                token.ThrowIfCancellationRequested();
                return TestSupport.CreateHandshake(TestSupport.CreateRegistration("id-cancel", "pipe-cancel"), 4);
            },
            GetElements = (_, _, _) => throw new InvalidOperationException("GetElements must not run after cancellation.")
        }));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateService(discovery, factory).ExecuteAsync(null, TestSupport.CreateGetElementsRequest(), cts.Token));
    }

    [Fact]
    public async Task Client_is_disposed_after_success_and_failure()
    {
        var success = await InvokeReadyAsync("id-ok", "pipe-ok");
        Assert.True(success.Client.Disposed);

        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("id-fail", "pipe-fail", protocolVersion: 4));
        var factory = Factory((_, _, _) => Task.FromResult(new RecordingBridgeClient
        {
            Handshake = (_, _) => Task.FromException<BridgeHandshakeResult>(
                new BridgeException(BridgeErrorCodes.HandshakeFailed, "failed")),
            GetElements = (_, _, _) => throw new InvalidOperationException("GetElements must not run.")
        }));

        await CreateService(discovery, factory).ExecuteAsync(null, TestSupport.CreateGetElementsRequest(), CancellationToken.None);
        Assert.True(factory.Clients[0].Disposed);
    }

    [Fact]
    public void Application_service_does_not_reference_streamjsonrpc()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "RevitMCP.Server", "GetElementsApplicationService.cs"));
        Assert.DoesNotContain("StreamJsonRpc", source, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonRpc", source, StringComparison.Ordinal);
    }

    private static async Task<InvocationContext> InvokeReadyAsync(
        string instanceId,
        string pipeName,
        GetElementsRequest? request = null)
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready(instanceId, pipeName, protocolVersion: 4));
        var factory = Factory((pipe, _, _) =>
        {
            var registration = TestSupport.CreateRegistration(instanceId, pipe);
            return Task.FromResult(new RecordingBridgeClient
            {
                Handshake = (handshake, _) =>
                {
                    Assert.Equal(instanceId, handshake.ExpectedInstanceId);
                    return Task.FromResult(TestSupport.CreateHandshake(registration, selectedProtocolVersion: 4));
                },
                GetElements = (inspection, _, _) => Task.FromResult(TestSupport.CreateGetElementsResult(
                    instanceId,
                    inspection.DocumentId,
                    TestSupport.CreateOkElement(inspection.ElementRefs[0], ProjectedString.Of("HRU"))))
            });
        });

        var outcome = await CreateService(discovery, factory).ExecuteAsync(
            null,
            request ?? TestSupport.CreateGetElementsRequest(),
            CancellationToken.None);
        return new InvocationContext(outcome, factory, factory.Clients[0]);
    }

    private static async Task<GetElementsOutcome> InvokeGetElementsExceptionAsync(BridgeException exception)
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("id-cap", "pipe-cap", protocolVersion: 4));
        var factory = Factory((_, _, _) => Task.FromResult(new RecordingBridgeClient
        {
            Handshake = (_, _) => Task.FromResult(TestSupport.CreateHandshake(TestSupport.CreateRegistration("id-cap", "pipe-cap"), 4)),
            GetElements = (_, _, _) => Task.FromException<GetElementsResult>(exception)
        }));

        return await CreateService(discovery, factory).ExecuteAsync(null, TestSupport.CreateGetElementsRequest(), CancellationToken.None);
    }

    private static GetElementsApplicationService CreateService(
        FakeDiscovery discovery,
        RecordingBridgeClientFactory factory)
    {
        return new GetElementsApplicationService(discovery, factory, new ServerTimeouts
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
        GetElementsOutcome Outcome,
        RecordingBridgeClientFactory Factory,
        RecordingBridgeClient Client);
}
