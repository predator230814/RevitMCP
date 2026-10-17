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
            [Placement(3, 0), Placement(4, 0, 1)],
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
        Assert.True(MepDevicePlacementGeometry.WithinBounds(MepDevicePlacementGeometry.MaxPlanFeet, 0, MepDevicePlacementGeometry.MaxVerticalFeet));
        Assert.False(MepDevicePlacementGeometry.WithinBounds(MepDevicePlacementGeometry.MaxPlanFeet + 0.01, 0, 0));
        Assert.False(MepDevicePlacementGeometry.WithinBounds(0, MepDevicePlacementGeometry.MaxPlanFeet + 0.01, 0));
        Assert.False(MepDevicePlacementGeometry.WithinBounds(0, 0, MepDevicePlacementGeometry.MaxVerticalFeet + 0.01));
        Assert.Equal(100.0 / 0.3048, MepDevicePlacementGeometry.MaxPlanFeet, 6);
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
            Placements = [new PreviewMepDevicePlacement { OffsetX = 1, OffsetY = 0, VerticalOffset = 0 }]
        }));
        Assert.Equal(CapabilityErrorCodes.InvalidMepDevicePlacement, exception.ErrorCode);
    }

    [Fact]
    public void Unknown_occupancy_cannot_create_an_intent_or_pass_revalidation()
    {
        var unknown = Facts(occupancyReadSucceeded: false, occupiedTargets: false);
        MepDevicePlacementPreviewComposer.TryEvaluate("template-a", unknown, [Placement(3, 0)], out _, out var items, out var intent);
        Assert.Null(intent);
        Assert.NotEqual(PreviewMepDevicePlacementItemStatus.Ok, items[0].Status);

        var approved = Facts().Snapshot!;
        Assert.False(MepDevicePlacementPreviewComposer.IsCurrent(approved, unknown));
    }

    [Fact]
    public void Stale_checks_cover_host_geometry_and_new_occupancy()
    {
        var approved = Facts().Snapshot!;
        Assert.True(MepDevicePlacementPreviewComposer.IsCurrent(approved, Facts(occupiedTargets: false)));
        Assert.False(MepDevicePlacementPreviewComposer.IsCurrent(approved, Facts(hostEnd: new MepPoint(12, 0, 0), occupiedTargets: false)));
        Assert.False(MepDevicePlacementPreviewComposer.IsCurrent(approved, Facts(extraOccupants: [new MepPoint(5, 0, 4)], occupiedTargets: false)));
        Assert.False(MepDevicePlacementPreviewComposer.IsCurrent(approved, Facts(categoryToken: "OST_LightingFixtures")));
        Assert.False(MepDevicePlacementPreviewComposer.IsCurrent(approved, Facts(rotation: 0.2, occupiedTargets: false)));
        Assert.False(MepDevicePlacementPreviewComposer.IsCurrent(approved, LevelFacts()));
    }

    [Fact]
    public void Level_based_uses_project_axes_and_same_level_occupancy_without_a_wall()
    {
        MepDevicePlacementPreviewComposer.TryEvaluate(
            "template-a",
            LevelFacts(),
            [Placement(3, 1, 0)],
            out var template,
            out var items,
            out var intent);
        Assert.Equal(PreviewMepDevicePlacementMode.LevelBased, template.PlacementMode);
        Assert.Equal(PreviewMepDeviceTemplateStatus.Ok, template.Status);
        Assert.Null(template.HostElementRef);
        Assert.Equal(PreviewMepDevicePlacementItemStatus.Ok, items[0].Status);
        Assert.Equal(MepPlacementStrategyKind.LevelBased, intent!.Strategy);
        Assert.Equal(5, intent.Items[0].Target.X);
        Assert.Equal(1, intent.Items[0].Target.Y);
        Assert.Equal(4, intent.Items[0].Target.Z);
        Assert.Equal(0.4, intent.Orientation.RotationRadians);

        MepDevicePlacementPreviewComposer.TryEvaluate("template-a", LevelFacts(), [Placement(0, 0)], out _, out items, out intent);
        Assert.Equal(PreviewMepDevicePlacementItemStatus.Occupied, items[0].Status);
        Assert.Null(intent);

        var unknown = LevelFacts(occupancyReadSucceeded: false);
        MepDevicePlacementPreviewComposer.TryEvaluate("template-a", unknown, [Placement(3, 0)], out _, out _, out intent);
        Assert.Null(intent);
    }

    [Fact]
    public void Wall_hosted_project_axis_target_must_stay_on_the_finite_wall()
    {
        MepDevicePlacementPreviewComposer.TryEvaluate("template-a", Facts(), [Placement(3, 0)], out var template, out var items, out var intent);
        Assert.Equal(PreviewMepDevicePlacementMode.WallHosted, template.PlacementMode);
        Assert.Equal("wall-a", template.HostElementRef);
        Assert.Equal(PreviewMepDevicePlacementItemStatus.Ok, items[0].Status);
        Assert.NotNull(intent);

        MepDevicePlacementPreviewComposer.TryEvaluate("template-a", Facts(), [Placement(0, 1)], out _, out items, out intent);
        Assert.Equal(PreviewMepDevicePlacementItemStatus.UnsupportedPosition, items[0].Status);
        Assert.Null(intent);
    }

    [Fact]
    public void Work_plane_modes_create_only_when_the_level_plane_is_proven()
    {
        Assert.Equal(PreviewMepDevicePlacementMode.WorkPlaneBasedLevel, MepWorkPlaneClassification.Classify(false, true));
        Assert.Equal(PreviewMepDevicePlacementMode.WorkPlaneBasedFace, MepWorkPlaneClassification.Classify(true, true));
        Assert.Equal(PreviewMepDevicePlacementMode.WorkPlaneBasedOther, MepWorkPlaneClassification.Classify(false, false));

        MepDevicePlacementPreviewComposer.TryEvaluate("template-a", WorkPlaneFacts(), [Placement(3, 0)], out var ready, out var items, out var intent);
        Assert.Equal(PreviewMepDevicePlacementMode.WorkPlaneBasedLevel, ready.PlacementMode);
        Assert.Equal(PreviewMepDeviceTemplateStatus.Ok, ready.Status);
        Assert.NotNull(intent);
        Assert.Equal(PreviewMepDevicePlacementItemStatus.Ok, items[0].Status);

        MepDevicePlacementPreviewComposer.TryEvaluate("template-a", Characterized(PreviewMepDevicePlacementMode.WorkPlaneBasedFace), [Placement(3, 0)], out var face, out items, out intent);
        Assert.Equal(PreviewMepDevicePlacementMode.WorkPlaneBasedFace, face.PlacementMode);
        Assert.Equal(PreviewMepDeviceTemplateStatus.UnsupportedPlacement, face.Status);
        Assert.Equal("Electrical Fixtures", face.CategoryName);
        Assert.Null(intent);
        Assert.Equal(PreviewMepDevicePlacementItemStatus.UnsupportedPosition, items[0].Status);

        MepDevicePlacementPreviewComposer.TryEvaluate("template-a", Characterized(PreviewMepDevicePlacementMode.WorkPlaneBasedOther), [Placement(3, 0)], out var other, out _, out intent);
        Assert.Equal(PreviewMepDevicePlacementMode.WorkPlaneBasedOther, other.PlacementMode);
        Assert.Equal(PreviewMepDeviceTemplateStatus.UnsupportedPlacement, other.Status);
        Assert.Null(intent);

        var approved = WorkPlaneFacts().Snapshot!;
        Assert.False(MepDevicePlacementPreviewComposer.IsCurrent(approved, WorkPlaneFacts(direction: new MepPlanDirection(0, 1))));
        Assert.False(MepDevicePlacementPreviewComposer.IsCurrent(approved, Characterized(PreviewMepDevicePlacementMode.WorkPlaneBasedFace)));
    }

    [Fact]
    public void Level_creation_is_selected_only_for_the_level_strategy_and_orientation_must_match()
    {
        Assert.True(MepCreationPlan.TrySelect(MepPlacementStrategyKind.LevelBased, out var level));
        Assert.Equal(MepCreationOverload.LevelPoint, level);
        Assert.True(MepCreationPlan.TrySelect(MepPlacementStrategyKind.WallHosted, out var wall));
        Assert.Equal(MepCreationOverload.WallHost, wall);
        Assert.True(MepCreationPlan.TrySelect(MepPlacementStrategyKind.WorkPlaneBasedLevel, out var plane));
        Assert.Equal(MepCreationOverload.LevelPlaneReference, plane);

        var orientation = new MepOrientation(0.4, false, false, true, true);
        Assert.False(MepDevicePlacementGeometry.OrientationMatches(MepPlacementStrategyKind.LevelBased, 0.4 + 0.01, false, false, orientation));
        Assert.True(MepDevicePlacementGeometry.OrientationMatches(MepPlacementStrategyKind.LevelBased, 0.4, false, false, orientation));
        Assert.True(MepDevicePlacementGeometry.OrientationMatches(MepPlacementStrategyKind.WallHosted, 1.2, false, false, orientation));
    }

    [Fact]
    public void Level_point_creation_z_subtracts_only_the_level_project_elevation()
    {
        const double x = 12;
        const double y = -4;

        var atZero = MepCreationPlan.CreationPoint(MepCreationOverload.LevelPoint, new MepPoint(x, y, 15), 0);
        Assert.Equal(new MepPoint(x, y, 15), atZero);

        var positive = MepCreationPlan.CreationPoint(MepCreationOverload.LevelPoint, new MepPoint(x, y, 40), 32);
        Assert.Equal(new MepPoint(x, y, 8), positive);

        var negative = MepCreationPlan.CreationPoint(MepCreationOverload.LevelPoint, new MepPoint(x, y, 5), -3);
        Assert.Equal(new MepPoint(x, y, 8), negative);

        const double projectElevation = 10;
        const double existingOffset = 4;
        const double verticalOffset = 2;
        var template = new MepPoint(x, y, projectElevation + existingOffset);
        Assert.True(MepDevicePlacementGeometry.TryProjectTarget(template, 1, -1, verticalOffset, out var target));
        var created = MepCreationPlan.CreationPoint(MepCreationOverload.LevelPoint, target, projectElevation);
        Assert.Equal(new MepPoint(x + 1, y - 1, existingOffset + verticalOffset), created);
        Assert.Equal(target, MepCreationPlan.CreationPoint(MepCreationOverload.WallHost, target, projectElevation));
        Assert.Equal(target, MepCreationPlan.CreationPoint(MepCreationOverload.LevelPlaneReference, target, projectElevation));
    }

    private static MepConvertedPlacement Placement(double offsetX, double offsetY = 0, double vertical = 0)
    {
        return new MepConvertedPlacement(offsetX, offsetY, vertical, offsetX, offsetY, vertical);
    }

    private static PreviewMepDevicePlacementsRequest Request(int count, double along = 1, double vertical = 0, bool duplicate = false)
    {
        var placements = new List<PreviewMepDevicePlacement>();
        for (var index = 0; index < count; index++)
        {
            placements.Add(new PreviewMepDevicePlacement
            {
                OffsetX = duplicate ? along : along + index,
                OffsetY = 0,
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
        bool occupiedTargets = true,
        bool occupancyReadSucceeded = true,
        double rotation = 0)
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

        var snapshot = resolved && family && location && level && placement
            ? WallIntent(categoryToken, template, end, rotation, [new MepDevicePlacementItem(1, 3, 0, 0, new MepPoint(5, 0, 4))])
            : null;
        return new MepTemplateFacts
        {
            Resolved = resolved,
            IsFamilyInstance = family,
            CategoryToken = categoryToken,
            HasSymbol = true,
            PlacementMode = placement ? PreviewMepDevicePlacementMode.WallHosted : null,
            HostInDocument = wall,
            HostIsWall = wall,
            HostIsStraightLine = straight,
            HasLocationPoint = location,
            HasLevel = level,
            CategoryName = "Electrical Fixtures",
            FamilyName = "Duplex Receptacle",
            TypeName = "Standard",
            LevelName = "L4",
            HostElementRef = "wall-a",
            HostName = "Basic Wall",
            Snapshot = snapshot,
            OccupancyReadSucceeded = occupancyReadSucceeded,
            OccupiedPoints = occupants
        };
    }

    private static MepTemplateFacts LevelFacts(bool occupancyReadSucceeded = true)
    {
        var template = new MepPoint(2, 0, 4);
        return new MepTemplateFacts
        {
            Resolved = true,
            IsFamilyInstance = true,
            CategoryToken = "OST_ElectricalFixtures",
            HasSymbol = true,
            PlacementMode = PreviewMepDevicePlacementMode.LevelBased,
            HasLocationPoint = true,
            HasLevel = true,
            CategoryName = "Electrical Fixtures",
            FamilyName = "Duplex Receptacle",
            TypeName = "Standard",
            LevelName = "Level 4",
            Snapshot = LevelIntent(template, 0.4, [new MepDevicePlacementItem(1, 3, 0, 0, new MepPoint(5, 0, 4))]),
            OccupancyReadSucceeded = occupancyReadSucceeded,
            OccupiedPoints = [template]
        };
    }

    private static MepTemplateFacts WorkPlaneFacts(MepPlanDirection? direction = null)
    {
        var template = new MepPoint(2, 0, 4);
        var reference = direction ?? new MepPlanDirection(1, 0);
        return new MepTemplateFacts
        {
            Resolved = true,
            IsFamilyInstance = true,
            CategoryToken = "OST_ElectricalFixtures",
            HasSymbol = true,
            PlacementMode = PreviewMepDevicePlacementMode.WorkPlaneBasedLevel,
            HasLocationPoint = true,
            HasLevel = true,
            CategoryName = "Electrical Fixtures",
            FamilyName = "Duplex Receptacle",
            TypeName = "Standard",
            LevelName = "Level 4",
            Snapshot = new MepDevicePlacementIntent(
                MepPlacementStrategyKind.WorkPlaneBasedLevel,
                "autodesk.unit.unit:millimeters-1.0.1",
                "template-a",
                "symbol-a",
                "OST_ElectricalFixtures",
                "Electrical Fixtures",
                "Duplex Receptacle",
                "Standard",
                "level-a",
                "Level 4",
                template,
                new MepOrientation(0, false, false, true, true),
                null,
                reference,
                [new MepDevicePlacementItem(1, 3, 0, 0, new MepPoint(5, 0, 4))]),
            OccupancyReadSucceeded = true,
            OccupiedPoints = [template]
        };
    }

    private static MepTemplateFacts Characterized(PreviewMepDevicePlacementMode mode)
    {
        return new MepTemplateFacts
        {
            Resolved = true,
            IsFamilyInstance = true,
            CategoryToken = "OST_ElectricalFixtures",
            HasSymbol = true,
            PlacementMode = mode,
            HasLocationPoint = true,
            HasLevel = true,
            CategoryName = "Electrical Fixtures",
            FamilyName = "Duplex Receptacle",
            TypeName = "Standard",
            LevelName = "Level 4",
            OccupancyReadSucceeded = true
        };
    }

    private static MepDevicePlacementIntent WallIntent(
        string categoryToken,
        MepPoint template,
        MepPoint hostEnd,
        double rotation,
        IReadOnlyList<MepDevicePlacementItem> items)
    {
        return new MepDevicePlacementIntent(
            MepPlacementStrategyKind.WallHosted,
            "autodesk.unit.unit:millimeters-1.0.1",
            "template-a",
            "symbol-a",
            categoryToken,
            "Electrical Fixtures",
            "Duplex Receptacle",
            "Standard",
            "level-a",
            "L4",
            template,
            new MepOrientation(rotation, false, false, true, true),
            new MepWallBinding("wall-a", "Basic Wall", new MepPoint(0, 0, 0), hostEnd),
            null,
            items);
    }

    private static MepDevicePlacementIntent LevelIntent(MepPoint template, double rotation, IReadOnlyList<MepDevicePlacementItem> items)
    {
        return new MepDevicePlacementIntent(
            MepPlacementStrategyKind.LevelBased,
            "autodesk.unit.unit:millimeters-1.0.1",
            "template-a",
            "symbol-a",
            "OST_ElectricalFixtures",
            "Electrical Fixtures",
            "Duplex Receptacle",
            "Standard",
            "level-a",
            "Level 4",
            template,
            new MepOrientation(rotation, false, false, true, true),
            null,
            null,
            items);
    }
}
