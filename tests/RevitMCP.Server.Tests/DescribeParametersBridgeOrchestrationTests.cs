using RevitMCP.Bridge;
using RevitMCP.Contracts;
using RevitMCP.Server;
using Xunit;

namespace RevitMCP.Server.Tests;

public sealed class DescribeParametersBridgeOrchestrationTests
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
        Assert.Equal(new[] { 6, 5, 4, 3, 2, 1 }, request.SupportedProtocolVersions);
        Assert.Equal("RevitMCP.Server", request.ClientName);
    }

    [Fact]
    public async Task Handshake_v5_executes_describe_parameters_with_exact_request()
    {
        var request = TestSupport.CreateDescribeParametersRequest(
            "doc-a",
            ["ref-b", "ref-a"],
            DescribeParameterSource.Type,
            "Flow",
            25);
        var context = await InvokeReadyAsync("id-v5", "pipe-v5", request);

        Assert.True(context.Outcome.IsSuccess);
        Assert.Equal(1, context.Client.DescribeParametersCalls);
        Assert.Same(request, context.Client.LastDescribeParametersRequest);
        Assert.Equal("doc-a", context.Client.LastDescribeParametersRequest!.DocumentId);
        Assert.Equal(new[] { "ref-b", "ref-a" }, context.Client.LastDescribeParametersRequest.ElementRefs);
        Assert.Equal(DescribeParameterSource.Type, context.Client.LastDescribeParametersRequest.Source);
        Assert.Equal("Flow", context.Client.LastDescribeParametersRequest.NameContains);
        Assert.Equal(25, context.Client.LastDescribeParametersRequest.Limit);
        Assert.Null(typeof(DescribeParametersRequest).GetProperty("InstanceId"));
    }

    [Fact]
    public async Task Handshake_v4_maps_to_unavailable_for_describe_parameters()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("id-v4", "pipe-v4", protocolVersion: 5));
        var factory = Factory((pipe, _, _) =>
        {
            var registration = TestSupport.CreateRegistration("id-v4", pipe);
            return Task.FromResult(new RecordingBridgeClient
            {
                Handshake = (_, _) => Task.FromResult(TestSupport.CreateHandshake(registration, selectedProtocolVersion: 4)),
                DescribeParameters = (_, _, _) => throw new InvalidOperationException("DescribeParameters must not run for protocol v4.")
            });
        });

        var outcome = await CreateService(discovery, factory).ExecuteAsync(null, TestSupport.CreateDescribeParametersRequest(), CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceUnavailable, outcome.ErrorCode);
        Assert.Equal(0, factory.Clients[0].DescribeParametersCalls);
    }

    [Fact]
    public async Task Handshake_unknown_v7_maps_to_unavailable()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("id-v7", "pipe-v7", protocolVersion: 5));
        var factory = Factory((pipe, _, _) =>
        {
            var registration = TestSupport.CreateRegistration("id-v7", pipe);
            return Task.FromResult(new RecordingBridgeClient
            {
                Handshake = (_, _) => Task.FromResult(TestSupport.CreateHandshake(registration, selectedProtocolVersion: 7)),
                DescribeParameters = (_, _, _) => throw new InvalidOperationException("DescribeParameters must not run for unknown v7.")
            });
        });

        var outcome = await CreateService(discovery, factory).ExecuteAsync(null, TestSupport.CreateDescribeParametersRequest(), CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceUnavailable, outcome.ErrorCode);
        Assert.Equal(0, factory.Clients[0].DescribeParametersCalls);
    }

    [Fact]
    public async Task Handshake_identity_mismatch_maps_to_unavailable()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("expected", "pipe-expected", protocolVersion: 5));
        var factory = Factory((_, _, _) => Task.FromResult(new RecordingBridgeClient
        {
            Handshake = (_, _) => Task.FromException<BridgeHandshakeResult>(
                new BridgeException(BridgeErrorCodes.IdentityMismatch, "identity mismatch")),
            DescribeParameters = (_, _, _) => throw new InvalidOperationException("DescribeParameters must not run.")
        }));

        var outcome = await CreateService(discovery, factory).ExecuteAsync(
            "expected",
            TestSupport.CreateDescribeParametersRequest(),
            CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceUnavailable, outcome.ErrorCode);
        Assert.DoesNotContain("BRIDGE_IDENTITY_MISMATCH", outcome.ErrorCode);
        Assert.Equal(0, factory.Clients[0].DescribeParametersCalls);
    }

    [Fact]
    public async Task Explicit_target_failure_does_not_select_an_alternate_instance()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("alpha", "pipe-alpha", protocolVersion: 5));
        discovery.Instances.Add(TestSupport.Ready("beta", "pipe-beta", protocolVersion: 5));
        var factory = new RecordingBridgeClientFactory
        {
            Connect = (_, _, _) => throw new IOException("pipe closed")
        };

        var outcome = await CreateService(discovery, factory).ExecuteAsync(
            "alpha",
            TestSupport.CreateDescribeParametersRequest(),
            CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceUnavailable, outcome.ErrorCode);
        Assert.Equal(new[] { "pipe-alpha" }, factory.RequestedPipes);
    }

    [Fact]
    public async Task Partial_not_found_result_is_preserved_in_order()
    {
        var request = TestSupport.CreateDescribeParametersRequest("doc-1", ["valid-ref", "not-a-revit-element-ref"]);
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("id-partial", "pipe-partial", protocolVersion: 5));
        var factory = Factory((_, _, _) => Task.FromResult(new RecordingBridgeClient
        {
            Handshake = (_, _) => Task.FromResult(
                TestSupport.CreateHandshake(TestSupport.CreateRegistration("id-partial", "pipe-partial"), 5)),
            DescribeParameters = (_, _, _) => Task.FromResult(TestSupport.CreateDescribeParametersResult(
                "id-partial",
                "doc-1",
                [TestSupport.CreateDescribeOkElement("valid-ref"), TestSupport.CreateDescribeNotFoundElement("not-a-revit-element-ref")],
                matchedCount: 1,
                truncated: false,
                TestSupport.CreateBuiltInDescriptor()))
        }));

        var outcome = await CreateService(discovery, factory).ExecuteAsync(null, request, CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(2, outcome.Result!.Elements.Count);
        Assert.Equal("valid-ref", outcome.Result.Elements[0].ElementRef);
        Assert.Equal(GetElementResultStatus.Ok, outcome.Result.Elements[0].Status);
        Assert.Equal("not-a-revit-element-ref", outcome.Result.Elements[1].ElementRef);
        Assert.Equal(GetElementResultStatus.NotFound, outcome.Result.Elements[1].Status);
        Assert.Single(outcome.Result.Parameters);
    }

    [Fact]
    public async Task Capability_timeout_preserves_accepted_code()
    {
        var outcome = await InvokeDescribeExceptionAsync(
            new BridgeException(CapabilityErrorCodes.ExecutionTimeout, "The Revit inspection request timed out."));

        Assert.Equal(CapabilityErrorCodes.ExecutionTimeout, outcome.ErrorCode);
        Assert.Equal("The Revit inspection request timed out.", outcome.ErrorMessage);
    }

    [Theory]
    [InlineData(CapabilityErrorCodes.DocumentContextChanged, "The supplied document_id does not match the active document.")]
    [InlineData(CapabilityErrorCodes.InvalidParameterDiscovery, "The parameter discovery request is invalid.")]
    [InlineData(CapabilityErrorCodes.NoActiveDocument, "No active Revit document is available.")]
    [InlineData(CapabilityErrorCodes.ExecutionFailed, "The Revit inspection could not be executed.")]
    public async Task Accepted_capability_errors_are_preserved(string code, string message)
    {
        var outcome = await InvokeDescribeExceptionAsync(new BridgeException(code, message));

        Assert.Equal(code, outcome.ErrorCode);
        Assert.Equal(message, outcome.ErrorMessage);
    }

    [Fact]
    public async Task Document_context_changed_does_not_retry()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("alpha", "pipe-alpha", protocolVersion: 5));
        discovery.Instances.Add(TestSupport.Ready("beta", "pipe-beta", protocolVersion: 5));
        var factory = Factory((_, _, _) => Task.FromResult(new RecordingBridgeClient
        {
            Handshake = (_, _) => Task.FromResult(
                TestSupport.CreateHandshake(TestSupport.CreateRegistration("alpha", "pipe-alpha"), 5)),
            DescribeParameters = (_, _, _) => Task.FromException<DescribeParametersResult>(
                new BridgeException(CapabilityErrorCodes.DocumentContextChanged, "The supplied document_id does not match the active document."))
        }));

        var outcome = await CreateService(discovery, factory).ExecuteAsync(
            "alpha",
            TestSupport.CreateDescribeParametersRequest(),
            CancellationToken.None);

        Assert.Equal(CapabilityErrorCodes.DocumentContextChanged, outcome.ErrorCode);
        Assert.Equal(new[] { "pipe-alpha" }, factory.RequestedPipes);
        Assert.Single(factory.Clients);
        Assert.Equal(1, factory.Clients[0].DescribeParametersCalls);
    }

    [Fact]
    public async Task Caller_cancellation_remains_cancellation()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("id-cancel", "pipe-cancel", protocolVersion: 5));
        using var cts = new CancellationTokenSource();
        var factory = Factory((_, _, _) => Task.FromResult(new RecordingBridgeClient
        {
            Handshake = async (_, token) =>
            {
                await cts.CancelAsync();
                token.ThrowIfCancellationRequested();
                return TestSupport.CreateHandshake(TestSupport.CreateRegistration("id-cancel", "pipe-cancel"), 5);
            },
            DescribeParameters = (_, _, _) => throw new InvalidOperationException("DescribeParameters must not run after cancellation.")
        }));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateService(discovery, factory).ExecuteAsync(null, TestSupport.CreateDescribeParametersRequest(), cts.Token));
    }

    [Fact]
    public async Task Client_is_disposed_after_success_and_failure()
    {
        var success = await InvokeReadyAsync("id-ok", "pipe-ok");
        Assert.True(success.Client.Disposed);

        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("id-fail", "pipe-fail", protocolVersion: 5));
        var factory = Factory((_, _, _) => Task.FromResult(new RecordingBridgeClient
        {
            Handshake = (_, _) => Task.FromException<BridgeHandshakeResult>(
                new BridgeException(BridgeErrorCodes.HandshakeFailed, "failed")),
            DescribeParameters = (_, _, _) => throw new InvalidOperationException("DescribeParameters must not run.")
        }));

        await CreateService(discovery, factory).ExecuteAsync(null, TestSupport.CreateDescribeParametersRequest(), CancellationToken.None);
        Assert.True(factory.Clients[0].Disposed);
    }

    [Fact]
    public void Application_service_does_not_reference_streamjsonrpc()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "RevitMCP.Server", "DescribeParametersApplicationService.cs"));
        Assert.DoesNotContain("StreamJsonRpc", source, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonRpc", source, StringComparison.Ordinal);
    }

    private static async Task<InvocationContext> InvokeReadyAsync(
        string instanceId,
        string pipeName,
        DescribeParametersRequest? request = null)
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready(instanceId, pipeName, protocolVersion: 5));
        var factory = Factory((pipe, _, _) =>
        {
            var registration = TestSupport.CreateRegistration(instanceId, pipe);
            return Task.FromResult(new RecordingBridgeClient
            {
                Handshake = (handshake, _) =>
                {
                    Assert.Equal(instanceId, handshake.ExpectedInstanceId);
                    return Task.FromResult(TestSupport.CreateHandshake(registration, selectedProtocolVersion: 5));
                },
                DescribeParameters = (discoveryRequest, _, _) => Task.FromResult(TestSupport.CreateDescribeParametersResult(
                    instanceId,
                    discoveryRequest.DocumentId,
                    [TestSupport.CreateDescribeOkElement(discoveryRequest.ElementRefs[0])],
                    matchedCount: 1,
                    truncated: false,
                    TestSupport.CreateBuiltInDescriptor()))
            });
        });

        var outcome = await CreateService(discovery, factory).ExecuteAsync(
            null,
            request ?? TestSupport.CreateDescribeParametersRequest(),
            CancellationToken.None);
        return new InvocationContext(outcome, factory, factory.Clients[0]);
    }

    private static async Task<DescribeParametersOutcome> InvokeDescribeExceptionAsync(BridgeException exception)
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("id-cap", "pipe-cap", protocolVersion: 5));
        var factory = Factory((_, _, _) => Task.FromResult(new RecordingBridgeClient
        {
            Handshake = (_, _) => Task.FromResult(TestSupport.CreateHandshake(TestSupport.CreateRegistration("id-cap", "pipe-cap"), 5)),
            DescribeParameters = (_, _, _) => Task.FromException<DescribeParametersResult>(exception)
        }));

        return await CreateService(discovery, factory).ExecuteAsync(null, TestSupport.CreateDescribeParametersRequest(), CancellationToken.None);
    }

    private static DescribeParametersApplicationService CreateService(
        FakeDiscovery discovery,
        RecordingBridgeClientFactory factory)
    {
        return new DescribeParametersApplicationService(discovery, factory, new ServerTimeouts
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
        DescribeParametersOutcome Outcome,
        RecordingBridgeClientFactory Factory,
        RecordingBridgeClient Client);
}
