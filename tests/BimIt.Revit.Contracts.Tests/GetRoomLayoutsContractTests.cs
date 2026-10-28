using System.Text.Json;
using Xunit;

namespace BimIt.Revit.Contracts.Tests;

public sealed class GetRoomLayoutsContractTests
{
    [Fact]
    public void Request_has_document_guard_and_no_instance_id()
    {
        Assert.Equal(
            [nameof(GetRoomLayoutsRequest.DocumentId), nameof(GetRoomLayoutsRequest.ElementRefs)],
            typeof(GetRoomLayoutsRequest).GetProperties().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());
        var json = JsonSerializer.Serialize(new GetRoomLayoutsRequest { DocumentId = "doc", ElementRefs = ["room-1"] }, ContractJson.Options);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(["document_id", "element_refs"], document.RootElement.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());
        Assert.False(document.RootElement.TryGetProperty("instance_id", out _));
    }

    [Fact]
    public void Result_uses_metres_and_closed_room_fields()
    {
        var result = new GetRoomLayoutsResult
        {
            Context = new GetRoomLayoutsContext { InstanceId = "inst", DocumentId = "doc" },
            LengthUnit = GetRoomLayoutsResult.Metres,
            ChordToleranceMetres = 0.05,
            OpeningScanTruncated = false,
            Items =
            [
                new GetRoomLayoutItem
                {
                    ElementRef = "room-1",
                    Status = RoomLayoutItemStatus.Ok,
                    Name = "Bedroom",
                    Number = "201",
                    LevelName = "Level 2",
                    CentroidXMetres = 1.25,
                    CentroidYMetres = 3.5,
                    LoopsTruncated = false,
                    FeaturesTruncated = false,
                    Loops =
                    [
                        new GetRoomLayoutLoop
                        {
                            VerticesTruncated = false,
                            Vertices = [new GetRoomLayoutVertex { XMetres = 0, YMetres = 0 }, new GetRoomLayoutVertex { XMetres = 4, YMetres = 0 }, new GetRoomLayoutVertex { XMetres = 4, YMetres = 3 }],
                        },
                    ],
                    Features =
                    [
                        new GetRoomLayoutFeature
                        {
                            Kind = RoomFeatureKind.Door,
                            XMetres = 1,
                            YMetres = 0,
                            FacingX = 0,
                            FacingY = 1,
                            WidthMetres = 0.9,
                        },
                    ],
                },
            ],
        };

        var json = JsonSerializer.Serialize(result, ContractJson.Options);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("m", document.RootElement.GetProperty("length_unit").GetString());
        Assert.Equal(0.05, document.RootElement.GetProperty("chord_tolerance_m").GetDouble());
        Assert.Equal(1.25, document.RootElement.GetProperty("items")[0].GetProperty("centroid_x_m").GetDouble());
        Assert.Equal("door", document.RootElement.GetProperty("items")[0].GetProperty("features")[0].GetProperty("kind").GetString());
        Assert.Equal("ok", document.RootElement.GetProperty("items")[0].GetProperty("status").GetString());
        Assert.DoesNotContain("feet", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("internal", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            ["chord_tolerance_m", "context", "items", "length_unit", "opening_scan_truncated"],
            document.RootElement.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());
    }

    [Theory]
    [InlineData(RoomLayoutItemStatus.NotFound, "not_found")]
    [InlineData(RoomLayoutItemStatus.NotARoom, "not_a_room")]
    [InlineData(RoomLayoutItemStatus.BoundaryUnavailable, "boundary_unavailable")]
    public void Item_statuses_serialize_as_closed_snake_case(RoomLayoutItemStatus status, string expected)
    {
        var json = JsonSerializer.Serialize(status, ContractJson.Options);
        Assert.Equal($"\"{expected}\"", json);
    }

    [Fact]
    public void Protocol_17_adds_room_layouts_and_preserves_earlier_capabilities()
    {
        Assert.Equal(17, BridgeProtocol.GetRoomLayoutsVersion);
        Assert.Equal(20, BridgeProtocol.CurrentVersion);
        Assert.True(BridgeProtocol.SupportsGetRoomLayouts(17));
        Assert.True(BridgeProtocol.SupportsGetRoomLayouts(18));
        Assert.True(BridgeProtocol.SupportsGetRoomLayouts(19));
        Assert.False(BridgeProtocol.SupportsGetRoomLayouts(16));
        Assert.True(BridgeProtocol.SupportsGetRoomLayouts(20)); Assert.False(BridgeProtocol.SupportsGetRoomLayouts(21));
        Assert.True(BridgeProtocol.SupportsApplyMepDevicePlacements(17));
        Assert.True(BridgeProtocol.SupportsGetSelection(17));
        Assert.True(BridgeProtocol.SupportsGetContext(18));
        Assert.True(BridgeProtocol.SupportsGetContext(20)); Assert.False(BridgeProtocol.SupportsGetContext(21));
        Assert.Equal(20, BridgeProtocol.SupportedVersions[0]);
    }
}
