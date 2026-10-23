using System.Diagnostics;
using BimIt.Revit.Bridge.Implementation;
using BimIt.Revit.Contracts;
using StreamJsonRpc;
using Xunit;

namespace BimIt.Revit.Bridge.Tests;

public sealed class GetSelectionBridgeTests
{
    [Fact]
    public void V13_requires_the_complete_v12_prefix_plus_selection()
    {
        var metadata = TestSupport.CreateMetadata();
        var services = Full();
        var warnings = new FakeGetWarningsService();
        var selection = new FakeGetSelectionService();

        var v13 = NamedPipeBridgeHost.WithAdvertisedProtocol(
            metadata,
            services.Capability,
            services.Query,
            services.Elements,
            services.Describe,
            services.Values,
            services.Topology,
            services.Preview,
            services.Review,
            services.Apply,
            warnings,
            selection);
        var v12 = NamedPipeBridgeHost.WithAdvertisedProtocol(
            metadata,
            services.Capability,
            services.Query,
            services.Elements,
            services.Describe,
            services.Values,
            services.Topology,
            services.Preview,
            services.Review,
            services.Apply,
            warnings);
        var selectionAlone = NamedPipeBridgeHost.WithAdvertisedProtocol(
            metadata,
            getSelection: selection);
        var selectionWithoutWarnings = NamedPipeBridgeHost.WithAdvertisedProtocol(
            metadata,
            services.Capability,
            services.Query,
            services.Elements,
            services.Describe,
            services.Values,
            services.Topology,
            services.Preview,
            services.Review,
            services.Apply,
            getWarnings: null,
            getSelection: selection);

        Assert.Equal(BridgeProtocol.GetSelectionVersions, v13.SupportedProtocolVersions);
        Assert.Equal(13, v13.SupportedProtocolVersions[0]);
        Assert.Equal(BridgeProtocol.ContextDocumentIdentityVersions, v12.SupportedProtocolVersions);
        Assert.DoesNotContain(13, v12.SupportedProtocolVersions);
        Assert.Equal(BridgeProtocol.HandshakeOnlyVersions, selectionAlone.SupportedProtocolVersions);
        Assert.Equal(BridgeProtocol.ApplyParameterUpdatesVersions, selectionWithoutWarnings.SupportedProtocolVersions);
        Assert.DoesNotContain(13, selectionWithoutWarnings.SupportedProtocolVersions);
    }

    [Fact]
    public void Mep_placement_versions_advertise_only_a_complete_prefix()
    {
        var metadata = TestSupport.CreateMetadata();
        var services = Full();
        var warnings = new FakeGetWarningsService();
        var selection = new FakeGetSelectionService();
        var preview = new StubPreviewMepService();
        var review = new StubReviewMepService();
        var apply = new StubApplyMepService();

        var layouts = new StubGetRoomLayoutsService();
        var v17 = NamedPipeBridgeHost.WithAdvertisedProtocol(metadata, services.Capability, services.Query, services.Elements, services.Describe, services.Values, services.Topology, services.Preview, services.Review, services.Apply, warnings, selection, preview, review, apply, layouts);
        var v16 = NamedPipeBridgeHost.WithAdvertisedProtocol(metadata, services.Capability, services.Query, services.Elements, services.Describe, services.Values, services.Topology, services.Preview, services.Review, services.Apply, warnings, selection, preview, review, apply);
        var v15 = NamedPipeBridgeHost.WithAdvertisedProtocol(metadata, services.Capability, services.Query, services.Elements, services.Describe, services.Values, services.Topology, services.Preview, services.Review, services.Apply, warnings, selection, preview, review);
        var v14 = NamedPipeBridgeHost.WithAdvertisedProtocol(metadata, services.Capability, services.Query, services.Elements, services.Describe, services.Values, services.Topology, services.Preview, services.Review, services.Apply, warnings, selection, preview);

        Assert.Equal(BridgeProtocol.GetRoomLayoutsVersions, v17.SupportedProtocolVersions);
        Assert.Equal(17, v17.SupportedProtocolVersions[0]);
        Assert.Equal(BridgeProtocol.ApplyMepDevicePlacementsVersions, v16.SupportedProtocolVersions);
        Assert.Equal(16, v16.SupportedProtocolVersions[0]);
        Assert.Equal(BridgeProtocol.RequestMepDevicePlacementReviewVersions, v15.SupportedProtocolVersions);
        Assert.Equal(15, v15.SupportedProtocolVersions[0]);
        Assert.Equal(BridgeProtocol.PreviewMepDevicePlacementsVersions, v14.SupportedProtocolVersions);
        Assert.Equal(14, v14.SupportedProtocolVersions[0]);
        Assert.True(BridgeProtocol.SupportsPreviewMepDevicePlacements(17));
        Assert.True(BridgeProtocol.SupportsRequestMepDevicePlacementReview(17));
        Assert.True(BridgeProtocol.SupportsApplyMepDevicePlacements(17));
        Assert.True(BridgeProtocol.SupportsGetSelection(17));
        Assert.False(BridgeProtocol.SupportsGetRoomLayouts(16));
        Assert.False(BridgeProtocol.SupportsGetSelection(20));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(12)]
    [InlineData(20)]
    public async Task Get_selection_is_rejected_outside_v13(int version)
    {
        var service = new FakeGetSelectionService();
        var adapter = Adapter(service, version);
        await adapter.HandshakeAsync(Handshake(), CancellationToken.None);

        var rejected = await Assert.ThrowsAsync<LocalRpcException>(() =>
            adapter.GetSelectionAsync(new GetSelectionRequest(), CancellationToken.None));

        Assert.Equal(BridgeErrorCodes.ProtocolIncompatible, ErrorCode(rejected));
        Assert.Equal(0, service.InvokeCount);
    }

    [Fact]
    public async Task Get_selection_on_v13_returns_the_service_result_unchanged()
    {
        var service = new FakeGetSelectionService
        {
            Result = Result(["z-ref", "a-ref"], selectedCount: 4, truncated: true)
        };
        var adapter = Adapter(service, 13);
        await adapter.HandshakeAsync(Handshake(), CancellationToken.None);

        var result = await adapter.GetSelectionAsync(new GetSelectionRequest { Limit = 2 }, CancellationToken.None);

        Assert.Equal(1, service.InvokeCount);
        Assert.Equal(4, result.SelectedCount);
        Assert.True(result.Truncated);
        Assert.Equal(["z-ref", "a-ref"], result.ElementRefs);
        Assert.Equal("instance-a", result.Context.InstanceId);
    }

    [Fact]
    public async Task Capability_errors_survive_and_unexpected_failures_are_bounded()
    {
        var service = new FakeGetSelectionService
        {
            Error = new BridgeException(CapabilityErrorCodes.InvalidSelection, "invalid")
        };
        var adapter = Adapter(service, 13);
        await adapter.HandshakeAsync(Handshake(), CancellationToken.None);
        var invalid = await Assert.ThrowsAsync<LocalRpcException>(() =>
            adapter.GetSelectionAsync(new GetSelectionRequest(), CancellationToken.None));
        Assert.Equal(CapabilityErrorCodes.InvalidSelection, ErrorCode(invalid));

        service.Error = new InvalidOperationException("revit exploded");
        var failed = await Assert.ThrowsAsync<LocalRpcException>(() =>
            adapter.GetSelectionAsync(new GetSelectionRequest(), CancellationToken.None));
        Assert.Equal(CapabilityErrorCodes.ExecutionFailed, ErrorCode(failed));
    }

    [Fact]
    public async Task Missing_selection_service_is_rejected_on_v13()
    {
        var adapter = new StreamJsonRpcBridgeAdapter(new SelectedHandshake(13), new FakeCapabilityService());
        await adapter.HandshakeAsync(Handshake(), CancellationToken.None);

        var rejected = await Assert.ThrowsAsync<LocalRpcException>(() =>
            adapter.GetSelectionAsync(new GetSelectionRequest(), CancellationToken.None));

        Assert.Equal(BridgeErrorCodes.ProtocolIncompatible, ErrorCode(rejected));
    }

    [Fact]
    public async Task Typed_client_rejects_non_v13_locally()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var selection = new FakeGetSelectionService();
        await using var context = await StartHostAsync(selection);
        await using var rejected = await ConnectAndHandshakeAsync(context, BridgeProtocol.ContextDocumentIdentityVersions);

        var local = await Assert.ThrowsAsync<BridgeException>(() =>
            rejected.GetSelectionAsync(new GetSelectionRequest(), TimeSpan.FromSeconds(2), CancellationToken.None));

        Assert.Equal(BridgeErrorCodes.ProtocolIncompatible, local.ErrorCode);
        Assert.Equal(0, selection.InvokeCount);
    }

    [Fact]
    public async Task Typed_client_timeout_marks_the_connection_unusable()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var selection = new FakeGetSelectionService
        {
            Hold = new TaskCompletionSource<GetSelectionResult>(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        await using var context = await StartHostAsync(selection);
        await using var client = await ConnectAndHandshakeAsync(context, BridgeProtocol.SupportedVersions);

        var timeout = await Assert.ThrowsAsync<BridgeException>(() =>
            client.GetSelectionAsync(new GetSelectionRequest(), TimeSpan.FromMilliseconds(200), CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(CapabilityErrorCodes.ExecutionTimeout, timeout.ErrorCode);

        var unusable = await Assert.ThrowsAsync<BridgeException>(() =>
            client.GetSelectionAsync(new GetSelectionRequest(), TimeSpan.FromSeconds(2), CancellationToken.None));
        Assert.Equal(CapabilityErrorCodes.ExecutionFailed, unusable.ErrorCode);
        selection.Hold.TrySetCanceled();
    }

    [Fact]
    public async Task Typed_client_caller_cancellation_remains_cancellation()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var selection = new FakeGetSelectionService
        {
            Hold = new TaskCompletionSource<GetSelectionResult>(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        await using var context = await StartHostAsync(selection);
        await using var client = await ConnectAndHandshakeAsync(context, BridgeProtocol.SupportedVersions);
        using var canceled = new CancellationTokenSource();
        var pending = client.GetSelectionAsync(new GetSelectionRequest(), TimeSpan.FromSeconds(10), canceled.Token);
        canceled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
        selection.Hold.TrySetCanceled();
    }

    private static StreamJsonRpcBridgeAdapter Adapter(FakeGetSelectionService service, int version)
    {
        return new StreamJsonRpcBridgeAdapter(
            new SelectedHandshake(version),
            new FakeCapabilityService(),
            getSelection: service);
    }

    private static BridgeHandshakeRequest Handshake()
    {
        return new BridgeHandshakeRequest
        {
            ExpectedInstanceId = "any",
            SupportedProtocolVersions = [13],
            ClientName = "RevitMCP.Tests"
        };
    }

    private static string ErrorCode(LocalRpcException exception)
    {
        return Assert.IsType<BridgeError>(exception.ErrorData).Code;
    }

    private static GetSelectionResult Result(IReadOnlyList<string> refs, int selectedCount, bool truncated)
    {
        return new GetSelectionResult
        {
            Context = new GetSelectionContext
            {
                InstanceId = "instance-a",
                DocumentId = "document-a"
            },
            SelectedCount = selectedCount,
            Truncated = truncated,
            ElementRefs = refs
        };
    }

    private static async Task<HostContext> StartHostAsync(IRevitGetSelectionService selection)
    {
        var metadata = CreateLiveMetadata();
        var root = Path.Combine(Path.GetTempPath(), "RevitMCP.Tests", Guid.NewGuid().ToString("N"));
        var services = Full();
        var host = await NamedPipeBridgeHost.StartAsync(
            metadata,
            new FileRegistrationStore(root),
            services.Capability,
            services.Query,
            services.Elements,
            services.Describe,
            services.Values,
            services.Topology,
            services.Preview,
            services.Review,
            services.Apply,
            new FakeGetWarningsService(),
            selection,
            CancellationToken.None);
        return new HostContext(host, metadata);
    }

    private static async Task<NamedPipeBridgeClient> ConnectAndHandshakeAsync(HostContext context, IReadOnlyList<int> versions)
    {
        var client = await NamedPipeBridgeClient.ConnectAsync(context.Host.PipeName, TimeSpan.FromSeconds(3), CancellationToken.None);
        await client.HandshakeAsync(
            new BridgeHandshakeRequest
            {
                ExpectedInstanceId = context.Metadata.InstanceId,
                SupportedProtocolVersions = versions,
                ClientName = "RevitMCP.Tests"
            },
            CancellationToken.None);
        return client;
    }

    private static BridgeInstanceMetadata CreateLiveMetadata()
    {
        var process = Process.GetCurrentProcess();
        return TestSupport.CreateMetadata(
            processId: process.Id,
            startTime: new DateTimeOffset(process.StartTime).ToUniversalTime(),
            sessionId: process.SessionId);
    }

    private static FullServices Full()
    {
        return new FullServices(
            new FakeCapabilityService(),
            new FakeQueryElementsService(),
            new FakeGetElementsService(),
            new FakeDescribeParametersService(),
            new FakeGetParameterValuesService(),
            new FakeGetMepTopologyService(),
            new FakePreviewParameterUpdatesService(),
            new FakeRequestParameterUpdateReviewService(),
            new FakeApplyParameterUpdatesService());
    }

    private sealed record FullServices(
        IRevitCapabilityService Capability,
        IRevitQueryElementsService Query,
        IRevitGetElementsService Elements,
        IRevitDescribeParametersService Describe,
        IRevitGetParameterValuesService Values,
        IRevitGetMepTopologyService Topology,
        IRevitPreviewParameterUpdatesService Preview,
        IRevitRequestParameterUpdateReviewService Review,
        IRevitApplyParameterUpdatesService Apply);

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

    private sealed class SelectedHandshake : IRevitBridgeService
    {
        private readonly int _selected;

        public SelectedHandshake(int selected) => _selected = selected;

        public Task<BridgeHandshakeResult> HandshakeAsync(BridgeHandshakeRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new BridgeHandshakeResult
            {
                InstanceId = request.ExpectedInstanceId,
                ProcessId = 1,
                ProcessStartTimeUtc = DateTimeOffset.UnixEpoch,
                WindowsSessionId = 1,
                RevitVersion = "2026",
                RevitBuild = "test",
                AddinVersion = "test",
                SelectedProtocolVersion = _selected,
                SupportedProtocolVersions = request.SupportedProtocolVersions
            });
        }
    }
}

internal sealed class FakeGetSelectionService : IRevitGetSelectionService
{
    public int InvokeCount { get; private set; }

    public GetSelectionResult Result { get; set; } = new()
    {
        Context = new GetSelectionContext
        {
            InstanceId = "instance-a",
            DocumentId = "document-a"
        },
        SelectedCount = 0,
        Truncated = false,
        ElementRefs = []
    };

    public Exception? Error { get; set; }

    public TaskCompletionSource<GetSelectionResult>? Hold { get; set; }

    public async Task<GetSelectionResult> GetSelectionAsync(GetSelectionRequest request, CancellationToken cancellationToken)
    {
        _ = request;
        InvokeCount++;
        if (Hold is not null)
        {
            return await Hold.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        if (Error is not null)
        {
            throw Error;
        }

        return Result;
    }
}

internal sealed class StubPreviewMepService : IRevitPreviewMepDevicePlacementsService
{
    public Task<PreviewMepDevicePlacementsResult> PreviewMepDevicePlacementsAsync(PreviewMepDevicePlacementsRequest request, CancellationToken cancellationToken)
        => throw new NotSupportedException();
}

internal sealed class StubReviewMepService : IRevitRequestMepDevicePlacementReviewService
{
    public Task<RequestMepDevicePlacementReviewResult> RequestMepDevicePlacementReviewAsync(RequestMepDevicePlacementReviewRequest request, CancellationToken cancellationToken)
        => throw new NotSupportedException();
}

internal sealed class StubApplyMepService : IRevitApplyMepDevicePlacementsService
{
    public Task<ApplyMepDevicePlacementsResult> ApplyMepDevicePlacementsAsync(ApplyMepDevicePlacementsRequest request, CancellationToken cancellationToken)
        => throw new NotSupportedException();
}

internal sealed class StubGetRoomLayoutsService : IRevitGetRoomLayoutsService
{
    public Task<GetRoomLayoutsResult> GetRoomLayoutsAsync(GetRoomLayoutsRequest request, CancellationToken cancellationToken)
        => throw new NotSupportedException();
}
