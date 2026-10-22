namespace BimIt.Revit.Addin.Inspection;

internal static class RoomLayoutLimits
{
    public const int MinElementRefs = 1;

    public const int MaxElementRefs = 20;

    public const int MaxRefLength = 128;

    public const int MaxLoops = 4;

    public const int MaxVerticesPerLoop = 48;

    public const int MaxFeaturesPerRoom = 12;

    public const int MaxOpeningScan = 4_000;

    public const int MaxTextLength = 120;

    public const double ChordToleranceMetres = 0.05;

    public const double MinimumLoopAreaSquareMetres = 0.05;
}
