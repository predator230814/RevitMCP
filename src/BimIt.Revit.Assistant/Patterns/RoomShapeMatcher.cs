namespace BimIt.Revit.Assistant.Patterns;

internal readonly record struct PlanPoint(double X, double Y);

internal sealed record SourceDevice(
    string Role,
    double X,
    double Y,
    string PlacementKind,
    string? FamilyHint);

internal sealed record SourceRoomPattern(
    IReadOnlyList<PlanPoint> Boundary,
    PlanPoint? Entrance,
    IReadOnlyList<SourceDevice> Devices,
    IReadOnlyList<PatternRule>? Rules = null,
    IReadOnlyList<PatternAnchor>? Anchors = null,
    SemanticReport? Semantics = null);

internal sealed record CandidateOpening(double X, double Y, string Kind);

internal sealed record CandidateRoom(
    string? Name,
    string? Number,
    string? LevelName,
    double CentroidX,
    double CentroidY,
    IReadOnlyList<PlanPoint> Loop,
    bool VerticesTruncated,
    IReadOnlyList<CandidateOpening> Openings,
    string? ElementRef = null,
    IReadOnlyList<IReadOnlyList<PlanPoint>>? Holes = null,
    string? SourceKind = null,
    string? LinkLabel = null,
    string? LoopFailure = null);

internal sealed record ProjectedDevice(
    string Role,
    string PlacementKind,
    double LocalX,
    double LocalY,
    double NormalizedX,
    double NormalizedY,
    string? FamilyHint = null);

internal sealed record RoomShapeMatch(
    string Label,
    string? LevelName,
    string Band,
    double Score,
    int RotationDegrees,
    bool Mirrored,
    string? Reason,
    IReadOnlyList<PlanPoint> Outline,
    IReadOnlyList<ProjectedDevice> Devices,
    string? RoomRef = null,
    string? SourceKind = null,
    string? LinkLabel = null);

internal static class RoomShapeMatcher
{
    public const double HighConfidence = 0.82;

    public const double LowerConfidence = 0.55;

    public const int SampleCount = 48;

    public const int MaxStraightVertices = 16;

    public const double OrthogonalToleranceRadians = 12.0 * Math.PI / 180.0;

    public const double AspectMismatch = 0.28;

    private const double RmsDivisor = 0.48;

    private const double EntranceTie = 0.04;

    public static IReadOnlyList<RoomShapeMatch> Match(
        SourceRoomPattern source,
        IReadOnlyList<CandidateRoom> candidates)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(candidates);
        var preparedSource = Prepare(source.Boundary);
        var matches = new List<RoomShapeMatch>(candidates.Count);
        for (var index = 0; index < candidates.Count; index++)
        {
            matches.Add(MatchOne(source, preparedSource, candidates[index], index));
        }

        return matches;
    }

    private static RoomShapeMatch MatchOne(
        SourceRoomPattern source,
        PreparedShape? sourceShape,
        CandidateRoom candidate,
        int index)
    {
        var label = Label(candidate, index);
        if (candidate.LoopFailure is not null)
        {
            return Rejected(label, candidate.LevelName, candidate.LoopFailure, candidate);
        }

        if (candidate.VerticesTruncated)
        {
            return Rejected(label, candidate.LevelName, "boundary_too_complex", candidate);
        }

        var prepared = Prepare(candidate.Loop);
        if (prepared is not { } room || sourceShape is not { } sourcePrepared)
        {
            return Rejected(label, candidate.LevelName, "degenerate_boundary", candidate);
        }

        if (room.Clean.Count > MaxStraightVertices)
        {
            return Rejected(label, candidate.LevelName, "boundary_too_complex", candidate);
        }

        if (SelfIntersects(room.Clean))
        {
            return Rejected(label, candidate.LevelName, "self_intersecting_boundary", candidate);
        }

        if (!IsMostlyOrthogonal(room.Clean))
        {
            return Rejected(label, candidate.LevelName, "non_orthogonal_boundary", candidate);
        }

        var aspectGap = Math.Abs(sourcePrepared.Aspect - room.Aspect) / Math.Max(sourcePrepared.Aspect, room.Aspect);
        var best = ChooseTransform(source, sourcePrepared, room, candidate);
        var score = Math.Clamp(1.0 - (best.Rms / RmsDivisor), 0, 1);
        if (aspectGap > AspectMismatch)
        {
            score *= 0.45;
        }

        score = Math.Round(score, 4, MidpointRounding.AwayFromZero);
        var reason = score < LowerConfidence
            ? aspectGap > AspectMismatch ? "aspect_mismatch" : "low_shape_similarity"
            : null;
        var band = score >= HighConfidence ? "high" : score >= LowerConfidence ? "lower" : "rejected";
        var devices = ProjectDevices(source, sourcePrepared, room, candidate, best);
        var rules = source.Rules ?? [];
        if (rules.Count > 0)
        {
            PlanPoint? entrance = source.Entrance is PlanPoint pageEntrance
                ? Absolute(ToLocal(pageEntrance, sourcePrepared, room, best.Quarters, best.Mirror), candidate)
                : null;
            var adapted = SemanticPatternAdapter.TryPlace(
                rules,
                source.Devices,
                candidate.Loop,
                candidate.Holes ?? [],
                candidate.CentroidX,
                candidate.CentroidY,
                candidate.Openings,
                devices,
                entrance,
                out var placements);
            devices = placements.Select(placement =>
            {
                var local = new PlanPoint(placement.X - candidate.CentroidX, placement.Y - candidate.CentroidY);
                var normalized = NormalizePoint(local, room);
                return new ProjectedDevice(placement.Role, placement.PlacementKind, placement.X, placement.Y, normalized.X, normalized.Y, placement.FamilyHint);
            }).ToArray();
            if (!adapted)
            {
                reason = "unresolved_rule";
                band = "rejected";
            }
            else if (band == "rejected")
            {
                band = "lower";
                reason = "semantic_match";
            }
        }

        return new RoomShapeMatch(
            label,
            candidate.LevelName,
            band,
            score,
            best.Quarters * 90,
            best.Mirror,
            reason,
            Normalize(room.Clean, room),
            devices,
            candidate.ElementRef,
            candidate.SourceKind,
            candidate.LinkLabel);
    }

    private static PlanPoint Absolute(PlanPoint local, CandidateRoom room)
    {
        return new PlanPoint(room.CentroidX + local.X, room.CentroidY + local.Y);
    }

    private static Chosen ChooseTransform(
        SourceRoomPattern source,
        PreparedShape sourceShape,
        PreparedShape candidate,
        CandidateRoom room)
    {
        Chosen? best = null;
        var reversed = Reverse(candidate.Samples);
        for (var mirror = 0; mirror < 2; mirror++)
        {
            for (var quarters = 0; quarters < 4; quarters++)
            {
                var transformed = Transform(sourceShape.Samples, quarters, mirror == 1);
                // Revit outer loops keep one winding. A mirrored room is the
                // reflection walked in that same direction, so compare both.
                var rms = Math.Min(
                    BestCyclicRms(transformed, candidate.Samples),
                    BestCyclicRms(transformed, reversed));
                var entrance = EntranceDistance(source, sourceShape, candidate, room, quarters, mirror == 1);
                var chosen = new Chosen(quarters, mirror == 1, rms, entrance);
                if (best is null || Better(chosen, best.Value))
                {
                    best = chosen;
                }
            }
        }

        return best!.Value;
    }

    private static bool Better(Chosen candidate, Chosen current)
    {
        if (candidate.Rms + EntranceTie < current.Rms)
        {
            return true;
        }

        if (Math.Abs(candidate.Rms - current.Rms) <= EntranceTie)
        {
            var entranceDelta = candidate.Entrance - current.Entrance;
            if (entranceDelta < -1e-6)
            {
                return true;
            }

            if (Math.Abs(entranceDelta) <= 1e-6)
            {
                if (current.Mirror && !candidate.Mirror)
                {
                    return true;
                }

                if (candidate.Mirror == current.Mirror && candidate.Quarters < current.Quarters)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static double EntranceDistance(
        SourceRoomPattern source,
        PreparedShape sourceShape,
        PreparedShape candidate,
        CandidateRoom room,
        int quarters,
        bool mirror)
    {
        if (source.Entrance is not PlanPoint entrance)
        {
            return 0;
        }

        var doors = room.Openings.Where(opening => opening.Kind == "door").ToArray();
        if (doors.Length == 0)
        {
            return 0;
        }

        var local = ToLocal(entrance, sourceShape, candidate, quarters, mirror);
        var best = double.MaxValue;
        foreach (var door in doors)
        {
            var dx = local.X - (door.X - room.CentroidX);
            var dy = local.Y - (door.Y - room.CentroidY);
            best = Math.Min(best, Math.Sqrt((dx * dx) + (dy * dy)));
        }

        return best / Math.Max(candidate.Scale, 1e-6);
    }

    private static IReadOnlyList<ProjectedDevice> ProjectDevices(
        SourceRoomPattern source,
        PreparedShape sourceShape,
        PreparedShape candidate,
        CandidateRoom room,
        Chosen chosen)
    {
        var projected = new List<ProjectedDevice>(source.Devices.Count);
        foreach (var device in source.Devices)
        {
            var local = ToLocal(new PlanPoint(device.X, device.Y), sourceShape, candidate, chosen.Quarters, chosen.Mirror);
            if (device.PlacementKind == "boundary")
            {
                local = NearestBoundary(local, Centered(candidate));
            }

            var normalized = NormalizePoint(local, candidate);
            projected.Add(new ProjectedDevice(
                device.Role,
                device.PlacementKind,
                Math.Round(room.CentroidX + local.X, 4, MidpointRounding.AwayFromZero),
                Math.Round(room.CentroidY + local.Y, 4, MidpointRounding.AwayFromZero),
                normalized.X,
                normalized.Y,
                device.FamilyHint));
        }

        return projected;
    }

    private static PlanPoint ToLocal(
        PlanPoint page,
        PreparedShape source,
        PreparedShape candidate,
        int quarters,
        bool mirror)
    {
        var offset = new PlanPoint((page.X - source.Centroid.X) / source.Scale, (page.Y - source.Centroid.Y) / source.Scale);
        var turned = Turn(offset, quarters, mirror);
        return new PlanPoint(turned.X * candidate.Scale, turned.Y * candidate.Scale);
    }

    private static IReadOnlyList<PlanPoint> Centered(PreparedShape shape)
    {
        return shape.Clean.Select(point => new PlanPoint(point.X - shape.Centroid.X, point.Y - shape.Centroid.Y)).ToArray();
    }

    private static PlanPoint NearestBoundary(PlanPoint point, IReadOnlyList<PlanPoint> loop)
    {
        var best = loop[0];
        var bestDistance = double.MaxValue;
        for (var index = 0; index < loop.Count; index++)
        {
            var start = loop[index];
            var end = loop[(index + 1) % loop.Count];
            var projected = ProjectSegment(point, start, end);
            var dx = point.X - projected.X;
            var dy = point.Y - projected.Y;
            var distance = (dx * dx) + (dy * dy);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = projected;
            }
        }

        return best;
    }

    private static PlanPoint ProjectSegment(PlanPoint point, PlanPoint start, PlanPoint end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var length = (dx * dx) + (dy * dy);
        if (length < 1e-12)
        {
            return start;
        }

        var t = Math.Clamp((((point.X - start.X) * dx) + ((point.Y - start.Y) * dy)) / length, 0, 1);
        return new PlanPoint(start.X + (t * dx), start.Y + (t * dy));
    }

    private static IReadOnlyList<PlanPoint> Normalize(IReadOnlyList<PlanPoint> loop, PreparedShape shape)
    {
        return loop.Select(point => NormalizePoint(new PlanPoint(point.X - shape.Centroid.X, point.Y - shape.Centroid.Y), shape)).ToArray();
    }

    private static PlanPoint NormalizePoint(PlanPoint local, PreparedShape shape)
    {
        var width = Math.Max(shape.MaxX - shape.MinX, 1e-6);
        var height = Math.Max(shape.MaxY - shape.MinY, 1e-6);
        var x = Math.Clamp((local.X - shape.MinX) / width, 0, 1);
        var y = Math.Clamp((local.Y - shape.MinY) / height, 0, 1);
        return new PlanPoint(Math.Round(x, 4, MidpointRounding.AwayFromZero), Math.Round(y, 4, MidpointRounding.AwayFromZero));
    }

    private static PreparedShape? Prepare(IReadOnlyList<PlanPoint> source)
    {
        var clean = Clean(source);
        if (clean.Count < 3 || !TryArea(clean, out var centroid, out var area) || Math.Abs(area) < 1e-8)
        {
            return null;
        }

        var scale = Math.Sqrt(Math.Abs(area));
        var samples = Resample(clean, centroid, scale, SampleCount);
        var aspect = Aspect(clean);
        Bounds(clean, centroid, out var minX, out var minY, out var maxX, out var maxY);
        return new PreparedShape(clean, centroid, scale, samples, aspect, minX, minY, maxX, maxY);
    }

    private static List<PlanPoint> Clean(IReadOnlyList<PlanPoint> source)
    {
        var points = new List<PlanPoint>();
        foreach (var point in source)
        {
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
            {
                continue;
            }

            if (points.Count == 0 || Distance(points[^1], point) > 1e-6)
            {
                points.Add(point);
            }
        }

        if (points.Count > 1 && Distance(points[0], points[^1]) <= 1e-6)
        {
            points.RemoveAt(points.Count - 1);
        }

        var simplified = new List<PlanPoint>();
        for (var index = 0; index < points.Count; index++)
        {
            var previous = points[(index - 1 + points.Count) % points.Count];
            var current = points[index];
            var next = points[(index + 1) % points.Count];
            if (points.Count > 3 && Colinear(previous, current, next))
            {
                continue;
            }

            simplified.Add(current);
        }

        return simplified.Count >= 3 ? simplified : points;
    }

    private static bool Colinear(PlanPoint previous, PlanPoint current, PlanPoint next)
    {
        var abx = current.X - previous.X;
        var aby = current.Y - previous.Y;
        var bcx = next.X - current.X;
        var bcy = next.Y - current.Y;
        var cross = Math.Abs((abx * bcy) - (aby * bcx));
        var scale = Math.Sqrt(((abx * abx) + (aby * aby)) * ((bcx * bcx) + (bcy * bcy)));
        return scale > 1e-9 && cross / scale < Math.Sin(8.0 * Math.PI / 180.0);
    }

    private static bool IsMostlyOrthogonal(IReadOnlyList<PlanPoint> loop)
    {
        double orthogonal = 0;
        double total = 0;
        for (var index = 0; index < loop.Count; index++)
        {
            var start = loop[index];
            var end = loop[(index + 1) % loop.Count];
            var length = Distance(start, end);
            if (length < 1e-8)
            {
                continue;
            }

            total += length;
            var angle = Math.Atan2(end.Y - start.Y, end.X - start.X);
            var modulo = Math.Abs(angle) % (Math.PI / 2.0);
            var delta = Math.Min(modulo, (Math.PI / 2.0) - modulo);
            if (delta <= OrthogonalToleranceRadians)
            {
                orthogonal += length;
            }
        }

        return total > 0 && orthogonal / total >= 0.85;
    }

    private static bool SelfIntersects(IReadOnlyList<PlanPoint> loop)
    {
        for (var i = 0; i < loop.Count; i++)
        {
            var a = loop[i];
            var b = loop[(i + 1) % loop.Count];
            for (var j = i + 1; j < loop.Count; j++)
            {
                var adjacent = j == i || (j + 1) % loop.Count == i || (i + 1) % loop.Count == j;
                if (adjacent)
                {
                    continue;
                }

                var c = loop[j];
                var d = loop[(j + 1) % loop.Count];
                if (SegmentsIntersect(a, b, c, d))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool SegmentsIntersect(PlanPoint a, PlanPoint b, PlanPoint c, PlanPoint d)
    {
        var abx = b.X - a.X;
        var aby = b.Y - a.Y;
        var acx = c.X - a.X;
        var acy = c.Y - a.Y;
        var adx = d.X - a.X;
        var ady = d.Y - a.Y;
        var cax = a.X - c.X;
        var cay = a.Y - c.Y;
        var cbx = b.X - c.X;
        var cby = b.Y - c.Y;
        var cdx = d.X - c.X;
        var cdy = d.Y - c.Y;
        var abCrossAc = (abx * acy) - (aby * acx);
        var abCrossAd = (abx * ady) - (aby * adx);
        var cdCrossCa = (cdx * cay) - (cdy * cax);
        var cdCrossCb = (cdx * cby) - (cdy * cbx);
        return abCrossAc * abCrossAd < 0 && cdCrossCa * cdCrossCb < 0;
    }

    private static double Aspect(IReadOnlyList<PlanPoint> loop)
    {
        var longest = 0.0;
        var angle = 0.0;
        for (var index = 0; index < loop.Count; index++)
        {
            var start = loop[index];
            var end = loop[(index + 1) % loop.Count];
            var length = Distance(start, end);
            if (length > longest)
            {
                longest = length;
                angle = Math.Atan2(end.Y - start.Y, end.X - start.X);
            }
        }

        double minX = double.MaxValue;
        double minY = double.MaxValue;
        double maxX = double.MinValue;
        double maxY = double.MinValue;
        var cosine = Math.Cos(-angle);
        var sine = Math.Sin(-angle);
        foreach (var point in loop)
        {
            var x = (point.X * cosine) - (point.Y * sine);
            var y = (point.X * sine) + (point.Y * cosine);
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
        }

        var width = Math.Max(maxX - minX, 1e-6);
        var height = Math.Max(maxY - minY, 1e-6);
        return Math.Max(width, height) / Math.Min(width, height);
    }

    private static PlanPoint[] Resample(IReadOnlyList<PlanPoint> loop, PlanPoint centroid, double scale, int count)
    {
        var lengths = new double[loop.Count];
        double total = 0;
        for (var index = 0; index < loop.Count; index++)
        {
            lengths[index] = Distance(loop[index], loop[(index + 1) % loop.Count]);
            total += lengths[index];
        }

        var samples = new PlanPoint[count];
        var cursor = 0;
        var walked = 0.0;
        for (var sample = 0; sample < count; sample++)
        {
            var target = total * sample / count;
            while (cursor < loop.Count - 1 && walked + lengths[cursor] < target)
            {
                walked += lengths[cursor];
                cursor++;
            }

            var edge = Math.Max(lengths[cursor], 1e-9);
            var t = Math.Clamp((target - walked) / edge, 0, 1);
            var start = loop[cursor];
            var end = loop[(cursor + 1) % loop.Count];
            samples[sample] = new PlanPoint(
                ((start.X + ((end.X - start.X) * t)) - centroid.X) / scale,
                ((start.Y + ((end.Y - start.Y) * t)) - centroid.Y) / scale);
        }

        return samples;
    }

    private static PlanPoint[] Transform(PlanPoint[] samples, int quarters, bool mirror)
    {
        var transformed = new PlanPoint[samples.Length];
        for (var index = 0; index < samples.Length; index++)
        {
            transformed[index] = Turn(samples[index], quarters, mirror);
        }

        return transformed;
    }

    private static PlanPoint Turn(PlanPoint point, int quarters, bool mirror)
    {
        var x = mirror ? -point.X : point.X;
        var y = point.Y;
        for (var turn = 0; turn < quarters; turn++)
        {
            var nextX = -y;
            var nextY = x;
            x = nextX;
            y = nextY;
        }

        return new PlanPoint(x, y);
    }

    private static PlanPoint[] Reverse(PlanPoint[] samples)
    {
        var reversed = (PlanPoint[])samples.Clone();
        Array.Reverse(reversed);
        return reversed;
    }

    private static double BestCyclicRms(PlanPoint[] left, PlanPoint[] right)
    {
        var best = double.MaxValue;
        for (var shift = 0; shift < left.Length; shift++)
        {
            double sum = 0;
            for (var index = 0; index < left.Length; index++)
            {
                var point = left[(index + shift) % left.Length];
                var other = right[index];
                var dx = point.X - other.X;
                var dy = point.Y - other.Y;
                sum += (dx * dx) + (dy * dy);
            }

            best = Math.Min(best, sum / left.Length);
        }

        return Math.Sqrt(best);
    }

    private static bool TryArea(IReadOnlyList<PlanPoint> loop, out PlanPoint centroid, out double area)
    {
        centroid = default;
        area = 0;
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

        if (Math.Abs(twice) < 1e-12)
        {
            return false;
        }

        area = twice / 2.0;
        centroid = new PlanPoint(cx / (3.0 * twice), cy / (3.0 * twice));
        return double.IsFinite(centroid.X) && double.IsFinite(centroid.Y);
    }

    private static void Bounds(
        IReadOnlyList<PlanPoint> loop,
        PlanPoint centroid,
        out double minX,
        out double minY,
        out double maxX,
        out double maxY)
    {
        minX = double.MaxValue;
        minY = double.MaxValue;
        maxX = double.MinValue;
        maxY = double.MinValue;
        foreach (var point in loop)
        {
            var x = point.X - centroid.X;
            var y = point.Y - centroid.Y;
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
        }
    }

    private static double Distance(PlanPoint left, PlanPoint right)
    {
        var dx = left.X - right.X;
        var dy = left.Y - right.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static string Label(CandidateRoom room, int index)
    {
        var name = string.IsNullOrWhiteSpace(room.Name) ? null : room.Name.Trim();
        var number = string.IsNullOrWhiteSpace(room.Number) ? null : room.Number.Trim();
        var label = name is null ? number : number is null ? name : name + " " + number;
        return string.IsNullOrWhiteSpace(label) ? "Room " + (index + 1) : label;
    }

    private static RoomShapeMatch Rejected(string label, string? level, string reason, CandidateRoom? candidate = null)
    {
        return new RoomShapeMatch(label, level, "rejected", 0, 0, false, reason, [], [], candidate?.ElementRef, candidate?.SourceKind, candidate?.LinkLabel);
    }

    private readonly record struct PreparedShape(
        IReadOnlyList<PlanPoint> Clean,
        PlanPoint Centroid,
        double Scale,
        PlanPoint[] Samples,
        double Aspect,
        double MinX,
        double MinY,
        double MaxX,
        double MaxY);

    private readonly record struct Chosen(int Quarters, bool Mirror, double Rms, double Entrance);
}
