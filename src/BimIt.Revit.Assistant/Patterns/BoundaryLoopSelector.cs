namespace BimIt.Revit.Assistant.Patterns;

internal static class BoundaryLoopSelector
{
    public const double AmbiguousAreaRatio = 0.85;

    public const string NoOuterLoop = "no_outer_loop";

    public const string AmbiguousOuterLoop = "ambiguous_outer_loop";

    public static bool TrySelect(
        IReadOnlyList<IReadOnlyList<PlanPoint>> loops,
        out IReadOnlyList<PlanPoint> outer,
        out IReadOnlyList<IReadOnlyList<PlanPoint>> holes,
        out string? reason)
    {
        outer = [];
        holes = [];
        reason = NoOuterLoop;
        var measured = new List<(IReadOnlyList<PlanPoint> Loop, double Area)>();
        foreach (var loop in loops)
        {
            if (loop.Count < 3 || !TryArea(loop, out var area) || area < 1e-4)
            {
                continue;
            }

            measured.Add((loop, area));
        }

        if (measured.Count == 0)
        {
            return false;
        }

        measured.Sort(static (left, right) => right.Area.CompareTo(left.Area));
        if (measured.Count > 1 && measured[1].Area / measured[0].Area >= AmbiguousAreaRatio)
        {
            reason = AmbiguousOuterLoop;
            return false;
        }

        outer = measured[0].Loop;
        holes = measured.Skip(1).Select(item => item.Loop).ToArray();
        reason = null;
        return true;
    }

    internal static bool TryArea(IReadOnlyList<PlanPoint> loop, out double area)
    {
        double twice = 0;
        for (var index = 0; index < loop.Count; index++)
        {
            var current = loop[index];
            var next = loop[(index + 1) % loop.Count];
            twice += (current.X * next.Y) - (next.X * current.Y);
        }

        area = Math.Abs(twice) * 0.5;
        return double.IsFinite(area);
    }
}
