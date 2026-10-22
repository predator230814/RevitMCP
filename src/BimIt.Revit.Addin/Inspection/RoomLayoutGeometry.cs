namespace BimIt.Revit.Addin.Inspection;

internal readonly record struct PlanPoint(double X, double Y);

internal readonly record struct RoomLoopBuild(IReadOnlyList<PlanPoint> Vertices, bool VerticesTruncated);

internal readonly record struct RoomPlanBuild(
    PlanPoint Centroid,
    IReadOnlyList<RoomLoopBuild> Loops,
    bool LoopsTruncated);

internal static class RoomLayoutGeometry
{
    public static bool TryBuildPlan(
        IReadOnlyList<IReadOnlyList<PlanPoint>> loopsInMetres,
        out RoomPlanBuild plan)
    {
        plan = default;
        if (loopsInMetres is null || loopsInMetres.Count == 0)
        {
            return false;
        }

        var built = new List<RoomLoopBuild>();
        var truncated = loopsInMetres.Count > RoomLayoutLimits.MaxLoops;
        var limit = Math.Min(loopsInMetres.Count, RoomLayoutLimits.MaxLoops);
        RoomLoopBuild? largest = null;
        var largestArea = 0.0;
        for (var index = 0; index < limit; index++)
        {
            if (!TryBuildLoop(loopsInMetres[index], out var loop, out var area))
            {
                continue;
            }

            built.Add(loop);
            if (Math.Abs(area) > Math.Abs(largestArea))
            {
                largestArea = area;
                largest = loop;
            }
        }

        if (built.Count == 0 || largest is null || !TryCentroid(largest.Value.Vertices, out var centroid, out var centroidArea))
        {
            return false;
        }

        if (Math.Abs(centroidArea) < RoomLayoutLimits.MinimumLoopAreaSquareMetres)
        {
            return false;
        }

        plan = new RoomPlanBuild(centroid, built, truncated);
        return true;
    }

    public static bool TryCentroid(IReadOnlyList<PlanPoint> loop, out PlanPoint centroid, out double area)
    {
        centroid = default;
        area = 0;
        if (loop.Count < 3)
        {
            return false;
        }

        double twice = 0;
        double cx = 0;
        double cy = 0;
        for (var index = 0; index < loop.Count; index++)
        {
            var current = loop[index];
            var next = loop[(index + 1) % loop.Count];
            var cross = (current.X * next.Y) - (next.X * current.Y);
            twice += cross;
            cx += (current.X + next.X) * cross;
            cy += (current.Y + next.Y) * cross;
        }

        if (Math.Abs(twice) < 1e-9)
        {
            return false;
        }

        area = twice / 2.0;
        centroid = new PlanPoint(cx / (3.0 * twice), cy / (3.0 * twice));
        return double.IsFinite(centroid.X) && double.IsFinite(centroid.Y) && double.IsFinite(area);
    }

    private static bool TryBuildLoop(
        IReadOnlyList<PlanPoint> source,
        out RoomLoopBuild loop,
        out double area)
    {
        loop = default;
        area = 0;
        var cleaned = new List<PlanPoint>(Math.Min(source.Count, RoomLayoutLimits.MaxVerticesPerLoop));
        foreach (var point in source)
        {
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
            {
                continue;
            }

            if (cleaned.Count > 0 && Distance(cleaned[^1], point) < RoomLayoutLimits.ChordToleranceMetres * 0.5)
            {
                continue;
            }

            cleaned.Add(new PlanPoint(Round(point.X), Round(point.Y)));
        }

        if (cleaned.Count > 1 && Distance(cleaned[0], cleaned[^1]) < RoomLayoutLimits.ChordToleranceMetres * 0.5)
        {
            cleaned.RemoveAt(cleaned.Count - 1);
        }

        var verticesTruncated = false;
        if (cleaned.Count > RoomLayoutLimits.MaxVerticesPerLoop)
        {
            cleaned = Downsample(cleaned, RoomLayoutLimits.MaxVerticesPerLoop);
            verticesTruncated = true;
        }

        if (!TryCentroid(cleaned, out _, out area))
        {
            return false;
        }

        loop = new RoomLoopBuild(cleaned, verticesTruncated);
        return true;
    }

    private static List<PlanPoint> Downsample(List<PlanPoint> source, int count)
    {
        var sampled = new List<PlanPoint>(count);
        var last = count - 1;
        for (var index = 0; index < count; index++)
        {
            var sourceIndex = (int)Math.Round(index * (source.Count - 1) / (double)last);
            var point = source[Math.Clamp(sourceIndex, 0, source.Count - 1)];
            if (sampled.Count == 0 || Distance(sampled[^1], point) > 1e-9)
            {
                sampled.Add(point);
            }
        }

        return sampled;
    }

    private static double Distance(PlanPoint left, PlanPoint right)
    {
        var dx = left.X - right.X;
        var dy = left.Y - right.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    public static double Round(double value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);
}
