using BimIt.Revit.Addin.Intents;
using BimIt.Revit.Contracts;
using Xunit;

namespace BimIt.Revit.Addin.Tests;

public sealed class PatternPlacementComposerTests
{
    [Fact]
    public void Interior_level_and_work_plane_compose_without_a_host()
    {
        var level = Compose("interior", "one_level_based", (5, 4), FaceResolved: false);
        Assert.Equal(PatternPlacementStrategyKind.InteriorLevel, level.Strategy);
        Assert.Null(level.Host);

        var plane = Compose("interior", "work_plane_based", (5, 4), FaceResolved: false);
        Assert.Equal(PatternPlacementStrategyKind.InteriorLevelWorkPlane, plane.Strategy);
        Assert.Null(plane.Host);
    }

    [Fact]
    public void Boundary_straight_wall_and_face_compose_on_the_room_side()
    {
        var hosted = Compose("boundary", "one_level_based_hosted", (5, 0.1), FaceResolved: false);
        Assert.Equal(PatternPlacementStrategyKind.BoundaryWallHosted, hosted.Strategy);
        Assert.Equal("wall-south", hosted.Host!.WallRef);
        Assert.True(hosted.Host.InsideNormal.Y > 0);

        var face = Compose("boundary", "work_plane_based", (5, 0.1), FaceResolved: true);
        Assert.Equal(PatternPlacementStrategyKind.BoundaryWallFace, face.Strategy);
        Assert.Equal("wall-south", face.Host!.WallRef);
    }

    [Fact]
    public void Unsupported_ambiguous_curved_and_missing_walls_fail_closed()
    {
        Assert.Equal(PreviewRoomPatternItemStatus.UnsupportedPlacement, Status("interior", "one_level_based_hosted", (5, 4), FaceResolved: true));
        Assert.Equal(PreviewRoomPatternItemStatus.UnsupportedFace, Status("boundary", "work_plane_based", (5, 0.1), FaceResolved: false));
        Assert.Equal(PreviewRoomPatternItemStatus.AmbiguousWall, Status("boundary", "one_level_based_hosted", (0.05, 0.05), FaceResolved: true));
        Assert.Equal(PreviewRoomPatternItemStatus.CurvedWall, Status("boundary", "one_level_based_hosted", (5, 0.1), FaceResolved: false, straight: false));
        Assert.Equal(PreviewRoomPatternItemStatus.NoWall, Status("boundary", "one_level_based_hosted", (5, 4), FaceResolved: true, includeWall: false));
    }

    [Fact]
    public void Occupancy_document_and_category_failures_create_no_intent()
    {
        Assert.Equal(PreviewRoomPatternItemStatus.Occupied, Status("interior", "one_level_based", (5, 4), FaceResolved: false, occupied: true));
        Assert.Equal(PreviewRoomPatternItemStatus.DocumentMismatch, Status("interior", "one_level_based", (5, 4), FaceResolved: false, documentMatches: false));
        Assert.Equal(PreviewRoomPatternItemStatus.CategoryMismatch, Status("interior", "one_level_based", (5, 4), FaceResolved: false, category: "OST_LightingFixtures"));
        Assert.Equal(PreviewRoomPatternItemStatus.RoomNotFound, Status("interior", "one_level_based", (5, 4), FaceResolved: false, roomRef: "missing"));
    }

    [Fact]
    public void Preview_service_has_no_transaction_and_apply_uses_one()
    {
        var preview = File.ReadAllText(Path.Combine(RepoRoot(), "src", "BimIt.Revit.Addin", "Capabilities", "RevitPreviewRoomPatternPlacementsService.cs"));
        var mutation = File.ReadAllText(Path.Combine(RepoRoot(), "src", "BimIt.Revit.Addin", "Capabilities", "RevitPatternMepPlacementMutation.cs"));
        Assert.DoesNotContain("new Transaction", preview, StringComparison.Ordinal);
        Assert.Contains("new Transaction", mutation, StringComparison.Ordinal);
        Assert.Equal(1, Count(mutation, "new Transaction"));
        var verify = mutation.IndexOf("!Verify(", StringComparison.Ordinal);
        var commit = mutation.IndexOf("transaction.Commit()", StringComparison.Ordinal);
        var face = mutation.IndexOf("FaceIntoRoom(", StringComparison.Ordinal);
        Assert.True(face >= 0);
        Assert.True(face < verify);
        Assert.True(verify < commit);
        Assert.Equal(2, Count(mutation, "document.Regenerate()"));
    }

    [Fact]
    public void Wall_placement_keeps_height_tight_and_plan_within_the_wall_tolerance()
    {
        var target = new MepPoint(10, 20, 8);
        var onWall = new MepPoint(10.4, 20, 8);
        var tooFar = new MepPoint(10.6, 20, 8);
        var wrongHeight = new MepPoint(10, 20, 8 + (2.0 / 304.8));

        Assert.True(MepDevicePlacementGeometry.SameWallPlacement(onWall, target, PatternPlacementComposer.WallToleranceFeet));
        Assert.False(MepDevicePlacementGeometry.SamePoint(onWall, target));
        Assert.False(MepDevicePlacementGeometry.SameWallPlacement(tooFar, target, PatternPlacementComposer.WallToleranceFeet));
        Assert.False(MepDevicePlacementGeometry.SameWallPlacement(wrongHeight, target, PatternPlacementComposer.WallToleranceFeet));
    }

    [Fact]
    public void Hosted_wall_ignores_the_gap_between_the_room_point_and_the_wall()
    {
        var wallStart = new MepPoint(0, 0, 0);
        var wallEnd = new MepPoint(20, 0, 0);
        var target = new MepPoint(10, 1, 8);
        var onWall = new MepPoint(10, -0.5, 8);
        var slid = new MepPoint(10.6, -0.5, 8);
        var wrongHeight = new MepPoint(10, -0.5, 8 + (2.0 / 304.8));

        Assert.True(MepDevicePlacementGeometry.SameHostedWallPlacement(onWall, target, wallStart, wallEnd, PatternPlacementComposer.WallToleranceFeet));
        Assert.False(MepDevicePlacementGeometry.SameWallPlacement(onWall, target, PatternPlacementComposer.WallToleranceFeet));
        Assert.False(MepDevicePlacementGeometry.SameHostedWallPlacement(slid, target, wallStart, wallEnd, PatternPlacementComposer.WallToleranceFeet));
        Assert.False(MepDevicePlacementGeometry.SameHostedWallPlacement(wrongHeight, target, wallStart, wallEnd, PatternPlacementComposer.WallToleranceFeet));
    }

    private static PatternMepPlacementItem Compose(
        string placement,
        string classification,
        (double X, double Y) point,
        bool FaceResolved,
        bool straight = true,
        bool includeWall = true,
        bool occupied = false,
        bool documentMatches = true,
        string category = "OST_ElectricalFixtures",
        string roomRef = "room-1")
    {
        Assert.True(PatternPlacementComposer.TryCompose(
            [Draft(placement, point, roomRef)],
            Facts(classification, FaceResolved, straight, includeWall, occupied, documentMatches, category),
            out var intent,
            out _));
        return intent!.Items[0];
    }

    private static PreviewRoomPatternItemStatus Status(
        string placement,
        string classification,
        (double X, double Y) point,
        bool FaceResolved,
        bool straight = true,
        bool includeWall = true,
        bool occupied = false,
        bool documentMatches = true,
        string category = "OST_ElectricalFixtures",
        string roomRef = "room-1")
    {
        Assert.False(PatternPlacementComposer.TryCompose(
            [Draft(placement, point, roomRef)],
            Facts(classification, FaceResolved, straight, includeWall, occupied, documentMatches, category),
            out _,
            out var statuses));
        return statuses[0];
    }

    private static PatternPlacementDraft Draft(string placement, (double X, double Y) point, string roomRef)
    {
        return new PatternPlacementDraft
        {
            RoomRef = roomRef,
            SymbolRef = "symbol-1",
            Role = "receptacle",
            PlacementKind = placement,
            XFeet = point.X,
            YFeet = point.Y,
            MountingHeightFeet = 1,
            QuarterTurns = 1,
        };
    }

    private static PatternProjectFacts Facts(
        string classification,
        bool faceResolved,
        bool straight,
        bool includeWall,
        bool occupied,
        bool documentMatches,
        string category)
    {
        var loop = new PatternPlanPoint[] { new(0, 0), new(10, 0), new(10, 8), new(0, 8) };
        var walls = includeWall
            ? new[]
            {
                Wall("wall-south", 0, 0, 10, 0, straight, faceResolved),
                Wall("wall-east", 10, 0, 10, 8, true, faceResolved),
                Wall("wall-north", 10, 8, 0, 8, true, faceResolved),
                Wall("wall-west", 0, 8, 0, 0, true, faceResolved),
            }
            : [];
        return new PatternProjectFacts
        {
            DocumentMatches = documentMatches,
            OccupancyReadSucceeded = true,
            Rooms =
            [
                new PatternRoomFacts
                {
                    RoomRef = "room-1",
                    Label = "Bedroom 201",
                    LevelRef = "level-1",
                    LevelName = "Level 1",
                    LevelElevationFeet = 0,
                    BoundarySupported = true,
                    Loop = loop,
                    Walls = walls,
                },
            ],
            Types =
            [
                new PatternTypeFacts
                {
                    SymbolRef = "symbol-1",
                    CategoryToken = category,
                    CategoryName = "Electrical Fixtures",
                    FamilyName = "Duplex",
                    TypeName = "Standard",
                    PlacementClassification = classification,
                    Active = true,
                },
            ],
            Occupants = occupied ? [new PatternOccupant("symbol-1", new MepPoint(5, 4, 1))] : [],
        };
    }

    private static PatternWallFacts Wall(string id, double x1, double y1, double x2, double y2, bool straight, bool faceResolved)
    {
        return new PatternWallFacts
        {
            WallRef = id,
            WallName = id,
            Start = new PatternPlanPoint(x1, y1),
            End = new PatternPlanPoint(x2, y2),
            Straight = straight,
            FaceResolved = faceResolved,
        };
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BimIt.Revit.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate BimIt.Revit.sln.");
    }
}
