using RevitMCP.Bridge;
using RevitMCP.Contracts;
using RevitMCP.Server;
using Xunit;

namespace RevitMCP.Server.Tests;

public sealed class GetParameterValuesBridgeOrchestrationTests
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
        Assert.Equal(new[] { 7, 6, 5, 4, 3, 2, 1 }, request.SupportedProtocolVersions);
        Assert.Equal("RevitMCP.Server", request.ClientName);
    }

    [Fact]
    public async Task Handshake_v6_executes_get_parameter_values_with_exact_request()
    {
        var request = TestSupport.CreateGetParameterValuesRequest(
            "doc-a",
            [
                new GetParameterValueRead { ElementRef = "ref-b", ParameterRef = "pref-2" },
                new GetParameterValueRead { ElementRef = "ref-a", ParameterRef = "pref-1" }
            ]);
        var context = await InvokeReadyAsync("id-v6", "pipe-v6", request);

        Assert.True(context.Outcome.IsSuccess);
        Assert.Equal(1, context.Client.GetParameterValuesCalls);
        Assert.Same(request, context.Client.LastGetParameterValuesRequest);
        Assert.Equal("doc-a", context.Client.LastGetParameterValuesRequest!.DocumentId);
        Assert.Equal(new[] { "ref-b", "ref-a" }, context.Client.LastGetParameterValuesRequest.Reads.Select(read => read.ElementRef));
        Assert.Null(typeof(GetParameterValuesRequest).GetProperty("InstanceId"));
    }

    [Fact]
    public async Task Handshake_v5_does_not_invoke_get_parameter_values()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("id-v5", "pipe-v5", protocolVersion: 6));
        var factory = Factory((pipe, _, _) =>
        {
            var registration = TestSupport.CreateRegistration("id-v5", pipe);
            return Task.FromResult(new RecordingBridgeClient
            {
                Handshake = (_, _) => Task.FromResult(TestSupport.CreateHandshake(registration, selectedProtocolVersion: 5)),
                GetParameterValues = (_, _, _) => throw new InvalidOperationException("GetParameterValues must not run for protocol v5.")
            });
        });

        var outcome = await CreateService(discovery, factory).ExecuteAsync(null, TestSupport.CreateGetParameterValuesRequest(), CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceUnavailable, outcome.ErrorCode);
        Assert.Equal(0, factory.Clients[0].GetParameterValuesCalls);
    }

    [Fact]
    public async Task Handshake_unknown_v8_does_not_invoke_get_parameter_values()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("id-v7", "pipe-v7", protocolVersion: 6));
        var factory = Factory((pipe, _, _) =>
        {
            var registration = TestSupport.CreateRegistration("id-v7", pipe);
            return Task.FromResult(new RecordingBridgeClient
            {
                Handshake = (_, _) => Task.FromResult(TestSupport.CreateHandshake(registration, selectedProtocolVersion: 8)),
                GetParameterValues = (_, _, _) => throw new InvalidOperationException("GetParameterValues must not run for unknown v8.")
            });
        });

        var outcome = await CreateService(discovery, factory).ExecuteAsync(null, TestSupport.CreateGetParameterValuesRequest(), CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceUnavailable, outcome.ErrorCode);
        Assert.Equal(0, factory.Clients[0].GetParameterValuesCalls);
    }

    [Fact]
    public async Task Handshake_identity_mismatch_maps_to_unavailable()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("expected", "pipe-expected", protocolVersion: 6));
        var factory = Factory((_, _, _) => Task.FromResult(new RecordingBridgeClient
        {
            Handshake = (_, _) => Task.FromException<BridgeHandshakeResult>(
                new BridgeException(BridgeErrorCodes.IdentityMismatch, "identity mismatch")),
            GetParameterValues = (_, _, _) => throw new InvalidOperationException("GetParameterValues must not run.")
        }));

        var outcome = await CreateService(discovery, factory).ExecuteAsync(
            "expected",
            TestSupport.CreateGetParameterValuesRequest(),
            CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceUnavailable, outcome.ErrorCode);
        Assert.DoesNotContain("BRIDGE_IDENTITY_MISMATCH", outcome.ErrorCode);
        Assert.Equal(0, factory.Clients[0].GetParameterValuesCalls);
    }

    [Fact]
    public async Task Explicit_target_failure_does_not_select_an_alternate_instance()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("alpha", "pipe-alpha", protocolVersion: 6));
        discovery.Instances.Add(TestSupport.Ready("beta", "pipe-beta", protocolVersion: 6));
        var factory = new RecordingBridgeClientFactory
        {
            Connect = (_, _, _) => throw new IOException("pipe closed")
        };

        var outcome = await CreateService(discovery, factory).ExecuteAsync(
            "alpha",
            TestSupport.CreateGetParameterValuesRequest(),
            CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceUnavailable, outcome.ErrorCode);
        Assert.Equal(new[] { "pipe-alpha" }, factory.RequestedPipes);
    }

    [Fact]
    public async Task Mixed_item_statuses_remain_successful_structured_results()
    {
        var request = TestSupport.CreateGetParameterValuesRequest(
            "doc-1",
            [
                new GetParameterValueRead { ElementRef = "valid-ref", ParameterRef = "pref-1" },
                new GetParameterValueRead { ElementRef = "missing-ref", ParameterRef = "pref-1" },
                new GetParameterValueRead { ElementRef = "valid-ref", ParameterRef = "unknown-pref" }
            ]);
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("id-partial", "pipe-partial", protocolVersion: 6));
        var factory = Factory((_, _, _) => Task.FromResult(new RecordingBridgeClient
        {
            Handshake = (_, _) => Task.FromResult(
                TestSupport.CreateHandshake(TestSupport.CreateRegistration("id-partial", "pipe-partial"), 6)),
            GetParameterValues = (_, _, _) => Task.FromResult(TestSupport.CreateGetParameterValuesResult(
                "id-partial",
                "doc-1",
                TestSupport.CreateParameterValueOk("valid-ref", "pref-1"),
                TestSupport.CreateParameterValueFailure("missing-ref", "pref-1", GetParameterValueStatus.ElementNotFound),
                TestSupport.CreateParameterValueFailure("valid-ref", "unknown-pref", GetParameterValueStatus.ParameterRefNotFound)))
        }));

        var outcome = await CreateService(discovery, factory).ExecuteAsync(null, request, CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(3, outcome.Result!.Items.Count);
        Assert.Equal(GetParameterValueStatus.Ok, outcome.Result.Items[0].Status);
        Assert.Equal(GetParameterValueStatus.ElementNotFound, outcome.Result.Items[1].Status);
        Assert.Equal(GetParameterValueStatus.ParameterRefNotFound, outcome.Result.Items[2].Status);
    }

    [Fact]
    public async Task Capability_timeout_preserves_accepted_code()
    {
        var outcome = await InvokeValuesExceptionAsync(
            new BridgeException(CapabilityErrorCodes.ExecutionTimeout, "The Revit inspection request timed out."));

        Assert.Equal(CapabilityErrorCodes.ExecutionTimeout, outcome.ErrorCode);
        Assert.Equal("The Revit inspection request timed out.", outcome.ErrorMessage);
    }

    [Theory]
    [InlineData(CapabilityErrorCodes.DocumentContextChanged, "The supplied document_id does not match the active document.")]
    [InlineData(CapabilityErrorCodes.InvalidParameterRead, "The parameter value request is invalid.")]
    [InlineData(CapabilityErrorCodes.NoActiveDocument, "No active Revit document is available.")]
    [InlineData(CapabilityErrorCodes.ExecutionFailed, "The Revit inspection could not be executed.")]
    public async Task Accepted_capability_errors_are_preserved(string code, string message)
    {
        var outcome = await InvokeValuesExceptionAsync(new BridgeException(code, message));

        Assert.Equal(code, outcome.ErrorCode);
        Assert.Equal(message, outcome.ErrorMessage);
    }

    [Fact]
    public async Task Document_context_changed_does_not_retry()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("alpha", "pipe-alpha", protocolVersion: 6));
        discovery.Instances.Add(TestSupport.Ready("beta", "pipe-beta", protocolVersion: 6));
        var factory = Factory((_, _, _) => Task.FromResult(new RecordingBridgeClient
        {
            Handshake = (_, _) => Task.FromResult(
                TestSupport.CreateHandshake(TestSupport.CreateRegistration("alpha", "pipe-alpha"), 6)),
            GetParameterValues = (_, _, _) => Task.FromException<GetParameterValuesResult>(
                new BridgeException(CapabilityErrorCodes.DocumentContextChanged, "The supplied document_id does not match the active document."))
        }));

        var outcome = await CreateService(discovery, factory).ExecuteAsync(
            "alpha",
            TestSupport.CreateGetParameterValuesRequest(),
            CancellationToken.None);

        Assert.Equal(CapabilityErrorCodes.DocumentContextChanged, outcome.ErrorCode);
        Assert.Equal(new[] { "pipe-alpha" }, factory.RequestedPipes);
        Assert.Single(factory.Clients);
        Assert.Equal(1, factory.Clients[0].GetParameterValuesCalls);
    }

    [Fact]
    public async Task Caller_cancellation_remains_cancellation()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("id-cancel", "pipe-cancel", protocolVersion: 6));
        using var cts = new CancellationTokenSource();
        var factory = Factory((_, _, _) => Task.FromResult(new RecordingBridgeClient
        {
            Handshake = async (_, token) =>
            {
                await cts.CancelAsync();
                token.ThrowIfCancellationRequested();
                return TestSupport.CreateHandshake(TestSupport.CreateRegistration("id-cancel", "pipe-cancel"), 6);
            },
            GetParameterValues = (_, _, _) => throw new InvalidOperationException("GetParameterValues must not run after cancellation.")
        }));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateService(discovery, factory).ExecuteAsync(null, TestSupport.CreateGetParameterValuesRequest(), cts.Token));
    }

    [Fact]
    public async Task Client_is_disposed_after_success_and_failure()
    {
        var success = await InvokeReadyAsync("id-ok", "pipe-ok");
        Assert.True(success.Client.Disposed);

        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("id-fail", "pipe-fail", protocolVersion: 6));
        var factory = Factory((_, _, _) => Task.FromResult(new RecordingBridgeClient
        {
            Handshake = (_, _) => Task.FromException<BridgeHandshakeResult>(
                new BridgeException(BridgeErrorCodes.HandshakeFailed, "failed")),
            GetParameterValues = (_, _, _) => throw new InvalidOperationException("GetParameterValues must not run.")
        }));

        await CreateService(discovery, factory).ExecuteAsync(null, TestSupport.CreateGetParameterValuesRequest(), CancellationToken.None);
        Assert.True(factory.Clients[0].Disposed);
    }

    [Fact]
    public void Application_service_does_not_reference_streamjsonrpc()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "RevitMCP.Server", "GetParameterValuesApplicationService.cs"));
        Assert.DoesNotContain("StreamJsonRpc", source, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonRpc", source, StringComparison.Ordinal);
    }

    private static async Task<InvocationContext> InvokeReadyAsync(
        string instanceId,
        string pipeName,
        GetParameterValuesRequest? request = null)
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready(instanceId, pipeName, protocolVersion: 6));
        var factory = Factory((pipe, _, _) =>
        {
            var registration = TestSupport.CreateRegistration(instanceId, pipe);
            return Task.FromResult(new RecordingBridgeClient
            {
                Handshake = (handshake, _) =>
                {
                    Assert.Equal(instanceId, handshake.ExpectedInstanceId);
                    return Task.FromResult(TestSupport.CreateHandshake(registration, selectedProtocolVersion: 6));
                },
                GetParameterValues = (valueRequest, _, _) => Task.FromResult(TestSupport.CreateGetParameterValuesResult(
                    instanceId,
                    valueRequest.DocumentId,
                    TestSupport.CreateParameterValueOk(
                        valueRequest.Reads[0].ElementRef,
                        valueRequest.Reads[0].ParameterRef)))
            });
        });

        var outcome = await CreateService(discovery, factory).ExecuteAsync(
            null,
            request ?? TestSupport.CreateGetParameterValuesRequest(),
            CancellationToken.None);
        return new InvocationContext(outcome, factory, factory.Clients[0]);
    }

    private static async Task<GetParameterValuesOutcome> InvokeValuesExceptionAsync(BridgeException exception)
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("id-cap", "pipe-cap", protocolVersion: 6));
        var factory = Factory((_, _, _) => Task.FromResult(new RecordingBridgeClient
        {
            Handshake = (_, _) => Task.FromResult(TestSupport.CreateHandshake(TestSupport.CreateRegistration("id-cap", "pipe-cap"), 6)),
            GetParameterValues = (_, _, _) => Task.FromException<GetParameterValuesResult>(exception)
        }));

        return await CreateService(discovery, factory).ExecuteAsync(null, TestSupport.CreateGetParameterValuesRequest(), CancellationToken.None);
    }

    private static GetParameterValuesApplicationService CreateService(
        FakeDiscovery discovery,
        RecordingBridgeClientFactory factory)
    {
        return new GetParameterValuesApplicationService(discovery, factory, new ServerTimeouts
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
        GetParameterValuesOutcome Outcome,
        RecordingBridgeClientFactory Factory,
        RecordingBridgeClient Client);
}
