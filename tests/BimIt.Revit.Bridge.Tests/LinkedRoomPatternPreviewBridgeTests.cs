using BimIt.Revit.Bridge;
using BimIt.Revit.Bridge.Implementation;
using BimIt.Revit.Contracts;
using StreamJsonRpc;
using Xunit;

namespace BimIt.Revit.Bridge.Tests;

public sealed class LinkedRoomPatternPreviewBridgeTests
{
    private static readonly string LinkedRoom = SpatialRoomRefCodec.Link("link-unique", "room-unique");

    [Fact]
    public void Full_host_advertises_21_only_when_preview_supports_linked_rooms()
    {
        var metadata = TestSupport.CreateMetadata();
        var linked = Advertise(metadata, new StubRoomPatternPreview { Linked = true });
        var hostOnly = Advertise(metadata, new StubRoomPatternPreview { Linked = false });

        Assert.Equal(BridgeProtocol.LinkedRoomPatternPreviewVersions, linked.SupportedProtocolVersions);
        Assert.Equal(21, linked.SupportedProtocolVersions[0]);
        Assert.Equal(BridgeProtocol.ReadSpatialRoomsVersions, hostOnly.SupportedProtocolVersions);
        Assert.Equal(20, hostOnly.SupportedProtocolVersions[0]);
        Assert.DoesNotContain(21, hostOnly.SupportedProtocolVersions);
    }

    [Theory]
    [InlineData(19)]
    [InlineData(20)]
    public async Task Linked_room_ref_is_refused_before_protocol_21(int version)
    {
        var preview = new StubRoomPatternPreview { Linked = true };
        var adapter = await AdapterAsync(preview, version);

        var rejected = await Assert.ThrowsAsync<LocalRpcException>(() =>
            adapter.PreviewRoomPatternPlacementsAsync(Request(LinkedRoom), CancellationToken.None));

        Assert.Equal(CapabilityErrorCodes.InvalidRoomPatternPlacement, Assert.IsType<BridgeError>(rejected.ErrorData).Code);
        Assert.Equal(0, preview.InvokeCount);

        await adapter.PreviewRoomPatternPlacementsAsync(Request("host-room-unique"), CancellationToken.None);
        Assert.Equal(1, preview.InvokeCount);
    }

    [Fact]
    public async Task Protocol_21_forwards_a_linked_room_ref_unchanged()
    {
        var preview = new StubRoomPatternPreview { Linked = true };
        var adapter = await AdapterAsync(preview, 21);

        await adapter.PreviewRoomPatternPlacementsAsync(Request(LinkedRoom), CancellationToken.None);

        Assert.Equal(1, preview.InvokeCount);
        Assert.Equal(LinkedRoom, preview.LastRoomRef);
    }

    [Fact]
    public async Task Unknown_v22_does_not_preview()
    {
        var preview = new StubRoomPatternPreview { Linked = true };
        var adapter = await AdapterAsync(preview, 22);

        var rejected = await Assert.ThrowsAsync<LocalRpcException>(() =>
            adapter.PreviewRoomPatternPlacementsAsync(Request("host-room-unique"), CancellationToken.None));

        Assert.Equal(BridgeErrorCodes.ProtocolIncompatible, Assert.IsType<BridgeError>(rejected.ErrorData).Code);
        Assert.Equal(0, preview.InvokeCount);
    }

    private static BridgeInstanceMetadata Advertise(BridgeInstanceMetadata metadata, IRevitPreviewRoomPatternPlacementsService preview)
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
            preview,
            new StubSpatialRooms());
    }

    private static async Task<StreamJsonRpcBridgeAdapter> AdapterAsync(IRevitPreviewRoomPatternPlacementsService preview, int version)
    {
        var adapter = new StreamJsonRpcBridgeAdapter(
            new Selected(version),
            new FakeCapabilityService(),
            previewRoomPatternPlacements: preview);
        await adapter.HandshakeAsync(
            new BridgeHandshakeRequest
            {
                ExpectedInstanceId = "any",
                SupportedProtocolVersions = [version],
                ClientName = "RevitMCP.Tests"
            },
            CancellationToken.None);
        return adapter;
    }

    private static PreviewRoomPatternPlacementsRequest Request(string roomRef)
    {
        return new PreviewRoomPatternPlacementsRequest
        {
            DocumentId = "doc",
            Placements =
            [
                new PreviewRoomPatternPlacement
                {
                    RoomRef = roomRef,
                    FamilyTypeRef = "type",
                    Role = "receptacle",
                    PlacementKind = "boundary",
                    XMetres = 1,
                    YMetres = 1,
                    MountingHeightMetres = 0.3,
                },
            ],
        };
    }

    private sealed class Selected : IRevitBridgeService
    {
        private readonly int _selected;

        public Selected(int selected) => _selected = selected;

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

    private sealed class StubRoomPatternPreview : IRevitPreviewRoomPatternPlacementsService
    {
        public bool Linked { get; init; }

        public int InvokeCount { get; private set; }

        public string? LastRoomRef { get; private set; }

        bool IRevitPreviewRoomPatternPlacementsService.SupportsLinkedRooms => Linked;

        public Task<PreviewRoomPatternPlacementsResult> PreviewRoomPatternPlacementsAsync(
            PreviewRoomPatternPlacementsRequest request,
            CancellationToken cancellationToken)
        {
            InvokeCount++;
            LastRoomRef = request.Placements![0].RoomRef;
            return Task.FromResult(new PreviewRoomPatternPlacementsResult
            {
                Context = new PreviewRoomPatternPlacementsContext { InstanceId = "instance", DocumentId = "doc" },
                LengthUnit = PreviewRoomPatternPlacementsResult.Metres,
                Ready = false,
                RoomCount = 1,
                Items = [],
            });
        }
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
}
