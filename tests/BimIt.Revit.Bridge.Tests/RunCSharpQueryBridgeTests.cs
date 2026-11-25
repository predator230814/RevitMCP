using System.Diagnostics;
using Xunit;
using System.Text.Json;
using BimIt.Revit.Bridge.Implementation;
using BimIt.Revit.Contracts;
using StreamJsonRpc;

namespace BimIt.Revit.Bridge.Tests;

public sealed class RunCSharpQueryBridgeTests
{
    [Fact]
    public void Generic_bridge_error_omits_details()
    {
        var json = JsonSerializer.Serialize(new BridgeError { Code = "BRIDGE_HANDSHAKE_FAILED", Message = "no" }, ContractJson.Options);
        Assert.DoesNotContain("details", json, StringComparison.OrdinalIgnoreCase);
        var local = StreamJsonRpcExceptionMapper.ToLocalRpc(new BridgeException("BRIDGE_HANDSHAKE_FAILED", "no"));
        var error = Assert.IsType<BridgeError>(local.ErrorData);
        Assert.Null(error.Details);
        Assert.Equal("BRIDGE_HANDSHAKE_FAILED", error.Code);
    }

    [Fact]
    public void Complete_v25_host_without_csharp_advertises_25_and_with_it_advertises_26()
    {
        var metadata = TestSupport.CreateMetadata();
        var without = NamedPipeBridgeHost.WithAdvertisedProtocol(
            metadata,
            new FakeCapabilityService(),
            new FakeQueryElementsService(),
            new FakeGetElementsService(),
            new FakeDescribeParametersService(),
            new FakeGetParameterValuesService(),
            new FakeGetMepTopologyService(),
            new FakePreviewParameterUpdatesService(),
            new FakeRequestParameterUpdateReviewService(),
            new UnusedApply(),
            new UnusedWarnings(),
            new UnusedSelection(),
            new UnusedMepPreview(),
            new UnusedMepReview(),
            new UnusedMepApply(),
            new UnusedRooms(),
            new UnusedDeviceTypes(),
            new LinkedPatterns(),
            new UnusedSpatial(),
            new UnusedFamilyTypes(),
            new UnusedBindings(),
            new UnusedRead());
        Assert.Equal(BridgeProtocol.QueryElementPredicatesVersions, without.SupportedProtocolVersions);
        Assert.Equal(25, without.SupportedProtocolVersions[0]);

        var with = NamedPipeBridgeHost.WithAdvertisedProtocol(
            metadata,
            new FakeCapabilityService(),
            new FakeQueryElementsService(),
            new FakeGetElementsService(),
            new FakeDescribeParametersService(),
            new FakeGetParameterValuesService(),
            new FakeGetMepTopologyService(),
            new FakePreviewParameterUpdatesService(),
            new FakeRequestParameterUpdateReviewService(),
            new UnusedApply(),
            new UnusedWarnings(),
            new UnusedSelection(),
            new UnusedMepPreview(),
            new UnusedMepReview(),
            new UnusedMepApply(),
            new UnusedRooms(),
            new UnusedDeviceTypes(),
            new LinkedPatterns(),
            new UnusedSpatial(),
            new UnusedFamilyTypes(),
            new UnusedBindings(),
            new UnusedRead(),
            new ScriptedQuery());
        Assert.Equal(BridgeProtocol.SupportedVersions, with.SupportedProtocolVersions);
        Assert.Equal(26, with.SupportedProtocolVersions[0]);
        Assert.False(BridgeProtocol.SupportsRunCSharpQuery(25));
        Assert.False(BridgeProtocol.SupportsRunCSharpQuery(27));
        Assert.True(BridgeProtocol.SupportsGetContext(26));
        Assert.True(BridgeProtocol.SupportsQueryElementPredicates(26));
    }

    [Fact]
    public async Task Handshake_26_returns_success_and_preserves_disabled_and_diagnostic_details()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var query = new ScriptedQuery();
        await using var context = await StartAsync(query);
        Assert.Equal(26, context.Host.Metadata.SupportedProtocolVersions[0]);
        await using var client = await ConnectAsync(context, BridgeProtocol.SupportedVersions);
        var success = await client.RunCSharpQueryAsync(Request(), TimeSpan.FromSeconds(3), CancellationToken.None);
        Assert.Equal(7, success.Result.GetInt32());
        Assert.Equal("hash", success.SourceSha256);

        query.Error = new BridgeException("CSHARP_QUERY_DISABLED", "The C# query could not be completed.");
        var disabled = await Assert.ThrowsAsync<BridgeException>(() =>
            client.RunCSharpQueryAsync(Request(), TimeSpan.FromSeconds(3), CancellationToken.None));
        Assert.Equal("CSHARP_QUERY_DISABLED", disabled.ErrorCode);
        Assert.Null(disabled.Details);

        var details = JsonSerializer.SerializeToElement(new RunCSharpQueryErrorDetails
        {
            SourceSha256 = "hash",
            PolicyVersion = 1,
            RevitApi = new RunCSharpQueryApiIdentity { Name = "RevitAPI", Version = "26.5.0.0" },
            CompileOutcome = "policy_rejected",
            Diagnostics = [new RunCSharpQueryDiagnostic { Code = "BIMQ001", Line = 1, Column = 2, Message = "forbidden" }]
        }, ContractJson.Options);
        query.Error = new BridgeException("CSHARP_QUERY_POLICY_REJECTED", "The C# query could not be completed.", details);
        var rejected = await Assert.ThrowsAsync<BridgeException>(() =>
            client.RunCSharpQueryAsync(Request(), TimeSpan.FromSeconds(3), CancellationToken.None));
        Assert.Equal("CSHARP_QUERY_POLICY_REJECTED", rejected.ErrorCode);
        Assert.Equal("forbidden", rejected.Details!.Value.GetProperty("diagnostics")[0].GetProperty("message").GetString());
        Assert.False(rejected.Details.Value.TryGetProperty("cache_key", out _));
        Assert.False(rejected.Details.Value.TryGetProperty("source", out _));
        Assert.DoesNotContain("exception", rejected.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Selected_25_rejects_the_csharp_query_locally()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var query = new ScriptedQuery();
        await using var context = await StartAsync(query, includeQuery: false);
        await using var client = await ConnectAsync(context, BridgeProtocol.QueryElementPredicatesVersions);
        var error = await Assert.ThrowsAsync<BridgeException>(() =>
            client.RunCSharpQueryAsync(Request(), TimeSpan.FromSeconds(3), CancellationToken.None));
        Assert.Equal(BridgeErrorCodes.ProtocolIncompatible, error.ErrorCode);
        Assert.Equal(0, query.Calls);
    }

    [Fact]
    public async Task Timeout_and_caller_cancellation_follow_capability_client_behavior()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var query = new ScriptedQuery
        {
            Hold = new TaskCompletionSource<RunCSharpQueryResult>(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        await using var context = await StartAsync(query);
        await using var client = await ConnectAsync(context, BridgeProtocol.SupportedVersions);
        var timeout = await Assert.ThrowsAsync<BridgeException>(() =>
            client.RunCSharpQueryAsync(Request(), TimeSpan.FromMilliseconds(200), CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(CapabilityErrorCodes.ExecutionTimeout, timeout.ErrorCode);
        query.Hold.TrySetCanceled();

        var second = new ScriptedQuery
        {
            Hold = new TaskCompletionSource<RunCSharpQueryResult>(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        await using var cancelledHost = await StartAsync(second);
        await using var cancelledClient = await ConnectAsync(cancelledHost, BridgeProtocol.SupportedVersions);
        using var cts = new CancellationTokenSource();
        var pending = cancelledClient.RunCSharpQueryAsync(Request(), TimeSpan.FromSeconds(10), cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
        second.Hold.TrySetCanceled();
    }

    private static RunCSharpQueryRequest Request() => new() { DocumentId = "doc", Source = "return 7;" };

    private static async Task<NamedPipeBridgeClient> ConnectAsync(HostContext context, IReadOnlyList<int> versions)
    {
        var client = await NamedPipeBridgeClient.ConnectAsync(context.Host.PipeName, TimeSpan.FromSeconds(3), CancellationToken.None);
        var handshake = await client.HandshakeAsync(new BridgeHandshakeRequest
        {
            ExpectedInstanceId = context.Metadata.InstanceId,
            SupportedProtocolVersions = versions,
            ClientName = "RevitMCP.Tests"
        }, CancellationToken.None);
        Assert.Equal(versions[0], handshake.SelectedProtocolVersion);
        return client;
    }

    private static async Task<HostContext> StartAsync(IRevitRunCSharpQueryService query, bool includeQuery = true)
    {
        var process = Process.GetCurrentProcess();
        var metadata = TestSupport.CreateMetadata(
            processId: process.Id,
            startTime: new DateTimeOffset(process.StartTime).ToUniversalTime(),
            sessionId: process.SessionId);
        var store = new FileRegistrationStore(Path.Combine(Path.GetTempPath(), "RevitMCP.Tests", Guid.NewGuid().ToString("N")));
        var host = await NamedPipeBridgeHost.StartAsync(
            metadata,
            store,
            new FakeCapabilityService(),
            new FakeQueryElementsService(),
            new FakeGetElementsService(),
            new FakeDescribeParametersService(),
            new FakeGetParameterValuesService(),
            new FakeGetMepTopologyService(),
            new FakePreviewParameterUpdatesService(),
            new FakeRequestParameterUpdateReviewService(),
            new UnusedApply(),
            new UnusedWarnings(),
            new UnusedSelection(),
            new UnusedMepPreview(),
            new UnusedMepReview(),
            new UnusedMepApply(),
            new UnusedRooms(),
            new UnusedDeviceTypes(),
            new LinkedPatterns(),
            new UnusedSpatial(),
            new UnusedFamilyTypes(),
            new UnusedBindings(),
            new UnusedRead(),
            includeQuery ? query : null,
            CancellationToken.None);
        return new HostContext(host, host.Metadata);
    }

    private sealed class HostContext(NamedPipeBridgeHost host, BridgeInstanceMetadata metadata) : IAsyncDisposable
    {
        public NamedPipeBridgeHost Host { get; } = host;
        public BridgeInstanceMetadata Metadata { get; } = metadata;
        public ValueTask DisposeAsync() => Host.DisposeAsync();
    }

    private sealed class ScriptedQuery : IRevitRunCSharpQueryService
    {
        public int Calls { get; private set; }
        public BridgeException? Error { get; set; }
        public TaskCompletionSource<RunCSharpQueryResult>? Hold { get; set; }

        public Task<RunCSharpQueryResult> RunCSharpQueryAsync(RunCSharpQueryRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            if (Hold is not null)
            {
                return Hold.Task.WaitAsync(cancellationToken);
            }

            if (Error is not null)
            {
                throw Error;
            }

            using var document = JsonDocument.Parse("7");
            return Task.FromResult(new RunCSharpQueryResult
            {
                Result = document.RootElement.Clone(),
                SourceSha256 = "hash",
                CacheKey = "cache",
                PolicyVersion = 1,
                RevitApi = new RunCSharpQueryApiIdentity { Name = "RevitAPI", Version = "26.5.0.0" },
                CompileOutcome = "success",
                DurationMs = 1,
                ResultCharacters = 1
            });
        }
    }

    private sealed class UnusedApply : IRevitApplyParameterUpdatesService
    {
        public Task<ApplyParameterUpdatesResult> ApplyParameterUpdatesAsync(ApplyParameterUpdatesRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class UnusedWarnings : IRevitGetWarningsService
    {
        public Task<GetWarningsResult> GetWarningsAsync(GetWarningsRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class UnusedSelection : IRevitGetSelectionService
    {
        public Task<GetSelectionResult> GetSelectionAsync(GetSelectionRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class UnusedMepPreview : IRevitPreviewMepDevicePlacementsService
    {
        public Task<PreviewMepDevicePlacementsResult> PreviewMepDevicePlacementsAsync(PreviewMepDevicePlacementsRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class UnusedMepReview : IRevitRequestMepDevicePlacementReviewService
    {
        public Task<RequestMepDevicePlacementReviewResult> RequestMepDevicePlacementReviewAsync(RequestMepDevicePlacementReviewRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class UnusedMepApply : IRevitApplyMepDevicePlacementsService
    {
        public Task<ApplyMepDevicePlacementsResult> ApplyMepDevicePlacementsAsync(ApplyMepDevicePlacementsRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class UnusedRooms : IRevitGetRoomLayoutsService
    {
        public Task<GetRoomLayoutsResult> GetRoomLayoutsAsync(GetRoomLayoutsRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class UnusedDeviceTypes : IRevitGetMepDeviceTypesService
    {
        public Task<GetMepDeviceTypesResult> GetMepDeviceTypesAsync(GetMepDeviceTypesRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class LinkedPatterns : IRevitPreviewRoomPatternPlacementsService
    {
        public bool SupportsLinkedRooms => true;
        public Task<PreviewRoomPatternPlacementsResult> PreviewRoomPatternPlacementsAsync(PreviewRoomPatternPlacementsRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class UnusedSpatial : IRevitReadSpatialRoomsService
    {
        public Task<ReadSpatialRoomsResult> ReadSpatialRoomsAsync(ReadSpatialRoomsRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class UnusedFamilyTypes : IRevitQueryFamilyTypesService
    {
        public Task<QueryFamilyTypesResult> QueryFamilyTypesAsync(QueryFamilyTypesRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class UnusedBindings : IRevitQueryParameterBindingsService
    {
        public Task<QueryParameterBindingsResult> QueryParameterBindingsAsync(QueryParameterBindingsRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class UnusedRead : IRevitReadOperationsService
    {
        public Task<LookupReadOperationsResult> LookupReadOperationsAsync(LookupReadOperationsRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ReadOperationResult> ReadOperationAsync(ReadOperationRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
