using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.Intents;

internal enum PatternPlacementStrategyKind
{
    InteriorLevel = 1,
    InteriorLevelWorkPlane = 2,
    BoundaryWallHosted = 3,
    BoundaryWallFace = 4,
    BoundaryLinkedWallFace = 5,
    BoundaryLevel = 6,
}

internal enum PatternRoomSourceKind
{
    Host = 1,
    Link = 2,
}

internal enum PatternHostLevelStatus
{
    Resolved = 1,
    NotFound = 2,
    Ambiguous = 3,
}

internal enum PatternShellSide
{
    Interior = 1,
    Exterior = 2,
}

internal readonly record struct PatternPlanPoint(double X, double Y);

internal readonly record struct PatternLinkTransform(
    double OriginX,
    double OriginY,
    double OriginZ,
    double BasisXx,
    double BasisXy,
    double BasisXz,
    double BasisYx,
    double BasisYy,
    double BasisYz,
    double BasisZx,
    double BasisZy,
    double BasisZz)
{
    public const double RigidTolerance = 1e-6;

    public static PatternLinkTransform Identity { get; } = new(0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1);

    public IReadOnlyList<double> Values =>
        [OriginX, OriginY, OriginZ, BasisXx, BasisXy, BasisXz, BasisYx, BasisYy, BasisYz, BasisZx, BasisZy, BasisZz];

    public bool IsPlanRigid
    {
        get
        {
            if (Values.Any(value => !double.IsFinite(value)))
            {
                return false;
            }

            var xLength = Math.Sqrt((BasisXx * BasisXx) + (BasisXy * BasisXy));
            var yLength = Math.Sqrt((BasisYx * BasisYx) + (BasisYy * BasisYy));
            var dot = (BasisXx * BasisYx) + (BasisXy * BasisYy);
            var cross = (BasisXx * BasisYy) - (BasisXy * BasisYx);
            return Math.Abs(BasisXz) <= RigidTolerance
                && Math.Abs(BasisYz) <= RigidTolerance
                && Math.Abs(BasisZx) <= RigidTolerance
                && Math.Abs(BasisZy) <= RigidTolerance
                && Math.Abs(BasisZz - 1) <= RigidTolerance
                && Math.Abs(xLength - 1) <= RigidTolerance
                && Math.Abs(yLength - 1) <= RigidTolerance
                && Math.Abs(dot) <= RigidTolerance
                && Math.Abs(cross - 1) <= RigidTolerance;
        }
    }

    public MepPoint Apply(double x, double y, double z)
    {
        return new MepPoint(
            OriginX + (x * BasisXx) + (y * BasisYx) + (z * BasisZx),
            OriginY + (x * BasisXy) + (y * BasisYy) + (z * BasisZy),
            OriginZ + (x * BasisXz) + (y * BasisYz) + (z * BasisZz));
    }

    public MepPoint ApplyVector(double x, double y, double z)
    {
        return new MepPoint(
            (x * BasisXx) + (y * BasisYx) + (z * BasisZx),
            (x * BasisXy) + (y * BasisYy) + (z * BasisZy),
            (x * BasisXz) + (y * BasisYz) + (z * BasisZz));
    }

    public MepPoint Inverse(MepPoint host)
    {
        var dx = host.X - OriginX;
        var dy = host.Y - OriginY;
        var dz = host.Z - OriginZ;
        return new MepPoint(
            (dx * BasisXx) + (dy * BasisXy) + (dz * BasisXz),
            (dx * BasisYx) + (dy * BasisYy) + (dz * BasisYz),
            (dx * BasisZx) + (dy * BasisZy) + (dz * BasisZz));
    }
}

internal sealed record PatternLinkedFaceFacts(PatternShellSide Side, double NormalX, double NormalY, double PlaneOffset)
{
    public double Distance(double x, double y) => (x * NormalX) + (y * NormalY) - PlaneOffset;
}

internal sealed class PatternWallFacts
{
    public required string WallRef { get; init; }

    public required string WallName { get; init; }

    public required PatternPlanPoint Start { get; init; }

    public required PatternPlanPoint End { get; init; }

    public required bool Straight { get; init; }

    public required bool FaceResolved { get; init; }

    public PatternLinkedFaceFacts? LinkedFace { get; init; }
}

internal sealed class PatternRoomFacts
{
    public required string RoomRef { get; init; }

    public required string Label { get; init; }

    public required string LevelRef { get; init; }

    public required string LevelName { get; init; }

    public required double LevelElevationFeet { get; init; }

    public required bool BoundarySupported { get; init; }

    public required IReadOnlyList<PatternPlanPoint> Loop { get; init; }

    public required IReadOnlyList<PatternWallFacts> Walls { get; init; }

    public IReadOnlyList<IReadOnlyList<PatternPlanPoint>> Holes { get; init; } = [];

    public PatternRoomSourceKind SourceKind { get; init; } = PatternRoomSourceKind.Host;

    public PatternHostLevelStatus HostLevelStatus { get; init; } = PatternHostLevelStatus.Resolved;

    public PatternLinkedRoomSource? Link { get; init; }
}

internal sealed record PatternLinkedRoomSource(
    string LinkInstanceRef,
    string LinkedRoomRef,
    string LinkLabel,
    PatternLinkTransform Transform);

internal sealed class PatternTypeFacts
{
    public required string SymbolRef { get; init; }

    public required string CategoryToken { get; init; }

    public required string CategoryName { get; init; }

    public required string FamilyName { get; init; }

    public required string TypeName { get; init; }

    public required string PlacementClassification { get; init; }

    public required bool Active { get; init; }
}

internal readonly record struct PatternOccupant(string SymbolRef, MepPoint Point);

internal sealed class PatternProjectFacts
{
    public bool DocumentMatches { get; init; } = true;

    public bool OccupancyReadSucceeded { get; init; } = true;

    public IReadOnlyList<PatternRoomFacts> Rooms { get; init; } = [];

    public IReadOnlyDictionary<string, PreviewRoomPatternItemStatus> RoomFailures { get; init; } =
        new Dictionary<string, PreviewRoomPatternItemStatus>(StringComparer.Ordinal);

    public IReadOnlyList<PatternTypeFacts> Types { get; init; } = [];

    public IReadOnlyList<PatternOccupant> Occupants { get; init; } = [];
}

internal sealed class PatternPlacementDraft
{
    public required string RoomRef { get; init; }

    public required string SymbolRef { get; init; }

    public required string Role { get; init; }

    public required string PlacementKind { get; init; }

    public required double XFeet { get; init; }

    public required double YFeet { get; init; }

    public required double MountingHeightFeet { get; init; }

    public required int QuarterTurns { get; init; }
}

internal sealed class PatternMepRoomBinding
{
    public PatternMepRoomBinding(
        string roomRef,
        string label,
        string levelRef,
        string levelName,
        double levelElevationFeet,
        IReadOnlyList<PatternPlanPoint> loop,
        IReadOnlyList<IReadOnlyList<PatternPlanPoint>>? holes = null,
        PatternRoomSourceKind sourceKind = PatternRoomSourceKind.Host,
        PatternHostLevelStatus hostLevelStatus = PatternHostLevelStatus.Resolved,
        PatternLinkedRoomSource? link = null)
    {
        RoomRef = roomRef;
        Label = label;
        LevelRef = levelRef;
        LevelName = levelName;
        LevelElevationFeet = levelElevationFeet;
        Loop = loop;
        Holes = holes ?? [];
        SourceKind = sourceKind;
        HostLevelStatus = hostLevelStatus;
        Link = link;
    }

    public string RoomRef { get; }

    public string Label { get; }

    public string LevelRef { get; }

    public string LevelName { get; }

    public double LevelElevationFeet { get; }

    public IReadOnlyList<PatternPlanPoint> Loop { get; }

    public IReadOnlyList<IReadOnlyList<PatternPlanPoint>> Holes { get; }

    public PatternRoomSourceKind SourceKind { get; }

    public PatternHostLevelStatus HostLevelStatus { get; }

    public PatternLinkedRoomSource? Link { get; }
}

internal sealed class PatternMepHostBinding
{
    public PatternMepHostBinding(
        string wallRef,
        string wallName,
        MepPoint start,
        MepPoint end,
        MepPlanDirection insideNormal,
        string? linkInstanceRef = null,
        PatternLinkedFaceFacts? linkedFace = null)
    {
        WallRef = wallRef;
        WallName = wallName;
        Start = start;
        End = end;
        InsideNormal = insideNormal;
        LinkInstanceRef = linkInstanceRef;
        LinkedFace = linkedFace;
    }

    public string WallRef { get; }

    public string WallName { get; }

    public MepPoint Start { get; }

    public MepPoint End { get; }

    public MepPlanDirection InsideNormal { get; }

    public string? LinkInstanceRef { get; }

    public PatternLinkedFaceFacts? LinkedFace { get; }
}

internal sealed class PatternMepPlacementItem
{
    public PatternMepPlacementItem(
        int position,
        string roomRef,
        string symbolRef,
        string categoryToken,
        string categoryName,
        string familyName,
        string typeName,
        string role,
        string placementKind,
        PatternPlacementStrategyKind strategy,
        string levelRef,
        MepPoint target,
        double mountingHeightFeet,
        int quarterTurns,
        MepPlanDirection referenceDirection,
        PatternMepHostBinding? host,
        PatternPlanPoint? requested = null)
    {
        Position = position;
        RoomRef = roomRef;
        SymbolRef = symbolRef;
        CategoryToken = categoryToken;
        CategoryName = categoryName;
        FamilyName = familyName;
        TypeName = typeName;
        Role = role;
        PlacementKind = placementKind;
        Strategy = strategy;
        LevelRef = levelRef;
        Target = target;
        MountingHeightFeet = mountingHeightFeet;
        QuarterTurns = quarterTurns;
        ReferenceDirection = referenceDirection;
        Host = host;
        Requested = requested ?? new PatternPlanPoint(target.X, target.Y);
    }

    public int Position { get; }

    public string RoomRef { get; }

    public string SymbolRef { get; }

    public string CategoryToken { get; }

    public string CategoryName { get; }

    public string FamilyName { get; }

    public string TypeName { get; }

    public string Role { get; }

    public string PlacementKind { get; }

    public PatternPlacementStrategyKind Strategy { get; }

    public string LevelRef { get; }

    public MepPoint Target { get; }

    public double MountingHeightFeet { get; }

    public int QuarterTurns { get; }

    public MepPlanDirection ReferenceDirection { get; }

    public PatternMepHostBinding? Host { get; }

    public PatternPlanPoint Requested { get; }
}

internal sealed class PatternMepPlacementIntent
{
    public PatternMepPlacementIntent(
        IReadOnlyList<PatternMepRoomBinding> rooms,
        IReadOnlyList<PatternMepPlacementItem> items)
    {
        Rooms = rooms;
        Items = items;
    }

    public IReadOnlyList<PatternMepRoomBinding> Rooms { get; }

    public IReadOnlyList<PatternMepPlacementItem> Items { get; }
}
