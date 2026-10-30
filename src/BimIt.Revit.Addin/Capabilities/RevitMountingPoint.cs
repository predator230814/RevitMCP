using Autodesk.Revit.DB;
using BimIt.Revit.Addin.Intents;

namespace BimIt.Revit.Addin.Capabilities;

/// <summary>
/// The point a pattern places, verifies, and compares for occupancy. A level-based family can keep its insertion
/// point above its base, so its mounting height is the Revit "Elevation from Level" above its Level, which is what
/// the modeler reads in Properties. Any other instance uses its location point.
/// </summary>
internal static class RevitMountingPoint
{
    public static bool TryRead(Document document, FamilyInstance instance, out MepPoint point)
    {
        point = default;
        if (instance.Location is not LocationPoint location || location.Point is null)
        {
            return false;
        }

        var origin = location.Point;
        point = TryElevation(document, instance, out var level, out var elevation)
            ? new MepPoint(origin.X, origin.Y, level.Elevation + elevation.AsDouble())
            : new MepPoint(origin.X, origin.Y, origin.Z);
        return true;
    }

    /// <summary>Sets the Elevation from Level of a level-based instance. False when the family has none to set.</summary>
    public static bool TrySetHeight(Document document, FamilyInstance instance, double heightAboveLevelFeet)
    {
        if (!TryElevation(document, instance, out _, out var elevation) || elevation.IsReadOnly)
        {
            return false;
        }

        return elevation.Set(heightAboveLevelFeet);
    }

    private static bool TryElevation(Document document, FamilyInstance instance, out Level level, out Parameter elevation)
    {
        level = null!;
        elevation = null!;
        if (instance.Symbol?.Family?.FamilyPlacementType != FamilyPlacementType.OneLevelBased
            || instance.Host is not null
            || document.GetElement(instance.LevelId) is not Level found
            || instance.get_Parameter(BuiltInParameter.INSTANCE_ELEVATION_PARAM) is not { StorageType: StorageType.Double } parameter)
        {
            return false;
        }

        level = found;
        elevation = parameter;
        return true;
    }
}
