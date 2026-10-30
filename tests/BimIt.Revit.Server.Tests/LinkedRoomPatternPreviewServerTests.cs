using BimIt.Revit.Contracts;
using BimIt.Revit.Server;
using Xunit;

namespace BimIt.Revit.Server.Tests;

public sealed class LinkedRoomPatternPreviewServerTests
{
    private static readonly string LinkedRoom = SpatialRoomRefCodec.Link("link-unique", "room-unique");

    [Theory]
    [InlineData(19)]
    [InlineData(20)]
    public async Task Linked_room_ref_is_refused_without_protocol_21(int selected)
    {
        var (service, factory) = Create(selected);

        var outcome = await service.ExecuteAsync(null, Request(LinkedRoom), CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(CapabilityErrorCodes.InvalidRoomPatternPlacement, outcome.ErrorCode);
        Assert.Equal(0, Assert.Single(factory.Clients).PreviewRoomPatternPlacementsCalls);
    }

    [Fact]
    public async Task Host_room_ref_still_previews_on_protocol_20()
    {
        var (service, factory) = Create(20);

        var outcome = await service.ExecuteAsync(null, Request("host-room-unique"), CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(1, Assert.Single(factory.Clients).PreviewRoomPatternPlacementsCalls);
    }

    [Fact]
    public async Task Protocol_21_forwards_the_linked_room_ref()
    {
        var (service, factory) = Create(21);

        var outcome = await service.ExecuteAsync(null, Request(LinkedRoom), CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        var client = Assert.Single(factory.Clients);
        Assert.Equal(1, client.PreviewRoomPatternPlacementsCalls);
        Assert.Equal(BridgeProtocol.SupportedVersions, client.HandshakeRequest!.SupportedProtocolVersions);
    }

    private static (PreviewRoomPatternPlacementsApplicationService Service, RecordingBridgeClientFactory Factory) Create(int selected)
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("id-1", "pipe-1", protocolVersion: selected));
        var factory = new RecordingBridgeClientFactory
        {
            Connect = (pipe, _, _) =>
            {
                var registration = TestSupport.CreateRegistration("id-1", pipe);
                return Task.FromResult(new RecordingBridgeClient
                {
                    Handshake = (_, _) => Task.FromResult(TestSupport.CreateHandshake(registration, selected)),
                    PreviewRoomPatternPlacements = _ => Task.FromResult(new PreviewRoomPatternPlacementsResult
                    {
                        Context = new PreviewRoomPatternPlacementsContext { InstanceId = "id-1", DocumentId = "doc" },
                        LengthUnit = PreviewRoomPatternPlacementsResult.Metres,
                        Ready = false,
                        RoomCount = 1,
                        Items = [],
                    }),
                });
            }
        };
        var service = new PreviewRoomPatternPlacementsApplicationService(discovery, factory, new ServerTimeouts
        {
            BootstrapTimeout = TimeSpan.FromMilliseconds(50),
            CapabilityTimeout = TimeSpan.FromMilliseconds(75)
        });
        return (service, factory);
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
}
