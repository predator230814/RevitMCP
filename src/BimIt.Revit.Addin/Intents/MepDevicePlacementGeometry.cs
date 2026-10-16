namespace BimIt.Revit.Addin.Intents;

internal static class MepDevicePlacementGeometry
{
    /// <summary>One millimetre expressed in Revit internal feet. 1 ft = 304.8 mm.</summary>
    internal const double OccupancyToleranceFeet = 1.0 / 304.8;

    internal const double MetresToFeet = 1.0 / 0.3048;

    internal const double MaxAlongHostFeet = 100.0 * MetresToFeet;

    internal const double MaxVerticalFeet = 10.0 * MetresToFeet;

    public static bool WithinBounds(double alongHostFeet, double verticalFeet)
    {
        return double.IsFinite(alongHostFeet)
            && double.IsFinite(verticalFeet)
            && Math.Abs(alongHostFeet) <= MaxAlongHostFeet + 1e-9
            && Math.Abs(verticalFeet) <= MaxVerticalFeet + 1e-9;
    }

    public static bool TryTarget(
        MepPoint template,
        MepPoint hostStart,
        MepPoint hostEnd,
        double alongHostFeet,
        double verticalFeet,
        out MepPoint target)
    {
        target = default;
        if (!TryDirection(hostStart, hostEnd, out var direction, out _))
        {
            return false;
        }

        target = new MepPoint(
            template.X + (direction.X * alongHostFeet),
            template.Y + (direction.Y * alongHostFeet),
            template.Z + (direction.Z * alongHostFeet) + verticalFeet);
        return double.IsFinite(target.X) && double.IsFinite(target.Y) && double.IsFinite(target.Z);
    }

    public static bool ProjectsWithinHost(MepPoint hostStart, MepPoint hostEnd, MepPoint target)
    {
        if (!TryDirection(hostStart, hostEnd, out var direction, out var length))
        {
            return false;
        }

        var parameter = ((target.X - hostStart.X) * direction.X)
            + ((target.Y - hostStart.Y) * direction.Y)
            + ((target.Z - hostStart.Z) * direction.Z);
        return parameter >= -OccupancyToleranceFeet && parameter <= length + OccupancyToleranceFeet;
    }

    public static bool IsOccupied(MepPoint target, IReadOnlyList<MepPoint> occupants)
    {
        foreach (var occupant in occupants)
        {
            if (target.DistanceTo(occupant) <= OccupancyToleranceFeet)
            {
                return true;
            }
        }

        return false;
    }

    public static bool SamePoint(MepPoint left, MepPoint right)
    {
        return left.DistanceTo(right) <= OccupancyToleranceFeet;
    }

    private static bool TryDirection(MepPoint start, MepPoint end, out MepPoint direction, out double length)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var dz = end.Z - start.Z;
        length = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        if (!double.IsFinite(length) || length <= OccupancyToleranceFeet)
        {
            direction = default;
            return false;
        }

        direction = new MepPoint(dx / length, dy / length, dz / length);
        return true;
    }
}
