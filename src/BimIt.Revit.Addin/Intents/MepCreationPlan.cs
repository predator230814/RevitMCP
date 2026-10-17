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

    /// <summary>
    /// Level-point creation receives Z as an offset from <c>Level.ProjectElevation</c>.
    /// The stored target stays in absolute project coordinates. Wall and level-plane creation keep that absolute point.
    /// </summary>
    public static MepPoint CreationPoint(MepCreationOverload overload, MepPoint absoluteTarget, double levelProjectElevation)
    {
        if (overload != MepCreationOverload.LevelPoint)
        {
            return absoluteTarget;
        }

        return new MepPoint(absoluteTarget.X, absoluteTarget.Y, absoluteTarget.Z - levelProjectElevation);
    }
}
