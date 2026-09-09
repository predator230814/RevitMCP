using System.Diagnostics;
using RevitMCP.Contracts;
using Xunit;

namespace RevitMCP.Bridge.Tests;

public sealed class GetContextBridgeTests
{
    [Fact]
    public async Task Handshake_selects_protocol_2_when_both_peers_support_v2()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var capability = new FakeCapabilityService();
        await using var context = await StartHostAsync(capability);
        await using var client = await NamedPipeBridgeClient.ConnectAsync(context.Host.PipeName, TimeSpan.FromSeconds(3), CancellationToken.None);

        var result = await client.HandshakeAsync(CreateHandshakeRequest(context.Metadata.InstanceId, [2, 1]), CancellationToken.None);

        Assert.Equal(2, result.SelectedProtocolVersion);
        Assert.Equal(new[] { 2, 1 }, result.SupportedProtocolVersions);
        Assert.Equal(2, context.Host.Registration?.BridgeProtocolVersion);
    }

    [Fact]
    public async Task Handshake_falls_back_to_protocol_1_when_host_is_handshake_only()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await using var context = await StartHostAsync();
        await using var client = await NamedPipeBridgeClient.ConnectAsync(context.Host.PipeName, TimeSpan.FromSeconds(3), CancellationToken.None);

        var result = await client.HandshakeAsync(CreateHandshakeRequest(context.Metadata.InstanceId, [2, 1]), CancellationToken.None);

        Assert.Equal(1, result.SelectedProtocolVersion);
        Assert.Equal(BridgeProtocol.HandshakeOnlyVersions, result.SupportedProtocolVersions);
        Assert.Equal(1, context.Host.Registration?.BridgeProtocolVersion);
    }

    [Fact]
    public async Task Handshake_only_host_does_not_advertise_protocol_v2()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var metadata = CreateLiveMetadata(protocolVersions: [2, 1]);
        await using var context = await StartHostAsync(capability: null, metadata);
        await using var client = await NamedPipeBridgeClient.ConnectAsync(context.Host.PipeName, TimeSpan.FromSeconds(3), CancellationToken.None);

        var result = await client.HandshakeAsync(CreateHandshakeRequest(metadata.InstanceId, [2, 1]), CancellationToken.None);

        Assert.DoesNotContain(2, result.SupportedProtocolVersions);
        Assert.Equal(1, result.SelectedProtocolVersion);
        Assert.Equal(1, context.Host.Registration?.BridgeProtocolVersion);
        Assert.Equal(BridgeProtocol.HandshakeOnlyVersions, context.Host.Metadata.SupportedProtocolVersions);
    }

    [Fact]
    public async Task Host_with_capability_service_advertises_protocol_v2()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var metadata = CreateLiveMetadata(protocolVersions: [1]);
        await using var context = await StartHostAsync(new FakeCapabilityService(), metadata);

        Assert.Equal(2, context.Host.Registration?.BridgeProtocolVersion);
        Assert.Equal(BridgeProtocol.SupportedVersions, context.Host.Metadata.SupportedProtocolVersions);
    }

    [Fact]
    public async Task Named_pipe_get_context_returns_capability_result()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var expected = FakeCapabilityService.CreateProjectResult();
        var capability = new FakeCapabilityService { Result = expected };
        await using var context = await StartHostAsync(capability);
        await using var client = await ConnectAndHandshakeAsync(context, [2, 1]);

        var result = await client.GetContextAsync(new GetContextRequest(), TimeSpan.FromSeconds(3), CancellationToken.None);

        Assert.Equal(1, capability.InvokeCount);
        Assert.NotNull(capability.LastRequest);
        Assert.Equal(expected.Instance.InstanceId, result.Instance.InstanceId);
        Assert.Equal(expected.Instance.RevitVersion, result.Instance.RevitVersion);
        Assert.Equal(expected.Instance.RevitBuild, result.Instance.RevitBuild);
        Assert.Equal(expected.Document!.Title, result.Document!.Title);
        Assert.Equal(expected.Document.Kind, result.Document.Kind);
        Assert.Equal(expected.Document.IsWorkshared, result.Document.IsWorkshared);
        Assert.Equal(expected.Document.IsModelInCloud, result.Document.IsModelInCloud);
        Assert.Equal(expected.Document.IsReadOnly, result.Document.IsReadOnly);
        Assert.Equal(expected.Document.IsModified, result.Document.IsModified);
        Assert.Equal(expected.ActiveView!.ElementId, result.ActiveView!.ElementId);
        Assert.Equal(expected.ActiveView.Name, result.ActiveView.Name);
        Assert.Equal(expected.ActiveView.ViewType, result.ActiveView.ViewType);
        Assert.Equal(expected.Selection.Count, result.Selection.Count);
    }

    [Fact]
    public async Task Get_context_before_handshake_is_rejected_locally()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var capability = new FakeCapabilityService();
        await using var context = await StartHostAsync(capability);
        await using var client = await NamedPipeBridgeClient.ConnectAsync(context.Host.PipeName, TimeSpan.FromSeconds(3), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<BridgeException>(() =>
            client.GetContextAsync(new GetContextRequest(), TimeSpan.FromSeconds(3), CancellationToken.None));

        Assert.Equal(BridgeErrorCodes.HandshakeFailed, exception.ErrorCode);
        Assert.Equal(0, capability.InvokeCount);
    }

    [Fact]
    public async Task Get_context_after_v1_negotiation_is_rejected_locally()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var capability = new FakeCapabilityService();
        await using var context = await StartHostAsync(capability);
        await using var client = await ConnectAndHandshakeAsync(context, [1]);

        var exception = await Assert.ThrowsAsync<BridgeException>(() =>
            client.GetContextAsync(new GetContextRequest(), TimeSpan.FromSeconds(3), CancellationToken.None));

        Assert.Equal(BridgeErrorCodes.ProtocolIncompatible, exception.ErrorCode);
        Assert.Equal(0, capability.InvokeCount);
    }

    [Fact]
    public async Task Get_context_after_v2_negotiation_succeeds()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var capability = new FakeCapabilityService { Result = FakeCapabilityService.CreateZeroDocumentResult() };
        await using var context = await StartHostAsync(capability);
        await using var client = await ConnectAndHandshakeAsync(context, [2, 1]);

        var result = await client.GetContextAsync(new GetContextRequest(), TimeSpan.FromSeconds(3), CancellationToken.None);

        Assert.Equal(1, capability.InvokeCount);
        Assert.Null(result.Document);
        Assert.Null(result.ActiveView);
        Assert.Equal(0, result.Selection.Count);
    }

    [Fact]
    public async Task Remote_execution_failed_survives_mapping()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var capability = new FakeCapabilityService
        {
            Error = new BridgeException(CapabilityErrorCodes.ExecutionFailed, "The Revit context could not be collected.")
        };
        await using var context = await StartHostAsync(capability);
        await using var client = await ConnectAndHandshakeAsync(context, [2, 1]);

        var exception = await Assert.ThrowsAsync<BridgeException>(() =>
            client.GetContextAsync(new GetContextRequest(), TimeSpan.FromSeconds(3), CancellationToken.None));

        Assert.Equal(CapabilityErrorCodes.ExecutionFailed, exception.ErrorCode);
        Assert.Equal("The Revit context could not be collected.", exception.Message);
        Assert.DoesNotContain("stack", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unstructured_capability_failure_is_not_handshake_failed()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var capability = new FakeCapabilityService
        {
            Error = new InvalidOperationException("unexpected capability boom")
        };
        await using var context = await StartHostAsync(capability);
        await using var client = await ConnectAndHandshakeAsync(context, [2, 1]);

        var exception = await Assert.ThrowsAsync<BridgeException>(() =>
            client.GetContextAsync(new GetContextRequest(), TimeSpan.FromSeconds(3), CancellationToken.None));

        Assert.Equal(CapabilityErrorCodes.ExecutionFailed, exception.ErrorCode);
        Assert.NotEqual(BridgeErrorCodes.HandshakeFailed, exception.ErrorCode);
        Assert.DoesNotContain("unexpected capability boom", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Silent_capability_peer_times_out_as_execution_timeout()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var capability = new FakeCapabilityService
        {
            Hold = new TaskCompletionSource<GetContextResult>(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        await using var context = await StartHostAsync(capability);
        await using var client = await ConnectAndHandshakeAsync(context, [2, 1]);

        var exception = await Assert.ThrowsAsync<BridgeException>(() =>
            client.GetContextAsync(new GetContextRequest(), TimeSpan.FromMilliseconds(400), CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Equal(CapabilityErrorCodes.ExecutionTimeout, exception.ErrorCode);
        Assert.NotEqual(BridgeErrorCodes.HandshakeTimeout, exception.ErrorCode);

        var reuse = await Assert.ThrowsAsync<BridgeException>(() =>
            client.GetContextAsync(new GetContextRequest(), TimeSpan.FromSeconds(3), CancellationToken.None));
        Assert.Equal(CapabilityErrorCodes.ExecutionFailed, reuse.ErrorCode);
        Assert.Equal(1, capability.InvokeCount);

        capability.Hold.TrySetCanceled();
    }

    [Fact]
    public async Task Caller_cancellation_remains_cancellation()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var capability = new FakeCapabilityService
        {
            Hold = new TaskCompletionSource<GetContextResult>(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        await using var context = await StartHostAsync(capability);
        await using var client = await ConnectAndHandshakeAsync(context, [2, 1]);
        using var cts = new CancellationTokenSource();
        var pending = client.GetContextAsync(new GetContextRequest(), TimeSpan.FromSeconds(10), cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
        capability.Hold.TrySetCanceled();
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

    private static async Task<HostContext> StartHostAsync(
        IRevitCapabilityService? capability = null,
        BridgeInstanceMetadata? metadata = null)
    {
        metadata ??= CreateLiveMetadata();
        var root = Path.Combine(Path.GetTempPath(), "RevitMCP.Tests", Guid.NewGuid().ToString("N"));
        var store = new FileRegistrationStore(root);
        var host = await NamedPipeBridgeHost.StartAsync(metadata, store, capability, CancellationToken.None);
        return new HostContext(host, metadata);
    }

    private static BridgeInstanceMetadata CreateLiveMetadata(IReadOnlyList<int>? protocolVersions = null)
    {
        var process = Process.GetCurrentProcess();
        return TestSupport.CreateMetadata(
            processId: process.Id,
            startTime: new DateTimeOffset(process.StartTime).ToUniversalTime(),
            sessionId: process.SessionId,
            protocolVersions: protocolVersions);
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
