namespace BimIt.Revit.Assistant.Patterns;

internal static class PatternPlacements
{
    public const string Boundary = "boundary";

    public const string Interior = "interior";

    public static bool IsPlacement(string? value) => value is Boundary or Interior;
}

internal static class PatternAnchors
{
    public const string RoomCenter = "room_center";

    public const string Entrance = "entrance";

    public const string NearestBoundary = "nearest_boundary";

    public const string OppositeBoundary = "opposite_boundary";

    public const string Corner = "corner";

    public const string SourceCorrespondence = "source_correspondence";

    public static readonly string[] All =
    [
        RoomCenter,
        Entrance,
        NearestBoundary,
        OppositeBoundary,
        Corner,
        SourceCorrespondence,
    ];

    public static bool IsAnchor(string? value) => value is not null && All.Contains(value, StringComparer.Ordinal);

    public static string PlacementFor(string anchor) => anchor == RoomCenter ? PatternPlacements.Interior : PatternPlacements.Boundary;
}

internal static class PatternArrangements
{
    public const string Single = "single";

    public const string SymmetricPair = "symmetric_pair";

    public const string Distributed = "distributed";

    public const string Cluster = "cluster";

    public static bool IsKnown(string? value) => value is Single or SymmetricPair or Distributed or Cluster;

    public static bool IsSupported(string? value) => value is Single or SymmetricPair;

    public static int CountFor(string arrangement) => arrangement == SymmetricPair ? 2 : 1;
}

internal static class PatternBasis
{
    public const string Observed = "observed";

    public const string UserStated = "user_stated";

    public static bool IsBasis(string? value) => value is Observed or UserStated;
}

internal sealed record PatternSpec(
    string Role,
    string Placement,
    string Anchor,
    int Count,
    string Arrangement,
    double OffsetMetres = 0,
    string? Group = null,
    double? Confidence = null,
    string? Basis = null,
    IReadOnlyList<int>? DeviceIndexes = null)
{
    public const int MinCount = 1;

    public const int MaxCount = 4;

    public const int MaxSpecs = 8;

    public bool IsPair => Arrangement == PatternArrangements.SymmetricPair;
}

internal sealed record PatternAnchor(string Label, double X, double Y);

internal sealed record PatternSpecEvidence(
    IReadOnlyList<int> DeviceIndexes,
    string? Placement = null,
    PlanPoint? Centroid = null,
    double? Spread = null,
    int? EdgeIndex = null,
    string? Side = null,
    double? Along = null,
    double? EdgeSpread = null,
    PlanPoint? Axis = null,
    double? AxisSpread = null)
{
    public static PatternSpecEvidence Empty { get; } = new([]);
}

internal static class PatternEvidence
{
    public static IReadOnlyList<PatternSpec> AssignDevices(IReadOnlyList<PatternSpec> specs, IReadOnlyList<SourceDevice> devices)
    {
        var claimed = new bool[devices.Count];
        var explicitIndexes = new IReadOnlyList<int>?[specs.Count];
        for (var index = 0; index < specs.Count; index++)
        {
            var spec = specs[index];
            if (spec.DeviceIndexes is not { Count: > 0 } requested)
            {
                continue;
            }

            var kept = requested
                .Where(device => device >= 0 && device < devices.Count && devices[device].Role == spec.Role && !claimed[device])
                .Distinct()
                .Take(PatternSpec.MaxCount)
                .ToArray();
            foreach (var device in kept)
            {
                claimed[device] = true;
            }

            explicitIndexes[index] = kept;
        }

        var assigned = new List<PatternSpec>(specs.Count);
        for (var index = 0; index < specs.Count; index++)
        {
            var spec = specs[index];
            if (explicitIndexes[index] is { Count: > 0 } kept)
            {
                assigned.Add(spec with { DeviceIndexes = kept });
                continue;
            }

            var candidates = Enumerable.Range(0, devices.Count)
                .Where(device => !claimed[device] && devices[device].Role == spec.Role)
                .ToArray();
            var pool = candidates.Where(device => devices[device].PlacementKind == spec.Placement).ToArray();
            if (pool.Length == 0)
            {
                pool = candidates;
            }

            if (pool.Length == 0)
            {
                assigned.Add(spec with { DeviceIndexes = [] });
                continue;
            }

            var first = pool[0];
            var chosen = new List<int> { first };
            foreach (var device in pool.Skip(1)
                .OrderBy(device => Distance(devices[device], devices[first]))
                .ThenBy(device => device)
                .Take(Math.Max(0, spec.Count - 1)))
            {
                chosen.Add(device);
            }

            foreach (var device in chosen)
            {
                claimed[device] = true;
            }

            assigned.Add(spec with { DeviceIndexes = chosen.ToArray() });
        }

        return assigned;
    }

    // The pattern must be in a Y-up frame, so top and bottom match the drawing.
    public static IReadOnlyList<PatternSpecEvidence> Describe(SourceRoomPattern pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        var specs = pattern.Specs ?? [];
        var result = new List<PatternSpecEvidence>(specs.Count);
        var boundary = pattern.Boundary;
        if (boundary.Count < 3)
        {
            return specs.Select(_ => PatternSpecEvidence.Empty).ToArray();
        }

        var minX = boundary.Min(point => point.X);
        var maxX = boundary.Max(point => point.X);
        var minY = boundary.Min(point => point.Y);
        var maxY = boundary.Max(point => point.Y);
        var width = Math.Max(maxX - minX, 1e-9);
        var height = Math.Max(maxY - minY, 1e-9);
        foreach (var spec in specs)
        {
            var indexes = (spec.DeviceIndexes ?? []).Where(index => index >= 0 && index < pattern.Devices.Count).ToArray();
            if (indexes.Length == 0)
            {
                result.Add(PatternSpecEvidence.Empty);
                continue;
            }

            var points = indexes.Select(index => new PlanPoint(pattern.Devices[index].X, pattern.Devices[index].Y)).ToArray();
            var centre = new PlanPoint(points.Average(point => point.X), points.Average(point => point.Y));
            var placement = indexes.Count(index => pattern.Devices[index].PlacementKind == PatternPlacements.Boundary) * 2 >= indexes.Length
                ? PatternPlacements.Boundary
                : PatternPlacements.Interior;
            var edge = NearestEdge(boundary, centre, out var along);
            var edgeLength = Length(boundary[edge], boundary[(edge + 1) % boundary.Count]);
            double? spread = null;
            double? edgeSpread = null;
            PlanPoint? axis = null;
            double? axisSpread = null;
            if (points.Length >= 2)
            {
                var (first, second) = Farthest(points);
                var distance = Length(first, second);
                if (distance > 1e-9)
                {
                    var direction = new PlanPoint((second.X - first.X) / distance, (second.Y - first.Y) / distance);
                    axis = Canonical(direction);
                    spread = Round(distance / Math.Max(width, height));
                    edgeSpread = edgeLength > 1e-9 ? Round(Math.Min(distance / edgeLength, 1)) : null;
                    var extent = Extent(boundary, axis.Value);
                    axisSpread = extent > 1e-9 ? Round(Math.Min(distance / extent, 1)) : null;
                }
            }

            result.Add(new PatternSpecEvidence(
                indexes,
                placement,
                new PlanPoint(Round((centre.X - minX) / width), Round((centre.Y - minY) / height)),
                spread,
                edge,
                Side(boundary, edge),
                Round(along),
                edgeSpread,
                axis,
                axisSpread));
        }

        return result;
    }

    public static string? Side(IReadOnlyList<PlanPoint> loop, int edge)
    {
        if (loop.Count < 3 || edge < 0 || edge >= loop.Count)
        {
            return null;
        }

        var outward = Outward(loop, edge);
        if (outward is not PlanPoint normal)
        {
            return null;
        }

        return Math.Abs(normal.X) >= Math.Abs(normal.Y)
            ? normal.X > 0 ? "right" : "left"
            : normal.Y > 0 ? "top" : "bottom";
    }

    public static string? Compass(IReadOnlyList<PlanPoint> loop, PlanPoint start, PlanPoint end)
    {
        for (var index = 0; index < loop.Count; index++)
        {
            if (loop[index] == start && loop[(index + 1) % loop.Count] == end)
            {
                return Compass(loop, index);
            }
        }

        return null;
    }

    public static string? Compass(IReadOnlyList<PlanPoint> loop, int edge)
    {
        return Side(loop, edge) switch
        {
            "right" => "east",
            "left" => "west",
            "top" => "north",
            "bottom" => "south",
            _ => null,
        };
    }

    private static PlanPoint? Outward(IReadOnlyList<PlanPoint> loop, int edge)
    {
        var start = loop[edge];
        var end = loop[(edge + 1) % loop.Count];
        var length = Length(start, end);
        if (length < 1e-12)
        {
            return null;
        }

        var dx = (end.X - start.X) / length;
        var dy = (end.Y - start.Y) / length;
        return SignedArea(loop) >= 0 ? new PlanPoint(dy, -dx) : new PlanPoint(-dy, dx);
    }

    private static double SignedArea(IReadOnlyList<PlanPoint> loop)
    {
        double twice = 0;
        for (var index = 0; index < loop.Count; index++)
        {
            var current = loop[index];
            var next = loop[(index + 1) % loop.Count];
            twice += (current.X * next.Y) - (next.X * current.Y);
        }

        return twice / 2;
    }

    private static int NearestEdge(IReadOnlyList<PlanPoint> loop, PlanPoint point, out double along)
    {
        var best = 0;
        var bestDistance = double.MaxValue;
        along = 0;
        for (var index = 0; index < loop.Count; index++)
        {
            var start = loop[index];
            var end = loop[(index + 1) % loop.Count];
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            var squared = (dx * dx) + (dy * dy);
            var t = squared < 1e-18 ? 0 : Math.Clamp((((point.X - start.X) * dx) + ((point.Y - start.Y) * dy)) / squared, 0, 1);
            var projected = new PlanPoint(start.X + (t * dx), start.Y + (t * dy));
            var distance = Length(point, projected);
            if (distance < bestDistance - 1e-12)
            {
                bestDistance = distance;
                best = index;
                along = t;
            }
        }

        return best;
    }

    private static (PlanPoint First, PlanPoint Second) Farthest(IReadOnlyList<PlanPoint> points)
    {
        var first = points[0];
        var second = points[1];
        var best = -1.0;
        for (var i = 0; i < points.Count; i++)
        {
            for (var j = i + 1; j < points.Count; j++)
            {
                var distance = Length(points[i], points[j]);
                if (distance > best)
                {
                    best = distance;
                    first = points[i];
                    second = points[j];
                }
            }
        }

        return (first, second);
    }

    private static PlanPoint Canonical(PlanPoint direction)
    {
        var flip = direction.X < -1e-9 || (Math.Abs(direction.X) <= 1e-9 && direction.Y < 0);
        return flip
            ? new PlanPoint(Round(-direction.X), Round(-direction.Y))
            : new PlanPoint(Round(direction.X), Round(direction.Y));
    }

    internal static double Extent(IReadOnlyList<PlanPoint> loop, PlanPoint axis)
    {
        var min = double.MaxValue;
        var max = double.MinValue;
        foreach (var point in loop)
        {
            var projected = (point.X * axis.X) + (point.Y * axis.Y);
            min = Math.Min(min, projected);
            max = Math.Max(max, projected);
        }

        return max - min;
    }

    private static double Distance(SourceDevice left, SourceDevice right)
    {
        var dx = left.X - right.X;
        var dy = left.Y - right.Y;
        return (dx * dx) + (dy * dy);
    }

    private static double Length(PlanPoint left, PlanPoint right)
    {
        var dx = left.X - right.X;
        var dy = left.Y - right.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static double Round(double value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);
}
