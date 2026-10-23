namespace BimIt.Revit.Addin.Intents;

internal static class MepDevicePlacementGeometry
{
    /// <summary>One millimetre expressed in Revit internal feet. 1 ft = 304.8 mm.</summary>
    internal const double OccupancyToleranceFeet = 1.0 / 304.8;

    /// <summary>About 0.0057 degrees. Orientation must match inside this tolerance.</summary>
    internal const double OrientationToleranceRadians = 1e-4;

    internal const double MetresToFeet = 1.0 / 0.3048;

    internal const double MaxPlanFeet = 100.0 * MetresToFeet;

    internal const double MaxVerticalFeet = 10.0 * MetresToFeet;

    public static bool WithinBounds(double offsetXFeet, double offsetYFeet, double verticalFeet)
    {
        return double.IsFinite(offsetXFeet)
            && double.IsFinite(offsetYFeet)
            && double.IsFinite(verticalFeet)
            && Math.Abs(offsetXFeet) <= MaxPlanFeet + 1e-9
            && Math.Abs(offsetYFeet) <= MaxPlanFeet + 1e-9
            && Math.Abs(verticalFeet) <= MaxVerticalFeet + 1e-9;
    }

    public static bool TryProjectTarget(
        MepPoint template,
        double offsetXFeet,
        double offsetYFeet,
        double verticalFeet,
        out MepPoint target)
    {
        target = new MepPoint(template.X + offsetXFeet, template.Y + offsetYFeet, template.Z + verticalFeet);
        return double.IsFinite(target.X) && double.IsFinite(target.Y) && double.IsFinite(target.Z);
    }

    public static bool TryWallPlacement(
        MepPoint template,
        MepPoint hostStart,
        MepPoint hostEnd,
        MepPoint target,
        out bool withinSegment)
    {
        withinSegment = false;
        var planX = hostEnd.X - hostStart.X;
        var planY = hostEnd.Y - hostStart.Y;
        var planLength = Math.Sqrt((planX * planX) + (planY * planY));
        if (!double.IsFinite(planLength) || planLength <= OccupancyToleranceFeet)
        {
            return false;
        }

        var dirX = planX / planLength;
        var dirY = planY / planLength;
        var moveX = target.X - template.X;
        var moveY = target.Y - template.Y;
        var along = (moveX * dirX) + (moveY * dirY);
        var perpX = moveX - (along * dirX);
        var perpY = moveY - (along * dirY);
        var perpendicular = Math.Sqrt((perpX * perpX) + (perpY * perpY));
        if (!double.IsFinite(perpendicular) || perpendicular > OccupancyToleranceFeet)
        {
            return false;
        }

        if (!TryDirection(hostStart, hostEnd, out var direction, out var length))
        {
            return false;
        }

        var templateParameter = ((template.X - hostStart.X) * direction.X)
            + ((template.Y - hostStart.Y) * direction.Y)
            + ((template.Z - hostStart.Z) * direction.Z);
        var scale = length / planLength;
        var targetParameter = templateParameter + (along * scale);
        withinSegment = targetParameter >= -OccupancyToleranceFeet && targetParameter <= length + OccupancyToleranceFeet;
        return true;
    }

    public static bool TryPlanDirection(double rotationRadians, out MepPlanDirection direction)
    {
        direction = default;
        if (!double.IsFinite(rotationRadians))
        {
            return false;
        }

        var x = Math.Cos(rotationRadians);
        var y = Math.Sin(rotationRadians);
        var length = Math.Sqrt((x * x) + (y * y));
        if (!double.IsFinite(length) || length <= 1e-9)
        {
            return false;
        }

        direction = new MepPlanDirection(x / length, y / length);
        return true;
    }

    public static bool SameAngle(double left, double right)
    {
        if (!double.IsFinite(left) || !double.IsFinite(right))
        {
            return false;
        }

        var delta = Math.Atan2(Math.Sin(left - right), Math.Cos(left - right));
        return Math.Abs(delta) <= OrientationToleranceRadians;
    }

    public static bool OrientationMatches(MepPlacementStrategyKind strategy, double actualRotation, bool facingFlipped, bool handFlipped, MepOrientation expected)
    {
        var flips = facingFlipped == expected.FacingFlipped && handFlipped == expected.HandFlipped;
        if (strategy == MepPlacementStrategyKind.WallHosted)
        {
            return flips;
        }

        return flips && SameAngle(actualRotation, expected.RotationRadians);
    }

    public static bool SameDirection(MepPlanDirection left, MepPlanDirection right)
    {
        var dot = (left.X * right.X) + (left.Y * right.Y);
        return double.IsFinite(dot) && dot >= Math.Cos(OrientationToleranceRadians);
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

    public static bool SameWallPlacement(MepPoint actual, MepPoint target, double planToleranceFeet)
    {
        if (!double.IsFinite(planToleranceFeet) || planToleranceFeet < 0)
        {
            return false;
        }

        if (Math.Abs(actual.Z - target.Z) > OccupancyToleranceFeet)
        {
            return false;
        }

        var dx = actual.X - target.X;
        var dy = actual.Y - target.Y;
        var plan = Math.Sqrt((dx * dx) + (dy * dy));
        return double.IsFinite(plan) && plan <= planToleranceFeet;
    }

    public static bool SameHostedWallPlacement(
        MepPoint actual,
        MepPoint target,
        MepPoint wallStart,
        MepPoint wallEnd,
        double alongToleranceFeet)
    {
        if (!double.IsFinite(alongToleranceFeet) || alongToleranceFeet < 0)
        {
            return false;
        }

        if (Math.Abs(actual.Z - target.Z) > OccupancyToleranceFeet)
        {
            return false;
        }

        var dx = wallEnd.X - wallStart.X;
        var dy = wallEnd.Y - wallStart.Y;
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        if (!double.IsFinite(length) || length <= OccupancyToleranceFeet)
        {
            return false;
        }

        var dirX = dx / length;
        var dirY = dy / length;
        var targetAlong = ((target.X - wallStart.X) * dirX) + ((target.Y - wallStart.Y) * dirY);
        var actualAlong = ((actual.X - wallStart.X) * dirX) + ((actual.Y - wallStart.Y) * dirY);
        var along = Math.Abs(actualAlong - targetAlong);
        return double.IsFinite(along) && along <= alongToleranceFeet;
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
