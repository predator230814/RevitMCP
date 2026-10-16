namespace BimIt.Revit.Addin.Intents;

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

internal sealed class MepDevicePlacementItem
{
    public MepDevicePlacementItem(int requestPosition, double offsetAlongHost, double verticalOffset, MepPoint target)
    {
        RequestPosition = requestPosition;
        OffsetAlongHost = offsetAlongHost;
        VerticalOffset = verticalOffset;
        Target = target;
    }

    public int RequestPosition { get; }

    public double OffsetAlongHost { get; }

    public double VerticalOffset { get; }

    public MepPoint Target { get; }
}

/// <summary>
/// Immutable RevitMCP-owned placement snapshot. It does not retain live Revit API objects.
/// </summary>
internal sealed class MepDevicePlacementIntent
{
    public MepDevicePlacementIntent(
        string lengthUnitId,
        string templateElementRef,
        string symbolElementRef,
        string categoryToken,
        string categoryName,
        string familyName,
        string typeName,
        string hostElementRef,
        string hostName,
        string levelElementRef,
        string levelName,
        MepPoint templateLocation,
        MepPoint hostStart,
        MepPoint hostEnd,
        bool facingFlipped,
        bool handFlipped,
        bool canFlipFacing,
        bool canFlipHand,
        IReadOnlyList<MepDevicePlacementItem> items)
    {
        LengthUnitId = lengthUnitId;
        TemplateElementRef = templateElementRef;
        SymbolElementRef = symbolElementRef;
        CategoryToken = categoryToken;
        CategoryName = categoryName;
        FamilyName = familyName;
        TypeName = typeName;
        HostElementRef = hostElementRef;
        HostName = hostName;
        LevelElementRef = levelElementRef;
        LevelName = levelName;
        TemplateLocation = templateLocation;
        HostStart = hostStart;
        HostEnd = hostEnd;
        FacingFlipped = facingFlipped;
        HandFlipped = handFlipped;
        CanFlipFacing = canFlipFacing;
        CanFlipHand = canFlipHand;
        Items = items;
    }

    public string LengthUnitId { get; }

    public string TemplateElementRef { get; }

    public string SymbolElementRef { get; }

    public string CategoryToken { get; }

    public string CategoryName { get; }

    public string FamilyName { get; }

    public string TypeName { get; }

    public string HostElementRef { get; }

    public string HostName { get; }

    public string LevelElementRef { get; }

    public string LevelName { get; }

    public MepPoint TemplateLocation { get; }

    public MepPoint HostStart { get; }

    public MepPoint HostEnd { get; }

    public bool FacingFlipped { get; }

    public bool HandFlipped { get; }

    public bool CanFlipFacing { get; }

    public bool CanFlipHand { get; }

    public IReadOnlyList<MepDevicePlacementItem> Items { get; }
}
