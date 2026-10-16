namespace BimIt.Revit.Addin.Intents;

internal enum MepCreationOverload
{
    LevelPoint,
    WallHost,
    LevelPlaneReference
}

internal static class MepCreationPlan
{
    public static bool TrySelect(MepPlacementStrategyKind strategy, out MepCreationOverload overload)
    {
        switch (strategy)
        {
            case MepPlacementStrategyKind.LevelBased:
                overload = MepCreationOverload.LevelPoint;
                return true;
            case MepPlacementStrategyKind.WallHosted:
                overload = MepCreationOverload.WallHost;
                return true;
            case MepPlacementStrategyKind.WorkPlaneBasedLevel:
                overload = MepCreationOverload.LevelPlaneReference;
                return true;
            default:
                overload = default;
                return false;
        }
    }
}
