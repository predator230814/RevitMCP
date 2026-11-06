using System.Diagnostics;
using BimIt.Revit.Bridge;
using BimIt.Revit.Bridge.Implementation;
using BimIt.Revit.Contracts;
using StreamJsonRpc;
using Xunit;

namespace BimIt.Revit.Bridge.Tests;

/// <summary>
/// BRIDGE-0019: <c>revit.query_parameter_bindings</c> on protocol 23 only, v23 preserving the
/// complete v22 prefix, no fallback, and the real CAP-0015 contracts over a named pipe.
/// </summary>
public sealed class QueryParameterBindingsBridgeTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(21)]
    [InlineData(22)]
    [InlineData(24)]
    public async Task Versions_other_than_23_refuse_the_rpc_without_invoking_the_service(int version)
    {
        var bindings = new RecordingBindings();
        var familyTypes = new RecordingFamilyTypes();
        var adapter = await AdapterAsync(version, bindings, familyTypes);

        var rejected = await Assert.ThrowsAsync<LocalRpcException>(() =>
            adapter.QueryParameterBindingsAsync(Request(), CancellationToken.None));

        Assert.Equal(BridgeErrorCodes.ProtocolIncompatible, Code(rejected));
        Assert.Equal(0, bindings.Calls);
        Assert.Equal(0, familyTypes.Calls);
    }

    [Fact]
    public async Task Version_23_invokes_exactly_the_parameter_binding_service()
    {
        var bindings = new RecordingBindings();
        var familyTypes = new RecordingFamilyTypes();
        var describe = new FakeDescribeParametersService();
        var values = new FakeGetParameterValuesService();
        var adapter = new StreamJsonRpcBridgeAdapter(
            new Selected(23),
            new FakeCapabilityService(),
            describeParameters: describe,
            getParameterValues: values,
            queryFamilyTypes: familyTypes,
            queryParameterBindings: bindings);
        await adapter.HandshakeAsync(Handshake(23), CancellationToken.None);

        var result = await adapter.QueryParameterBindingsAsync(Request(), CancellationToken.None);

        Assert.Equal(1, bindings.Calls);
        Assert.Equal(0, familyTypes.Calls);
        Assert.Equal("doc", bindings.LastRequest!.DocumentId);
        Assert.True(result.Complete);
        Assert.Null(result.NextCursor);
    }

    [Fact]
    public async Task Version_23_without_the_service_is_protocol_incompatible_and_does_not_fall_back()
    {
        var familyTypes = new RecordingFamilyTypes();
        var adapter = await AdapterAsync(23, bindings: null, familyTypes);

        var rejected = await Assert.ThrowsAsync<LocalRpcException>(() =>
            adapter.QueryParameterBindingsAsync(Request(), CancellationToken.None));

        Assert.Equal(BridgeErrorCodes.ProtocolIncompatible, Code(rejected));
        Assert.Equal(0, familyTypes.Calls);
    }

    [Theory]
    [InlineData(22)]
    [InlineData(23)]
    public async Task Query_family_types_stays_available_on_22_and_23(int version)
    {
        var bindings = new RecordingBindings();
        var familyTypes = new RecordingFamilyTypes();
        var adapter = await AdapterAsync(version, bindings, familyTypes);

        await adapter.QueryFamilyTypesAsync(new QueryFamilyTypesRequest { DocumentId = "doc" }, CancellationToken.None);

        Assert.Equal(1, familyTypes.Calls);
        Assert.Equal(0, bindings.Calls);
    }

    [Fact]
    public async Task Capability_errors_unexpected_failures_and_cancellation_follow_bridge_conventions()
    {
        var invalid = new RecordingBindings { Error = new BridgeException(CapabilityErrorCodes.StaleCursor, "stale") };
        var stale = await Assert.ThrowsAsync<LocalRpcException>(async () =>
            await (await AdapterAsync(23, invalid, null)).QueryParameterBindingsAsync(Request(), CancellationToken.None));
        Assert.Equal(CapabilityErrorCodes.StaleCursor, Code(stale));

        var crash = new RecordingBindings { Error = new InvalidOperationException("C:\\secret\\path boom") };
        var failed = await Assert.ThrowsAsync<LocalRpcException>(async () =>
            await (await AdapterAsync(23, crash, null)).QueryParameterBindingsAsync(Request(), CancellationToken.None));
        Assert.Equal(CapabilityErrorCodes.ExecutionFailed, Code(failed));
        Assert.Equal("The parameter binding query could not be read.", Assert.IsType<BridgeError>(failed.ErrorData).Message);
        Assert.DoesNotContain("secret", failed.Message, StringComparison.Ordinal);

        var canceled = new RecordingBindings { Error = new OperationCanceledException() };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await (await AdapterAsync(23, canceled, null)).QueryParameterBindingsAsync(Request(), CancellationToken.None));
    }

    [Fact]
    public void A_full_v22_prefix_with_the_service_advertises_23_and_without_it_stays_on_22()
    {
        var metadata = TestSupport.CreateMetadata();

        var full = Advertise(metadata, new RecordingFamilyTypes(), new RecordingBindings());
        Assert.Equal(BridgeProtocol.SupportedVersions, full.SupportedProtocolVersions);
        Assert.Equal(
            new[] { 23, 22, 21, 20, 19, 18, 17, 16, 15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1 },
            full.SupportedProtocolVersions);

        var withoutBindings = Advertise(metadata, new RecordingFamilyTypes(), bindings: null);
        Assert.Equal(BridgeProtocol.QueryFamilyTypesVersions, withoutBindings.SupportedProtocolVersions);
        Assert.Equal(22, withoutBindings.SupportedProtocolVersions[0]);
        Assert.DoesNotContain(23, withoutBindings.SupportedProtocolVersions);
    }

    [Fact]
    public void Parameter_bindings_without_the_complete_v22_prefix_never_advertise_23()
    {
        var metadata = TestSupport.CreateMetadata();

        var noFamilyTypes = Advertise(metadata, familyTypes: null, new RecordingBindings());
        Assert.Equal(BridgeProtocol.LinkedRoomPatternPreviewVersions, noFamilyTypes.SupportedProtocolVersions);
        Assert.DoesNotContain(23, noFamilyTypes.SupportedProtocolVersions);

        var hostOnlyPreview = Advertise(metadata, new RecordingFamilyTypes(), new RecordingBindings(), linkedRooms: false);
        Assert.Equal(BridgeProtocol.ReadSpatialRoomsVersions, hostOnlyPreview.SupportedProtocolVersions);
        Assert.DoesNotContain(23, hostOnlyPreview.SupportedProtocolVersions);

        var bindingsOnly = NamedPipeBridgeHost.WithAdvertisedProtocol(
            metadata,
            new FakeCapabilityService(),
            queryParameterBindings: new RecordingBindings());
        Assert.Equal(BridgeProtocol.GetContextVersions, bindingsOnly.SupportedProtocolVersions);
        Assert.DoesNotContain(23, bindingsOnly.SupportedProtocolVersions);
    }

    [Fact]
    public async Task The_named_pipe_client_round_trips_the_real_CAP_0015_contracts_on_23()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var bindings = new RecordingBindings { Result = UnicodeResult() };
        await using var context = await StartHostAsync(bindings);
        await using var client = await ConnectAsync(context, BridgeProtocol.SupportedVersions);

        var request = new QueryParameterBindingsRequest
        {
            DocumentId = "doc",
            NameContains = "Capacité",
            IdentityKind = "shared",
            BindingKind = "instance",
            CategoryTokens = ["OST_ElectricalFixtures"],
            PageSize = 50,
            Cursor = null
        };
        var result = await client.QueryParameterBindingsAsync(request, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.Equal(1, bindings.Calls);
        var received = bindings.LastRequest!;
        Assert.Equal("doc", received.DocumentId);
        Assert.Equal("Capacité", received.NameContains);
        Assert.Equal("shared", received.IdentityKind);
        Assert.Equal("instance", received.BindingKind);
        Assert.Equal(["OST_ElectricalFixtures"], received.CategoryTokens!);
        Assert.Equal(50, received.PageSize);
        Assert.Null(received.Cursor);

        Assert.True(result.Complete);
        Assert.Null(result.NextCursor);
        Assert.Equal(1, result.Summary.SharedCount);
        var row = Assert.Single(result.Bindings);
        Assert.Equal("Tt_Capacité_Sectionneur", row.Name);
        Assert.Equal(DescribeParameterIdentityKind.Shared, row.Identity.Kind);
        Assert.Equal("857e1417-0000-0000-0000-00000000000b", row.Identity.Guid);
        Assert.Equal(DescribeParameterDataTypeKind.MeasurableSpec, row.DataType.Kind);
        Assert.Equal(QueryParameterBindingKind.Instance, row.BindingKind);
        Assert.Equal("Appareils électriques", Assert.Single(row.Categories).CategoryName);
    }

    [Fact]
    public async Task A_client_on_22_refuses_before_sending_and_the_host_service_is_not_invoked()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var bindings = new RecordingBindings();
        await using var context = await StartHostAsync(bindings);
        await using var client = await ConnectAsync(context, BridgeProtocol.QueryFamilyTypesVersions);

        var rejected = await Assert.ThrowsAsync<BridgeException>(() =>
            client.QueryParameterBindingsAsync(Request(), TimeSpan.FromSeconds(5), CancellationToken.None));

        Assert.Equal(BridgeErrorCodes.ProtocolIncompatible, rejected.ErrorCode);
        Assert.Equal(0, bindings.Calls);
    }

    private static async Task<StreamJsonRpcBridgeAdapter> AdapterAsync(
        int version,
        IRevitQueryParameterBindingsService? bindings,
        IRevitQueryFamilyTypesService? familyTypes)
    {
        var adapter = new StreamJsonRpcBridgeAdapter(
            new Selected(version),
            new FakeCapabilityService(),
            queryFamilyTypes: familyTypes,
            queryParameterBindings: bindings);
        await adapter.HandshakeAsync(Handshake(version), CancellationToken.None);
        return adapter;
    }

    private static BridgeInstanceMetadata Advertise(
        BridgeInstanceMetadata metadata,
        IRevitQueryFamilyTypesService? familyTypes,
        IRevitQueryParameterBindingsService? bindings,
        bool linkedRooms = true)
    {
        return NamedPipeBridgeHost.WithAdvertisedProtocol(
            metadata,
            new FakeCapabilityService(),
            new FakeQueryElementsService(),
            new FakeGetElementsService(),
            new FakeDescribeParametersService(),
            new FakeGetParameterValuesService(),
            new FakeGetMepTopologyService(),
            new FakePreviewParameterUpdatesService(),
            new FakeRequestParameterUpdateReviewService(),
            new FakeApplyParameterUpdatesService(),
            new FakeGetWarningsService(),
            new FakeGetSelectionService(),
            new StubPreviewMepService(),
            new StubReviewMepService(),
            new StubApplyMepService(),
            new StubGetRoomLayoutsService(),
            new StubDeviceTypes(),
            new StubRoomPatternPreview(linkedRooms),
            new StubSpatialRooms(),
            familyTypes,
            bindings);
    }

    private static async Task<HostContext> StartHostAsync(IRevitQueryParameterBindingsService bindings)
    {
        var process = Process.GetCurrentProcess();
        var metadata = TestSupport.CreateMetadata(
            processId: process.Id,
            startTime: new DateTimeOffset(process.StartTime).ToUniversalTime(),
            sessionId: process.SessionId);
        var root = Path.Combine(Path.GetTempPath(), "RevitMCP.Tests", Guid.NewGuid().ToString("N"));
        var host = await NamedPipeBridgeHost.StartAsync(
            metadata,
            new FileRegistrationStore(root),
            new FakeCapabilityService(),
            new FakeQueryElementsService(),
            new FakeGetElementsService(),
            new FakeDescribeParametersService(),
            new FakeGetParameterValuesService(),
            new FakeGetMepTopologyService(),
            new FakePreviewParameterUpdatesService(),
            new FakeRequestParameterUpdateReviewService(),
            new FakeApplyParameterUpdatesService(),
            new FakeGetWarningsService(),
            new FakeGetSelectionService(),
            new StubPreviewMepService(),
            new StubReviewMepService(),
            new StubApplyMepService(),
            new StubGetRoomLayoutsService(),
            new StubDeviceTypes(),
            new StubRoomPatternPreview(true),
            new StubSpatialRooms(),
            new RecordingFamilyTypes(),
            bindings,
            CancellationToken.None);
        Assert.Equal(BridgeProtocol.SupportedVersions, host.Metadata.SupportedProtocolVersions);
        return new HostContext(host, metadata);
    }

    private static async Task<NamedPipeBridgeClient> ConnectAsync(HostContext context, IReadOnlyList<int> versions)
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

    private static QueryParameterBindingsRequest Request() => new() { DocumentId = "doc" };

    private static BridgeHandshakeRequest Handshake(int version)
    {
        return new BridgeHandshakeRequest
        {
            ExpectedInstanceId = "any",
            SupportedProtocolVersions = [version],
            ClientName = "RevitMCP.Tests"
        };
    }

    private static string Code(LocalRpcException exception) => Assert.IsType<BridgeError>(exception.ErrorData).Code;

    private static QueryParameterBindingsResult EmptyResult()
    {
        return new QueryParameterBindingsResult
        {
            Context = new QueryParameterBindingsContext { InstanceId = "any", DocumentId = "doc" },
            Summary = new QueryParameterBindingsSummary
            {
                MatchedDefinitionCount = 0,
                SharedCount = 0,
                LocalCount = 0,
                BuiltInCount = 0,
                InstanceBoundCount = 0,
                TypeBoundCount = 0,
                DistinctCategoryCount = 0
            },
            ReturnedCount = 0,
            Complete = true,
            NextCursor = null,
            Bindings = []
        };
    }

    private static QueryParameterBindingsResult UnicodeResult()
    {
        return new QueryParameterBindingsResult
        {
            Context = new QueryParameterBindingsContext { InstanceId = "any", DocumentId = "doc" },
            Summary = new QueryParameterBindingsSummary
            {
                MatchedDefinitionCount = 1,
                SharedCount = 1,
                LocalCount = 0,
                BuiltInCount = 0,
                InstanceBoundCount = 1,
                TypeBoundCount = 0,
                DistinctCategoryCount = 1
            },
            ReturnedCount = 1,
            Complete = true,
            NextCursor = null,
            Bindings =
            [
                new QueryParameterBindingRow
                {
                    ParameterRef = "opaque-ref",
                    Name = "Tt_Capacité_Sectionneur",
                    Identity = new DescribeParameterIdentity { Kind = DescribeParameterIdentityKind.Shared, Guid = "857e1417-0000-0000-0000-00000000000b" },
                    DataType = new DescribeParameterDataType { Kind = DescribeParameterDataTypeKind.MeasurableSpec, ForgeTypeId = "autodesk.spec.aec.electrical:current-2.0.0" },
                    BindingKind = QueryParameterBindingKind.Instance,
                    Categories = [new QueryParameterBindingCategory { CategoryToken = "OST_ElectricalFixtures", CategoryName = "Appareils électriques" }]
                }
            ]
        };
    }

    private sealed class RecordingBindings : IRevitQueryParameterBindingsService
    {
        public int Calls { get; private set; }

        public QueryParameterBindingsRequest? LastRequest { get; private set; }

        public Exception? Error { get; init; }

        public QueryParameterBindingsResult? Result { get; init; }

        public Task<QueryParameterBindingsResult> QueryParameterBindingsAsync(QueryParameterBindingsRequest request, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            Calls++;
            LastRequest = request;
            if (Error is not null)
            {
                throw Error;
            }

            return Task.FromResult(Result ?? EmptyResult());
        }
    }

    private sealed class RecordingFamilyTypes : IRevitQueryFamilyTypesService
    {
        public int Calls { get; private set; }

        public Task<QueryFamilyTypesResult> QueryFamilyTypesAsync(QueryFamilyTypesRequest request, CancellationToken cancellationToken)
        {
            _ = request;
            _ = cancellationToken;
            Calls++;
            return Task.FromResult(new QueryFamilyTypesResult
            {
                Context = new QueryFamilyTypesContext { InstanceId = "any", DocumentId = "doc" },
                Summary = new QueryFamilyTypesSummary
                {
                    MatchedTypeCount = 0,
                    DistinctFamilyCount = 0,
                    PlacedTypeCount = 0,
                    PlacedInstanceCount = 0,
                    UsedFamilyCount = 0,
                    UnusedFamilyCount = 0
                },
                ReturnedCount = 0,
                Complete = true,
                NextCursor = null,
                Types = []
            });
        }
    }

    private sealed class Selected : IRevitBridgeService
    {
        private readonly int _selected;

        public Selected(int selected) => _selected = selected;

        public Task<BridgeHandshakeResult> HandshakeAsync(BridgeHandshakeRequest request, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
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
                SupportedProtocolVersions = [_selected]
            });
        }
    }

    private sealed class StubRoomPatternPreview : IRevitPreviewRoomPatternPlacementsService
    {
        private readonly bool _linked;

        public StubRoomPatternPreview(bool linked) => _linked = linked;

        bool IRevitPreviewRoomPatternPlacementsService.SupportsLinkedRooms => _linked;

        public Task<PreviewRoomPatternPlacementsResult> PreviewRoomPatternPlacementsAsync(PreviewRoomPatternPlacementsRequest request, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class StubDeviceTypes : IRevitGetMepDeviceTypesService
    {
        public Task<GetMepDeviceTypesResult> GetMepDeviceTypesAsync(GetMepDeviceTypesRequest request, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class StubSpatialRooms : IRevitReadSpatialRoomsService
    {
        public Task<ReadSpatialRoomsResult> ReadSpatialRoomsAsync(ReadSpatialRoomsRequest request, CancellationToken cancellationToken)
            => throw new NotSupportedException();
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
