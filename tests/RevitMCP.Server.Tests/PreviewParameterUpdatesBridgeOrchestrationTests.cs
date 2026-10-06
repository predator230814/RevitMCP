using RevitMCP.Bridge;
using RevitMCP.Contracts;
using RevitMCP.Server;
using Xunit;

namespace RevitMCP.Server.Tests;

public sealed class PreviewParameterUpdatesBridgeOrchestrationTests
{
    [Fact]
    public async Task Handshake_sends_expected_instance_id_and_supported_versions()
    {
        var context = await InvokeReadyAsync("id-2", "revitmcp-pipe-2");

        var request = context.Client.HandshakeRequest;
        Assert.NotNull(request);
        Assert.Equal("id-2", request.ExpectedInstanceId);
        Assert.Equal(BridgeProtocol.SupportedVersions, request.SupportedProtocolVersions);
        Assert.Equal(new[] { 9, 8, 7, 6, 5, 4, 3, 2, 1 }, request.SupportedProtocolVersions);
        Assert.Equal("RevitMCP.Server", request.ClientName);
    }

    [Fact]
    public async Task Selected_v8_invokes_preview_once_with_exact_request_and_timeout()
    {
        var request = TestSupport.CreatePreviewParameterUpdatesRequest(
            "doc-a",
            [
                new PreviewParameterUpdate
                {
                    ElementRef = "ref-b",
                    ParameterRef = "pref-b",
                    Value = new PreviewParameterQuantityValue
                    {
                        Value = 1.5,
                        UnitTypeId = "autodesk.unit.unit:feet-1.0.1"
                    }
                },
                new PreviewParameterUpdate
                {
                    ElementRef = "ref-a",
                    ParameterRef = "pref-a",
                    Value = new PreviewParameterIntegerValue { Value = -7 }
                }
            ]);
        var context = await InvokeReadyAsync("id-v8", "pipe-v8", request);

        Assert.True(context.Outcome.IsSuccess);
        Assert.Equal(1, context.Client.PreviewParameterUpdatesCalls);
        Assert.Same(request, context.Client.LastPreviewParameterUpdatesRequest);
        Assert.Equal("doc-a", context.Client.LastPreviewParameterUpdatesRequest!.DocumentId);
        Assert.Equal("ref-b", context.Client.LastPreviewParameterUpdatesRequest.Updates[0].ElementRef);
        Assert.Equal("pref-b", context.Client.LastPreviewParameterUpdatesRequest.Updates[0].ParameterRef);
        var quantity = Assert.IsType<PreviewParameterQuantityValue>(context.Client.LastPreviewParameterUpdatesRequest.Updates[0].Value);
        Assert.Equal(1.5, quantity.Value);
        Assert.Equal("autodesk.unit.unit:feet-1.0.1", quantity.UnitTypeId);
        var integer = Assert.IsType<PreviewParameterIntegerValue>(context.Client.LastPreviewParameterUpdatesRequest.Updates[1].Value);
        Assert.Equal(-7, integer.Value);
        Assert.Equal(TimeSpan.FromMilliseconds(60), context.Client.LastPreviewParameterUpdatesTimeout);
        Assert.Null(typeof(PreviewParameterUpdatesRequest).GetProperty("InstanceId"));
    }

    [Theory]
    [InlineData(7)]
    [InlineData(10)]
    public async Task Selected_unsupported_protocol_does_not_invoke_preview(int selectedVersion)
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("id-v8", "pipe-v8", protocolVersion: 8));
        var factory = Factory((pipe, _, _) =>
        {
            var registration = TestSupport.CreateRegistration("id-v8", pipe);
            return Task.FromResult(new RecordingBridgeClient
            {
                Handshake = (_, _) => Task.FromResult(TestSupport.CreateHandshake(registration, selectedProtocolVersion: selectedVersion)),
                PreviewParameterUpdates = (_, _, _) => throw new InvalidOperationException("Preview must not run for an unsupported protocol selection.")
            });
        });

        var outcome = await CreateService(discovery, factory).ExecuteAsync(null, TestSupport.CreatePreviewParameterUpdatesRequest(), CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceUnavailable, outcome.ErrorCode);
        Assert.Equal(0, factory.Clients[0].PreviewParameterUpdatesCalls);
    }

    [Fact]
    public async Task Handshake_identity_mismatch_does_not_invoke_preview()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("expected", "pipe-expected", protocolVersion: 8));
        var factory = Factory((_, _, _) => Task.FromResult(new RecordingBridgeClient
        {
            Handshake = (_, _) => Task.FromResult(
                TestSupport.CreateHandshake(TestSupport.CreateRegistration("other", "pipe-expected"), 8)),
            PreviewParameterUpdates = (_, _, _) => throw new InvalidOperationException("Preview must not run.")
        }));

        var outcome = await CreateService(discovery, factory).ExecuteAsync(
            "expected",
            TestSupport.CreatePreviewParameterUpdatesRequest(),
            CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceUnavailable, outcome.ErrorCode);
        Assert.Equal(0, factory.Clients[0].PreviewParameterUpdatesCalls);
    }

    [Theory]
    [InlineData(CapabilityErrorCodes.NoActiveDocument, "No active Revit document is available.")]
    [InlineData(CapabilityErrorCodes.DocumentContextChanged, "The supplied document_id does not match the active document.")]
    [InlineData(CapabilityErrorCodes.UnsupportedDocumentKind, "The active document kind is not supported.")]
    [InlineData(CapabilityErrorCodes.DocumentNotWritable, "The active document is not writable.")]
    [InlineData(CapabilityErrorCodes.InvalidParameterUpdatePreview, "The parameter update preview request is invalid.")]
    [InlineData(CapabilityErrorCodes.IntentCapacityReached, "The preview intent capacity has been reached.")]
    [InlineData(CapabilityErrorCodes.ExecutionTimeout, "The Revit inspection request timed out.")]
    [InlineData(CapabilityErrorCodes.ExecutionFailed, "The Revit inspection could not be executed.")]
    public async Task Accepted_capability_errors_are_preserved(string code, string message)
    {
        var outcome = await InvokePreviewExceptionAsync(new BridgeException(code, message));

        Assert.Equal(code, outcome.ErrorCode);
        Assert.Equal(message, outcome.ErrorMessage);
        Assert.False(outcome.IsSuccess);
    }

    [Fact]
    public async Task Intent_capacity_reached_is_not_retried()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("alpha", "pipe-alpha", protocolVersion: 8));
        discovery.Instances.Add(TestSupport.Ready("beta", "pipe-beta", protocolVersion: 8));
        var factory = Factory((_, _, _) => Task.FromResult(new RecordingBridgeClient
        {
            Handshake = (_, _) => Task.FromResult(TestSupport.CreateHandshake(TestSupport.CreateRegistration("alpha", "pipe-alpha"), 8)),
            PreviewParameterUpdates = (_, _, _) => Task.FromException<PreviewParameterUpdatesResult>(
                new BridgeException(CapabilityErrorCodes.IntentCapacityReached, "The preview intent capacity has been reached."))
        }));

        var outcome = await CreateService(discovery, factory).ExecuteAsync(
            "alpha",
            TestSupport.CreatePreviewParameterUpdatesRequest(),
            CancellationToken.None);

        Assert.Equal(CapabilityErrorCodes.IntentCapacityReached, outcome.ErrorCode);
        Assert.Equal(new[] { "pipe-alpha" }, factory.RequestedPipes);
        Assert.Single(factory.Clients);
        Assert.Equal(1, factory.Clients[0].PreviewParameterUpdatesCalls);
    }

    [Fact]
    public async Task Caller_cancellation_remains_cancellation()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("id-cancel", "pipe-cancel", protocolVersion: 8));
        using var cts = new CancellationTokenSource();
        var factory = Factory((_, _, _) => Task.FromResult(new RecordingBridgeClient
        {
            Handshake = async (_, token) =>
            {
                await cts.CancelAsync();
                token.ThrowIfCancellationRequested();
                return TestSupport.CreateHandshake(TestSupport.CreateRegistration("id-cancel", "pipe-cancel"), 8);
            },
            PreviewParameterUpdates = (_, _, _) => throw new InvalidOperationException("Preview must not run after cancellation.")
        }));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateService(discovery, factory).ExecuteAsync(null, TestSupport.CreatePreviewParameterUpdatesRequest(), cts.Token));
    }

    [Fact]
    public async Task Client_is_disposed_after_success_and_failure()
    {
        var success = await InvokeReadyAsync("id-ok", "pipe-ok");
        Assert.True(success.Client.Disposed);

        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("id-fail", "pipe-fail", protocolVersion: 8));
        var factory = Factory((_, _, _) => Task.FromResult(new RecordingBridgeClient
        {
            Handshake = (_, _) => Task.FromException<BridgeHandshakeResult>(
                new BridgeException(BridgeErrorCodes.HandshakeFailed, "failed")),
            PreviewParameterUpdates = (_, _, _) => throw new InvalidOperationException("Preview must not run.")
        }));

        await CreateService(discovery, factory).ExecuteAsync(null, TestSupport.CreatePreviewParameterUpdatesRequest(), CancellationToken.None);
        Assert.True(factory.Clients[0].Disposed);
        Assert.Equal(0, factory.Clients[0].PreviewParameterUpdatesCalls);
    }

    private static async Task<InvocationContext> InvokeReadyAsync(
        string instanceId,
        string pipeName,
        PreviewParameterUpdatesRequest? request = null)
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready(instanceId, pipeName, protocolVersion: 8));
        var factory = Factory((pipe, _, _) =>
        {
            var registration = TestSupport.CreateRegistration(instanceId, pipe);
            return Task.FromResult(new RecordingBridgeClient
            {
                Handshake = (handshake, _) =>
                {
                    Assert.Equal(instanceId, handshake.ExpectedInstanceId);
                    return Task.FromResult(TestSupport.CreateHandshake(registration, selectedProtocolVersion: 8));
                },
                PreviewParameterUpdates = (previewRequest, _, _) => Task.FromResult(
                    TestSupport.CreatePreviewParameterUpdatesResult(
                        instanceId,
                        previewRequest.DocumentId,
                        ready: false,
                        previewRequest.Updates.Select(update => TestSupport.CreatePreviewFailureItem(
                            update.ElementRef,
                            update.ParameterRef,
                            PreviewParameterUpdateStatus.ParameterNotWritable)).ToArray()))
            });
        });

        var outcome = await CreateService(discovery, factory).ExecuteAsync(
            null,
            request ?? TestSupport.CreatePreviewParameterUpdatesRequest(),
            CancellationToken.None);
        return new InvocationContext(outcome, factory, factory.Clients[0]);
    }

    private static async Task<PreviewParameterUpdatesOutcome> InvokePreviewExceptionAsync(BridgeException exception)
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("id-cap", "pipe-cap", protocolVersion: 8));
        var factory = Factory((_, _, _) => Task.FromResult(new RecordingBridgeClient
        {
            Handshake = (_, _) => Task.FromResult(TestSupport.CreateHandshake(TestSupport.CreateRegistration("id-cap", "pipe-cap"), 8)),
            PreviewParameterUpdates = (_, _, _) => Task.FromException<PreviewParameterUpdatesResult>(exception)
        }));

        return await CreateService(discovery, factory).ExecuteAsync(null, TestSupport.CreatePreviewParameterUpdatesRequest(), CancellationToken.None);
    }

    private static PreviewParameterUpdatesApplicationService CreateService(
        FakeDiscovery discovery,
        RecordingBridgeClientFactory factory)
    {
        return new PreviewParameterUpdatesApplicationService(discovery, factory, new ServerTimeouts
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
        PreviewParameterUpdatesOutcome Outcome,
        RecordingBridgeClientFactory Factory,
        RecordingBridgeClient Client);
}
