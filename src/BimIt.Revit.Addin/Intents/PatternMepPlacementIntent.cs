namespace BimIt.Revit.Addin.Intents;

internal enum PatternPlacementStrategyKind
{
    InteriorLevel = 1,
    InteriorLevelWorkPlane = 2,
    BoundaryWallHosted = 3,
    BoundaryWallFace = 4,
}

internal readonly record struct PatternPlanPoint(double X, double Y);

internal sealed class PatternWallFacts
{
    public required string WallRef { get; init; }

    public required string WallName { get; init; }

    public required PatternPlanPoint Start { get; init; }

    public required PatternPlanPoint End { get; init; }

    public required bool Straight { get; init; }

    public required bool FaceResolved { get; init; }
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
}

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
        IReadOnlyList<PatternPlanPoint> loop)
    {
        RoomRef = roomRef;
        Label = label;
        LevelRef = levelRef;
        LevelName = levelName;
        LevelElevationFeet = levelElevationFeet;
        Loop = loop;
    }

    public string RoomRef { get; }

    public string Label { get; }

    public string LevelRef { get; }

    public string LevelName { get; }

    public double LevelElevationFeet { get; }

    public IReadOnlyList<PatternPlanPoint> Loop { get; }
}

internal sealed class PatternMepHostBinding
{
    public PatternMepHostBinding(string wallRef, string wallName, MepPoint start, MepPoint end, MepPlanDirection insideNormal)
    {
        WallRef = wallRef;
        WallName = wallName;
        Start = start;
        End = end;
        InsideNormal = insideNormal;
    }

    public string WallRef { get; }

    public string WallName { get; }

    public MepPoint Start { get; }

    public MepPoint End { get; }

    public MepPlanDirection InsideNormal { get; }
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
        PatternMepHostBinding? host)
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
