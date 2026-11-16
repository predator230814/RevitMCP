using System.Diagnostics;
using System.Text.Json;
using BimIt.Revit.Bridge;
using BimIt.Revit.Bridge.Implementation;
using BimIt.Revit.Contracts;
using StreamJsonRpc;
using Xunit;

namespace BimIt.Revit.Bridge.Tests;

/// <summary>
/// BRIDGE-0020: protocol 24 read-operation RPCs, capability-dependent advertisement, and no fallback.
/// </summary>
public sealed class ReadOperationsBridgeTests
{
    [Theory]
    [InlineData(23)]
    [InlineData(25)]
    public async Task Versions_other_than_24_reject_both_rpcs_before_the_service(int version)
    {
        var read = new RecordingReadOperations();
        var bindings = new RecordingBindings();
        var familyTypes = new RecordingFamilyTypes();
        var adapter = await AdapterAsync(version, read, bindings, familyTypes);

        var lookup = await Assert.ThrowsAsync<LocalRpcException>(() =>
            adapter.LookupReadOperationsAsync(new LookupReadOperationsRequest(), CancellationToken.None));
        var operation = await Assert.ThrowsAsync<LocalRpcException>(() =>
            adapter.ReadOperationAsync(ReadRequest(), CancellationToken.None));

        Assert.Equal(BridgeErrorCodes.ProtocolIncompatible, Code(lookup));
        Assert.Equal(BridgeErrorCodes.ProtocolIncompatible, Code(operation));
        Assert.Equal(0, read.LookupCalls);
        Assert.Equal(0, read.ReadCalls);
        Assert.Equal(0, bindings.Calls);
        Assert.Equal(0, familyTypes.Calls);
    }

    [Fact]
    public async Task Version_24_invokes_exactly_the_read_operation_service()
    {
        var read = new RecordingReadOperations();
        var bindings = new RecordingBindings();
        var familyTypes = new RecordingFamilyTypes();
        var adapter = await AdapterAsync(24, read, bindings, familyTypes);

        var lookup = await adapter.LookupReadOperationsAsync(
            new LookupReadOperationsRequest { Query = "Capacité" },
            CancellationToken.None);
        var result = await adapter.ReadOperationAsync(ReadRequest(), CancellationToken.None);

        Assert.Equal(1, read.LookupCalls);
        Assert.Equal(1, read.ReadCalls);
        Assert.Equal(0, bindings.Calls);
        Assert.Equal(0, familyTypes.Calls);
        Assert.Equal("Capacité", read.LastLookup!.Query);
        Assert.Equal("étage", result.Result.GetProperty("label").GetString());
    }

    [Fact]
    public async Task Version_24_without_the_service_is_protocol_incompatible_and_does_not_fall_back()
    {
        var bindings = new RecordingBindings();
        var familyTypes = new RecordingFamilyTypes();
        var adapter = await AdapterAsync(24, read: null, bindings, familyTypes);

        var lookup = await Assert.ThrowsAsync<LocalRpcException>(() =>
            adapter.LookupReadOperationsAsync(new LookupReadOperationsRequest(), CancellationToken.None));
        var operation = await Assert.ThrowsAsync<LocalRpcException>(() =>
            adapter.ReadOperationAsync(ReadRequest(), CancellationToken.None));

        Assert.Equal(BridgeErrorCodes.ProtocolIncompatible, Code(lookup));
        Assert.Equal(BridgeErrorCodes.ProtocolIncompatible, Code(operation));
        Assert.Contains("revit.lookup_read_operations", lookup.Message, StringComparison.Ordinal);
        Assert.Contains("revit.read_operation", operation.Message, StringComparison.Ordinal);
        Assert.Equal(0, bindings.Calls);
        Assert.Equal(0, familyTypes.Calls);
    }

    [Fact]
    public async Task Expected_errors_pass_through_and_unexpected_errors_stay_compact()
    {
        var expected = new RecordingReadOperations
        {
            Error = new BridgeException(CapabilityErrorCodes.UnknownReadOperation, "No read operation with this id is registered.")
        };
        var expectedAdapter = await AdapterAsync(24, expected, null, null);
        var rejected = await Assert.ThrowsAsync<LocalRpcException>(() =>
            expectedAdapter.LookupReadOperationsAsync(new LookupReadOperationsRequest(), CancellationToken.None));
        Assert.Equal(CapabilityErrorCodes.UnknownReadOperation, Code(rejected));

        var failed = new RecordingReadOperations { Error = new InvalidOperationException(@"C:\secret\pipe machine-name") };
        var failedAdapter = await AdapterAsync(24, failed, null, null);
        var lookup = await Assert.ThrowsAsync<LocalRpcException>(() =>
            failedAdapter.LookupReadOperationsAsync(new LookupReadOperationsRequest(), CancellationToken.None));
        var read = await Assert.ThrowsAsync<LocalRpcException>(() =>
            failedAdapter.ReadOperationAsync(ReadRequest(), CancellationToken.None));
        Assert.Equal(CapabilityErrorCodes.ExecutionFailed, Code(lookup));
        Assert.Equal(CapabilityErrorCodes.ExecutionFailed, Code(read));
        Assert.Equal("The read operation lookup could not be read.", Message(lookup));
        Assert.Equal("The read operation could not be read.", Message(read));
        Assert.DoesNotContain("secret", lookup.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", read.Message, StringComparison.Ordinal);

        var canceled = new RecordingReadOperations { Error = new OperationCanceledException() };
        var canceledAdapter = await AdapterAsync(24, canceled, null, null);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            canceledAdapter.LookupReadOperationsAsync(new LookupReadOperationsRequest(), CancellationToken.None));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            canceledAdapter.ReadOperationAsync(ReadRequest(), CancellationToken.None));
    }

    [Fact]
    public async Task No_handshake_rejects_both_rpcs()
    {
        var read = new RecordingReadOperations();
        var adapter = new StreamJsonRpcBridgeAdapter(new Selected(24), new FakeCapabilityService(), readOperations: read);

        var lookup = await Assert.ThrowsAsync<LocalRpcException>(() =>
            adapter.LookupReadOperationsAsync(new LookupReadOperationsRequest(), CancellationToken.None));
        var operation = await Assert.ThrowsAsync<LocalRpcException>(() =>
            adapter.ReadOperationAsync(ReadRequest(), CancellationToken.None));

        Assert.Equal(BridgeErrorCodes.HandshakeFailed, Code(lookup));
        Assert.Equal(BridgeErrorCodes.HandshakeFailed, Code(operation));
        Assert.Equal(0, read.LookupCalls);
        Assert.Equal(0, read.ReadCalls);
    }

    [Fact]
    public async Task Protocol_24_still_serves_parameter_bindings_family_types_and_context()
    {
        var read = new RecordingReadOperations();
        var bindings = new RecordingBindings();
        var familyTypes = new RecordingFamilyTypes();
        var capability = new FakeCapabilityService();
        var adapter = new StreamJsonRpcBridgeAdapter(
            new Selected(24),
            capability,
            queryFamilyTypes: familyTypes,
            queryParameterBindings: bindings,
            readOperations: read);
        await adapter.HandshakeAsync(Handshake(24), CancellationToken.None);

        await adapter.QueryParameterBindingsAsync(new QueryParameterBindingsRequest { DocumentId = "doc" }, CancellationToken.None);
        await adapter.QueryFamilyTypesAsync(new QueryFamilyTypesRequest { DocumentId = "doc" }, CancellationToken.None);
        await adapter.GetContextAsync(new GetContextRequest(), CancellationToken.None);

        Assert.Equal(1, bindings.Calls);
        Assert.Equal(1, familyTypes.Calls);
        Assert.Equal(1, capability.InvokeCount);
        Assert.Equal(0, read.LookupCalls);
        Assert.Equal(0, read.ReadCalls);
    }

    [Fact]
    public void Advertisement_requires_the_read_service_and_the_complete_v23_prefix()
    {
        var metadata = TestSupport.CreateMetadata();
        var read = new RecordingReadOperations();

        var full = Advertise(metadata, read);
        Assert.Equal(BridgeProtocol.SupportedVersions, full.SupportedProtocolVersions);
        Assert.Equal(BridgeProtocol.ReadOperationsVersions, full.SupportedProtocolVersions);
        Assert.Equal(24, full.SupportedProtocolVersions[0]);

        var missingRead = Advertise(metadata, readOperations: null);
        Assert.Equal(BridgeProtocol.QueryParameterBindingsVersions, missingRead.SupportedProtocolVersions);
        Assert.Equal(23, missingRead.SupportedProtocolVersions[0]);
        Assert.DoesNotContain(24, missingRead.SupportedProtocolVersions);

        var missingBindings = Advertise(metadata, read, useBindings: false);
        Assert.Equal(BridgeProtocol.QueryFamilyTypesVersions, missingBindings.SupportedProtocolVersions);
        Assert.DoesNotContain(24, missingBindings.SupportedProtocolVersions);

        var missingFamilyTypes = Advertise(metadata, read, useFamilyTypes: false);
        Assert.Equal(BridgeProtocol.LinkedRoomPatternPreviewVersions, missingFamilyTypes.SupportedProtocolVersions);
        Assert.DoesNotContain(24, missingFamilyTypes.SupportedProtocolVersions);

        var hostOnlyPreview = Advertise(metadata, read, linkedRooms: false);
        Assert.Equal(BridgeProtocol.ReadSpatialRoomsVersions, hostOnlyPreview.SupportedProtocolVersions);
        Assert.DoesNotContain(24, hostOnlyPreview.SupportedProtocolVersions);

        var noCapability = Advertise(metadata, read, useCapability: false);
        Assert.Equal(BridgeProtocol.HandshakeOnlyVersions, noCapability.SupportedProtocolVersions);
        Assert.DoesNotContain(24, noCapability.SupportedProtocolVersions);
    }

    [Fact]
    public async Task The_named_pipe_client_round_trips_both_operations_on_24_and_refuses_23()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var read = new RecordingReadOperations();
        await using var context = await StartHostAsync(read);
        await using var client = await ConnectAsync(context, BridgeProtocol.ReadOperationsVersions);

        var lookup = await client.LookupReadOperationsAsync(
            new LookupReadOperationsRequest { Query = "Capacité", Offset = 0, Limit = 25 },
            TimeSpan.FromSeconds(5),
            CancellationToken.None);
        var result = await client.ReadOperationAsync(ReadRequest(), TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.Equal("Capacité", read.LastLookup!.Query);
        Assert.Equal(0, read.LastLookup.Offset);
        Assert.Equal(25, read.LastLookup.Limit);
        Assert.Equal("Capacité", Assert.Single(lookup.Operations).Title);
        Assert.Equal("doc-24", read.LastRead!.DocumentId);
        Assert.Equal("étage", read.LastRead.Inputs!.Value.GetProperty("name_contains").GetString());
        Assert.Equal("étage", result.Result.GetProperty("label").GetString());
        Assert.Equal("""{"name_contains":"étage"}""", read.LastRead.Inputs.Value.GetRawText());

        await using var older = await ConnectAsync(context, BridgeProtocol.QueryParameterBindingsVersions);
        var rejected = await Assert.ThrowsAsync<BridgeException>(() =>
            older.LookupReadOperationsAsync(new LookupReadOperationsRequest(), TimeSpan.FromSeconds(5), CancellationToken.None));
        var rejectedRead = await Assert.ThrowsAsync<BridgeException>(() =>
            older.ReadOperationAsync(ReadRequest(), TimeSpan.FromSeconds(5), CancellationToken.None));
        Assert.Equal(BridgeErrorCodes.ProtocolIncompatible, rejected.ErrorCode);
        Assert.Equal(BridgeErrorCodes.ProtocolIncompatible, rejectedRead.ErrorCode);
        Assert.Equal(1, read.LookupCalls);
        Assert.Equal(1, read.ReadCalls);
    }

    private static async Task<StreamJsonRpcBridgeAdapter> AdapterAsync(
        int version,
        IRevitReadOperationsService? read,
        IRevitQueryParameterBindingsService? bindings,
        IRevitQueryFamilyTypesService? familyTypes)
    {
        var adapter = new StreamJsonRpcBridgeAdapter(
            new Selected(version),
            new FakeCapabilityService(),
            queryFamilyTypes: familyTypes,
            queryParameterBindings: bindings,
            readOperations: read);
        await adapter.HandshakeAsync(Handshake(version), CancellationToken.None);
        return adapter;
    }

    private static BridgeInstanceMetadata Advertise(
        BridgeInstanceMetadata metadata,
        IRevitReadOperationsService? readOperations,
        IRevitQueryParameterBindingsService? bindings = null,
        bool useBindings = true,
        IRevitQueryFamilyTypesService? familyTypes = null,
        bool useFamilyTypes = true,
        bool linkedRooms = true,
        IRevitCapabilityService? capability = null,
        bool useCapability = true)
    {
        return NamedPipeBridgeHost.WithAdvertisedProtocol(
            metadata,
            useCapability ? capability ?? new FakeCapabilityService() : null,
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
            new DeviceTypes(),
            new RoomPattern(linkedRooms),
            new SpatialRooms(),
            useFamilyTypes ? familyTypes ?? new RecordingFamilyTypes() : null,
            useBindings ? bindings ?? new RecordingBindings() : null,
            readOperations);
    }

    private static async Task<HostContext> StartHostAsync(IRevitReadOperationsService read)
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
            new DeviceTypes(),
            new RoomPattern(true),
            new SpatialRooms(),
            new RecordingFamilyTypes(),
            new RecordingBindings(),
            read,
            CancellationToken.None);
        Assert.Equal(BridgeProtocol.ReadOperationsVersions, host.Metadata.SupportedProtocolVersions);
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

    private static ReadOperationRequest ReadRequest()
    {
        return new ReadOperationRequest
        {
            DocumentId = "doc-24",
            Operation = "project.user_worksets",
            ContractVersion = 1,
            Inputs = Parse("""{"name_contains":"étage"}""")
        };
    }

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

    private static string Message(LocalRpcException exception) => Assert.IsType<BridgeError>(exception.ErrorData).Message;

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class RecordingReadOperations : IRevitReadOperationsService
    {
        public int LookupCalls { get; private set; }

        public int ReadCalls { get; private set; }

        public LookupReadOperationsRequest? LastLookup { get; private set; }

        public ReadOperationRequest? LastRead { get; private set; }

        public Exception? Error { get; init; }

        public Task<LookupReadOperationsResult> LookupReadOperationsAsync(
            LookupReadOperationsRequest request,
            CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            LookupCalls++;
            LastLookup = request;
            if (Error is not null)
            {
                throw Error;
            }

            return Task.FromResult(new LookupReadOperationsResult
            {
                Mode = "search",
                Total = 1,
                ReturnedCount = 1,
                NextOffset = null,
                Operations =
                [
                    new ReadOperationDescriptor
                    {
                        Id = "project.user_worksets",
                        ContractVersion = 1,
                        Title = request.Query ?? "User worksets",
                        Description = "User worksets",
                        Context = "document",
                        Effects = "read",
                        Pagination = "none"
                    }
                ]
            });
        }

        public Task<ReadOperationResult> ReadOperationAsync(
            ReadOperationRequest request,
            CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            ReadCalls++;
            LastRead = request;
            if (Error is not null)
            {
                throw Error;
            }

            return Task.FromResult(new ReadOperationResult
            {
                Context = new ReadOperationContext { InstanceId = "any", DocumentId = request.DocumentId ?? "doc" },
                Operation = request.Operation ?? "project.user_worksets",
                ContractVersion = request.ContractVersion ?? 1,
                Result = Parse("""{"label":"étage"}"""),
                Complete = true,
                NextCursor = null
            });
        }
    }

    private sealed class RecordingBindings : IRevitQueryParameterBindingsService
    {
        public int Calls { get; private set; }

        public Task<QueryParameterBindingsResult> QueryParameterBindingsAsync(
            QueryParameterBindingsRequest request,
            CancellationToken cancellationToken)
        {
            _ = request;
            _ = cancellationToken;
            Calls++;
            return Task.FromResult(new QueryParameterBindingsResult
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
            });
        }
    }

    private sealed class RecordingFamilyTypes : IRevitQueryFamilyTypesService
    {
        public int Calls { get; private set; }

        public Task<QueryFamilyTypesResult> QueryFamilyTypesAsync(
            QueryFamilyTypesRequest request,
            CancellationToken cancellationToken)
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

    private sealed class DeviceTypes : IRevitGetMepDeviceTypesService
    {
        public Task<GetMepDeviceTypesResult> GetMepDeviceTypesAsync(GetMepDeviceTypesRequest request, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class SpatialRooms : IRevitReadSpatialRoomsService
    {
        public Task<ReadSpatialRoomsResult> ReadSpatialRoomsAsync(ReadSpatialRoomsRequest request, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class RoomPattern : IRevitPreviewRoomPatternPlacementsService
    {
        private readonly bool _linked;

        public RoomPattern(bool linked) => _linked = linked;

        bool IRevitPreviewRoomPatternPlacementsService.SupportsLinkedRooms => _linked;

        public Task<PreviewRoomPatternPlacementsResult> PreviewRoomPatternPlacementsAsync(
            PreviewRoomPatternPlacementsRequest request,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class HostContext(NamedPipeBridgeHost host, BridgeInstanceMetadata metadata) : IAsyncDisposable
    {
        public NamedPipeBridgeHost Host { get; } = host;

        public BridgeInstanceMetadata Metadata { get; } = metadata;

        public ValueTask DisposeAsync() => Host.DisposeAsync();
    }
}
