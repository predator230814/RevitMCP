namespace BimIt.Revit.Addin.Intents;

internal enum MepPlacementStrategyKind
{
    LevelBased = 1,
    WallHosted = 2,
    WorkPlaneBasedLevel = 3
}

internal readonly record struct MepPoint(double X, double Y, double Z)
{
    public double DistanceTo(MepPoint other)
    {
        var dx = X - other.X;
        var dy = Y - other.Y;
        var dz = Z - other.Z;
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }
}

internal readonly record struct MepOrientation(
    double RotationRadians,
    bool FacingFlipped,
    bool HandFlipped,
    bool CanFlipFacing,
    bool CanFlipHand);

internal readonly record struct MepPlanDirection(double X, double Y);

internal sealed class MepWallBinding
{
    public MepWallBinding(string hostElementRef, string hostName, MepPoint hostStart, MepPoint hostEnd)
    {
        HostElementRef = hostElementRef;
        HostName = hostName;
        HostStart = hostStart;
        HostEnd = hostEnd;
    }

    public string HostElementRef { get; }

    public string HostName { get; }

    public MepPoint HostStart { get; }

    public MepPoint HostEnd { get; }
}

internal sealed class MepDevicePlacementItem
{
    public MepDevicePlacementItem(int requestPosition, double offsetX, double offsetY, double verticalOffset, MepPoint target)
    {
        RequestPosition = requestPosition;
        OffsetX = offsetX;
        OffsetY = offsetY;
        VerticalOffset = verticalOffset;
        Target = target;
    }

    public int RequestPosition { get; }

    public double OffsetX { get; }

    public double OffsetY { get; }

    public double VerticalOffset { get; }

    public MepPoint Target { get; }
}

/// <summary>
/// Immutable RevitMCP-owned placement snapshot. It does not retain live Revit API objects.
/// </summary>
internal sealed class MepDevicePlacementIntent
{
    public MepDevicePlacementIntent(
        MepPlacementStrategyKind strategy,
        string lengthUnitId,
        string templateElementRef,
        string symbolElementRef,
        string categoryToken,
        string categoryName,
        string familyName,
        string typeName,
        string levelElementRef,
        string levelName,
        MepPoint templateLocation,
        MepOrientation orientation,
        MepWallBinding? wall,
        MepPlanDirection? referenceDirection,
        IReadOnlyList<MepDevicePlacementItem> items)
    {
        Strategy = strategy;
        LengthUnitId = lengthUnitId;
        TemplateElementRef = templateElementRef;
        SymbolElementRef = symbolElementRef;
        CategoryToken = categoryToken;
        CategoryName = categoryName;
        FamilyName = familyName;
        TypeName = typeName;
        LevelElementRef = levelElementRef;
        LevelName = levelName;
        TemplateLocation = templateLocation;
        Orientation = orientation;
        Wall = wall;
        ReferenceDirection = referenceDirection;
        Items = items;
    }

    public MepPlacementStrategyKind Strategy { get; }

    public string LengthUnitId { get; }

    public string TemplateElementRef { get; }

    public string SymbolElementRef { get; }

    public string CategoryToken { get; }

    public string CategoryName { get; }

    public string FamilyName { get; }

    public string TypeName { get; }

    public string LevelElementRef { get; }

    public string LevelName { get; }

    public MepPoint TemplateLocation { get; }

    public MepOrientation Orientation { get; }

    public MepWallBinding? Wall { get; }

    public MepPlanDirection? ReferenceDirection { get; }

    public IReadOnlyList<MepDevicePlacementItem> Items { get; }
}
