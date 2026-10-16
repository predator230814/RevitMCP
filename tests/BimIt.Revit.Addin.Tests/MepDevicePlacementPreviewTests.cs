using BimIt.Revit.Addin.Inspection;
using BimIt.Revit.Addin.Intents;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;
using Xunit;

namespace BimIt.Revit.Addin.Tests;

public sealed class MepDevicePlacementPreviewTests
{
    [Theory]
    [MemberData(nameof(SupportedCategories))]
    public void Supported_categories_classify_as_ok(string categoryToken)
    {
        Assert.Equal(PreviewMepDeviceTemplateStatus.Ok, MepDevicePlacementPreviewComposer.Classify(Facts(categoryToken: categoryToken)));
    }

    public static IEnumerable<object[]> SupportedCategories()
    {
        return MepDeviceCategorySupport.SupportedTokens.Select(token => new object[] { token });
    }

    [Theory]
    [InlineData("OST_GenericModel")]
    [InlineData("OST_Doors")]
    [InlineData("OST_Windows")]
    [InlineData("OST_Furniture")]
    public void Unsupported_categories_fail_closed(string categoryToken)
    {
        Assert.Equal(PreviewMepDeviceTemplateStatus.UnsupportedCategory, MepDevicePlacementPreviewComposer.Classify(Facts(categoryToken: categoryToken)));
    }

    [Fact]
    public void Template_eligibility_failures_are_closed_statuses()
    {
        Assert.Equal(PreviewMepDeviceTemplateStatus.NotFound, MepDevicePlacementPreviewComposer.Classify(Facts(resolved: false)));
        Assert.Equal(PreviewMepDeviceTemplateStatus.NotFamilyInstance, MepDevicePlacementPreviewComposer.Classify(Facts(family: false)));
        Assert.Equal(PreviewMepDeviceTemplateStatus.UnsupportedPlacement, MepDevicePlacementPreviewComposer.Classify(Facts(placement: false)));
        Assert.Equal(PreviewMepDeviceTemplateStatus.UnsupportedHost, MepDevicePlacementPreviewComposer.Classify(Facts(wall: false)));
        Assert.Equal(PreviewMepDeviceTemplateStatus.UnsupportedHostGeometry, MepDevicePlacementPreviewComposer.Classify(Facts(straight: false)));
        Assert.Equal(PreviewMepDeviceTemplateStatus.UnsupportedLocation, MepDevicePlacementPreviewComposer.Classify(Facts(location: false)));
        Assert.Equal(PreviewMepDeviceTemplateStatus.UnsupportedLevel, MepDevicePlacementPreviewComposer.Classify(Facts(level: false)));
    }

    [Fact]
    public void Zero_offset_is_occupied_and_points_outside_the_wall_are_rejected()
    {
        MepDevicePlacementPreviewComposer.TryEvaluate("template-a", Facts(), [Placement(0, 0)], out var template, out var items, out var intent);
        Assert.Equal(PreviewMepDeviceTemplateStatus.Ok, template.Status);
        Assert.Equal(PreviewMepDevicePlacementItemStatus.Occupied, items[0].Status);
        Assert.Null(intent);

        MepDevicePlacementPreviewComposer.TryEvaluate("template-a", Facts(), [Placement(20, 0)], out _, out items, out intent);
        Assert.Equal(PreviewMepDevicePlacementItemStatus.OutOfHostBounds, items[0].Status);
        Assert.Null(intent);

        MepDevicePlacementPreviewComposer.TryEvaluate("template-a", Facts(), [Placement(-5, 0)], out _, out items, out intent);
        Assert.Equal(PreviewMepDevicePlacementItemStatus.OutOfHostBounds, items[0].Status);
        Assert.Null(intent);
    }

    [Fact]
    public void Same_symbol_occupancy_and_a_single_invalid_item_suppress_the_intent()
    {
        var occupied = new MepPoint(5, 0, 4);
        MepDevicePlacementPreviewComposer.TryEvaluate(
            "template-a",
            Facts(extraOccupants: [occupied]),
            [Placement(3, 0), Placement(4, 0)],
            out _,
            out var items,
            out var intent);
        Assert.Equal(PreviewMepDevicePlacementItemStatus.Occupied, items[0].Status);
        Assert.Equal(PreviewMepDevicePlacementItemStatus.Ok, items[1].Status);
        Assert.Null(intent);
    }

    [Fact]
    public void All_eligible_placements_produce_an_intent_without_mutating_a_model()
    {
        MepDevicePlacementPreviewComposer.TryEvaluate(
            "template-a",
            Facts(),
            [Placement(3, 0), Placement(4, 1)],
            out var template,
            out var items,
            out var intent);
        Assert.Equal(PreviewMepDeviceTemplateStatus.Ok, template.Status);
        Assert.Equal("Electrical Fixtures", template.CategoryName);
        Assert.All(items, item => Assert.Equal(PreviewMepDevicePlacementItemStatus.Ok, item.Status));
        Assert.NotNull(intent);
        Assert.Equal(2, intent!.Items.Count);
        Assert.Equal(1, intent.Items[0].RequestPosition);
        Assert.Equal(2, intent.Items[1].RequestPosition);
    }

    [Fact]
    public void Length_bounds_reject_values_outside_100_and_10_metres()
    {
        Assert.True(MepDevicePlacementGeometry.WithinBounds(MepDevicePlacementGeometry.MaxAlongHostFeet, MepDevicePlacementGeometry.MaxVerticalFeet));
        Assert.False(MepDevicePlacementGeometry.WithinBounds(MepDevicePlacementGeometry.MaxAlongHostFeet + 0.01, 0));
        Assert.False(MepDevicePlacementGeometry.WithinBounds(0, MepDevicePlacementGeometry.MaxVerticalFeet + 0.01));
        Assert.Equal(100.0 / 0.3048, MepDevicePlacementGeometry.MaxAlongHostFeet, 6);
        Assert.Equal(10.0 / 0.3048, MepDevicePlacementGeometry.MaxVerticalFeet, 6);
        Assert.Equal(1.0 / 304.8, MepDevicePlacementGeometry.OccupancyToleranceFeet, 8);
    }

    [Fact]
    public void Request_shape_rejects_range_duplicates_and_non_finite_numbers()
    {
        Assert.Throws<BridgeException>(() => PreviewMepDevicePlacementsRequestValidator.Validate(Request(0)));
        Assert.Throws<BridgeException>(() => PreviewMepDevicePlacementsRequestValidator.Validate(Request(21)));
        Assert.Throws<BridgeException>(() => PreviewMepDevicePlacementsRequestValidator.Validate(Request(1, double.NaN, 0)));
        Assert.Throws<BridgeException>(() => PreviewMepDevicePlacementsRequestValidator.Validate(Request(2, 1, 0, duplicate: true)));
        var exception = Assert.Throws<BridgeException>(() => PreviewMepDevicePlacementsRequestValidator.Validate(new PreviewMepDevicePlacementsRequest
        {
            DocumentId = "doc a",
            TemplateElementRef = "template-a",
            LengthUnitId = "autodesk.unit.unit:millimeters-1.0.1",
            Placements = [new PreviewMepDevicePlacement { OffsetAlongHost = 1, VerticalOffset = 0 }]
        }));
        Assert.Equal(CapabilityErrorCodes.InvalidMepDevicePlacement, exception.ErrorCode);
    }

    [Fact]
    public void Stale_checks_cover_host_geometry_and_new_occupancy()
    {
        var approved = Facts().Snapshot!;
        Assert.True(MepDevicePlacementPreviewComposer.IsCurrent(approved, Facts(occupiedTargets: false)));
        Assert.False(MepDevicePlacementPreviewComposer.IsCurrent(approved, Facts(hostEnd: new MepPoint(12, 0, 0), occupiedTargets: false)));
        Assert.False(MepDevicePlacementPreviewComposer.IsCurrent(approved, Facts(extraOccupants: [new MepPoint(5, 0, 4)], occupiedTargets: false)));
        Assert.False(MepDevicePlacementPreviewComposer.IsCurrent(approved, Facts(categoryToken: "OST_LightingFixtures")));
    }

    private static MepConvertedPlacement Placement(double alongFeet, double verticalFeet)
    {
        return new MepConvertedPlacement(alongFeet, verticalFeet, alongFeet, verticalFeet);
    }

    private static PreviewMepDevicePlacementsRequest Request(int count, double along = 1, double vertical = 0, bool duplicate = false)
    {
        var placements = new List<PreviewMepDevicePlacement>();
        for (var index = 0; index < count; index++)
        {
            placements.Add(new PreviewMepDevicePlacement
            {
                OffsetAlongHost = duplicate ? along : along + index,
                VerticalOffset = vertical
            });
        }

        return new PreviewMepDevicePlacementsRequest
        {
            DocumentId = "doc-a",
            TemplateElementRef = "template-a",
            LengthUnitId = "autodesk.unit.unit:millimeters-1.0.1",
            Placements = placements
        };
    }

    private static MepTemplateFacts Facts(
        bool resolved = true,
        bool family = true,
        string categoryToken = "OST_ElectricalFixtures",
        bool placement = true,
        bool wall = true,
        bool straight = true,
        bool location = true,
        bool level = true,
        MepPoint? hostEnd = null,
        IReadOnlyList<MepPoint>? extraOccupants = null,
        bool occupiedTargets = true)
    {
        var template = new MepPoint(2, 0, 4);
        var end = hostEnd ?? new MepPoint(10, 0, 0);
        var occupants = new List<MepPoint>();
        if (occupiedTargets)
        {
            occupants.Add(template);
        }

        if (extraOccupants is not null)
        {
            occupants.AddRange(extraOccupants);
        }

        var snapshot = resolved && family && location && level
            ? new MepDevicePlacementIntent(
                "autodesk.unit.unit:millimeters-1.0.1",
                "template-a",
                "symbol-a",
                categoryToken,
                "Electrical Fixtures",
                "Duplex Receptacle",
                "Standard",
                "wall-a",
                "Basic Wall",
                "level-a",
                "L4",
                template,
                new MepPoint(0, 0, 0),
                end,
                false,
                false,
                true,
                true,
                [new MepDevicePlacementItem(1, 3, 0, new MepPoint(5, 0, 4))])
            : null;
        return new MepTemplateFacts
        {
            Resolved = resolved,
            IsFamilyInstance = family,
            CategoryToken = categoryToken,
            SupportedPlacementMode = placement,
            HasSymbol = true,
            HostInDocument = wall,
            HostIsWall = wall,
            HostIsStraightLine = straight,
            HasLocationPoint = location,
            HasLevel = level,
            Snapshot = snapshot,
            OccupiedPoints = occupants
        };
    }
}
