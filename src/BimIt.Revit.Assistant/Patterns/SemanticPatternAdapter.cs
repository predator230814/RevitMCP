namespace BimIt.Revit.Assistant.Patterns;

internal static class SemanticPatternAdapter
{
    public const double DefaultInset = 0.15;

    public const double DefaultPairHalf = 0.6;

    public const double MinPairHalf = 0.3;

    public const double MaxPairHalf = 1.5;

    public const double MaxInteriorPairHalf = 2.5;

    public const double InteriorClearance = 0.3;

    private const double WallMargin = 0.15;

    public readonly record struct Placement(string Role, string PlacementKind, double X, double Y, string? FamilyHint);

    public readonly record struct EdgeSegment(PlanPoint Start, PlanPoint End);

    public static class Failures
    {
        public const string EntranceAmbiguous = "entrance_ambiguous";

        public const string NoFacingWall = "no_facing_wall";

        public const string NoCorrespondingWall = "no_corresponding_wall";

        public const string IncompatibleBinding = "incompatible_binding";

        public const string NoObservedDevice = "no_observed_device";

        public const string OutsideRoom = "outside_room";

        public const string WallTooShort = "wall_too_short";

        public const string UnsupportedArrangement = "unsupported_arrangement";
    }

    public sealed record SpecOutcome(int Index, PatternSpec Spec, IReadOnlyList<Placement> Placements, string? Failure, EdgeSegment? Edge = null)
    {
        public bool Resolved => Failure is null;
    }

    public sealed record Adaptation(IReadOnlyList<SpecOutcome> Outcomes)
    {
        public IReadOnlyList<Placement> Placements => Outcomes.SelectMany(outcome => outcome.Placements).ToArray();

        public string Status => Outcomes.All(outcome => outcome.Resolved)
            ? PatternAdaptationStatus.Resolved
            : Outcomes.Any(outcome => outcome.Resolved) ? PatternAdaptationStatus.Partial : PatternAdaptationStatus.Unresolved;
    }

    public sealed class Target
    {
        public required IReadOnlyList<PlanPoint> Loop { get; init; }

        public IReadOnlyList<IReadOnlyList<PlanPoint>> Holes { get; init; } = [];

        public required double CentroidX { get; init; }

        public required double CentroidY { get; init; }

        public IReadOnlyList<CandidateOpening> Openings { get; init; } = [];

        public IReadOnlyList<ProjectedDevice> Projected { get; init; } = [];

        public PlanPoint? ProjectedEntrance { get; init; }

        public PlanPoint? SelectedDoor { get; init; }

        public IReadOnlyDictionary<int, EdgeSegment>? Bindings { get; init; }

        public IReadOnlySet<int>? RejectedBindings { get; init; }

        public int Quarters { get; init; }

        public bool Mirror { get; init; }

        public bool OrientationTrusted { get; init; }
    }

    public static Adaptation Adapt(SourceRoomPattern source, Target target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        var specs = source.Specs ?? [];
        var evidence = source.Evidence ?? PatternEvidence.Describe(source);
        var shared = SharedEdges(specs, target);
        var outcomes = new List<SpecOutcome>(specs.Count);
        for (var index = 0; index < specs.Count; index++)
        {
            var spec = specs[index];
            var facts = index < evidence.Count ? evidence[index] : PatternSpecEvidence.Empty;
            outcomes.Add(Resolve(index, spec, facts, source, target, shared));
        }

        return new Adaptation(outcomes);
    }

    private static SpecOutcome Resolve(
        int index,
        PatternSpec spec,
        PatternSpecEvidence evidence,
        SourceRoomPattern source,
        Target target,
        IReadOnlyDictionary<int, (int Slot, int Total)> shared)
    {
        SpecOutcome Fail(string failure, EdgeSegment? edge = null) => new(index, spec, [], failure, edge);

        if (!PatternArrangements.IsSupported(spec.Arrangement)
            || spec.Count != PatternArrangements.CountFor(spec.Arrangement)
            || PatternAnchors.PlacementFor(spec.Anchor) != spec.Placement)
        {
            return Fail(Failures.UnsupportedArrangement);
        }

        if (target.RejectedBindings?.Contains(index) == true)
        {
            return Fail(Failures.IncompatibleBinding);
        }

        var hint = Hint(source, spec, evidence);
        var inset = spec.OffsetMetres > 0 ? spec.OffsetMetres : DefaultInset;
        var centre = Centre(target);
        var points = new List<PlanPoint>(2);
        EdgeSegment? used = null;
        string? failure = null;
        switch (spec.Anchor)
        {
            case PatternAnchors.RoomCenter:
                if (spec.IsPair)
                {
                    if (!InteriorPair(target, evidence, out var first, out var second))
                    {
                        failure = Failures.OutsideRoom;
                        break;
                    }

                    points.Add(first);
                    points.Add(second);
                }
                else
                {
                    points.Add(centre);
                }

                break;

            case PatternAnchors.Entrance:
            {
                if (!TryDoor(target, out var door))
                {
                    failure = Failures.EntranceAmbiguous;
                    break;
                }

                if (!spec.IsPair)
                {
                    if (!InsetFrom(door, centre, inset, out var point))
                    {
                        failure = Failures.OutsideRoom;
                        break;
                    }

                    points.Add(point);
                    break;
                }

                if (!TryNearestEdge(target.Loop, door, out var start, out var end))
                {
                    failure = Failures.OutsideRoom;
                    break;
                }

                used = new EdgeSegment(start, end);
                failure = AlongEdge(target.Loop, start, end, AlongOf(Project(door, start, end), start, end), PairHalf(evidence, start, end), inset, points);
                break;
            }

            case PatternAnchors.OppositeBoundary:
            {
                if (!TryDoor(target, out var door))
                {
                    failure = Failures.EntranceAmbiguous;
                    break;
                }

                if (!TryFacing(target.Loop, door, out var facing))
                {
                    failure = Failures.NoFacingWall;
                    break;
                }

                used = new EdgeSegment(facing.Start, facing.End);
                if (spec.IsPair)
                {
                    failure = AlongEdge(target.Loop, facing.Start, facing.End, AlongOf(facing.Hit, facing.Start, facing.End), PairHalf(evidence, facing.Start, facing.End), inset, points);
                }
                else
                {
                    points.Add(new PlanPoint(facing.Hit.X + (facing.Inward.X * inset), facing.Hit.Y + (facing.Inward.Y * inset)));
                }

                break;
            }

            case PatternAnchors.NearestBoundary:
            {
                if (!TryObserved(target, evidence, out var observed))
                {
                    failure = Failures.NoObservedDevice;
                    break;
                }

                if (!TryNearestEdge(target.Loop, observed, out var start, out var end))
                {
                    failure = Failures.OutsideRoom;
                    break;
                }

                var projected = Project(observed, start, end);
                used = new EdgeSegment(start, end);
                if (spec.IsPair)
                {
                    failure = AlongEdge(target.Loop, start, end, AlongOf(projected, start, end), PairHalf(evidence, start, end), inset, points);
                }
                else if (InsetFrom(projected, centre, inset, out var point))
                {
                    points.Add(point);
                }
                else
                {
                    failure = Failures.OutsideRoom;
                }

                break;
            }

            case PatternAnchors.Corner:
            {
                if (spec.IsPair)
                {
                    failure = Failures.UnsupportedArrangement;
                    break;
                }

                if (!TryObserved(target, evidence, out var observed))
                {
                    failure = Failures.NoObservedDevice;
                    break;
                }

                if (!TryCorner(target.Loop, observed, out var corner) || !InsetFrom(corner, centre, inset, out var point))
                {
                    failure = Failures.OutsideRoom;
                    break;
                }

                points.Add(point);
                break;
            }

            case PatternAnchors.SourceCorrespondence:
            {
                if (target.Bindings is null || !target.Bindings.TryGetValue(index, out var edge))
                {
                    failure = Failures.NoCorrespondingWall;
                    break;
                }

                used = edge;
                var length = Distance(edge.Start, edge.End);
                var (slot, total) = shared.TryGetValue(index, out var share) ? share : (0, 1);
                var along = length * (slot + 1) / (total + 1);
                if (spec.IsPair)
                {
                    var half = PairHalf(evidence, edge.Start, edge.End);
                    if (total > 1)
                    {
                        half = Math.Min(half, Math.Max(MinPairHalf, (length / (total + 1) / 2) - WallMargin));
                    }

                    failure = AlongEdge(target.Loop, edge.Start, edge.End, along, half, inset, points);
                }
                else
                {
                    failure = AlongEdge(target.Loop, edge.Start, edge.End, along, 0, inset, points);
                }

                break;
            }

            default:
                failure = Failures.UnsupportedArrangement;
                break;
        }

        if (failure is not null)
        {
            return Fail(failure, used);
        }

        var placements = new List<Placement>(points.Count);
        foreach (var point in points)
        {
            if (!Accept(point, target.Loop, target.Holes, out var accepted))
            {
                return Fail(Failures.OutsideRoom, used);
            }

            placements.Add(new Placement(spec.Role, spec.Placement, accepted.X, accepted.Y, hint));
        }

        return new SpecOutcome(index, spec, placements, null, used);
    }

    private static Dictionary<int, (int Slot, int Total)> SharedEdges(IReadOnlyList<PatternSpec> specs, Target target)
    {
        var result = new Dictionary<int, (int Slot, int Total)>();
        if (target.Bindings is null)
        {
            return result;
        }

        foreach (var group in target.Bindings
            .Where(pair => pair.Key < specs.Count && specs[pair.Key].Anchor == PatternAnchors.SourceCorrespondence && target.RejectedBindings?.Contains(pair.Key) != true)
            .GroupBy(pair => pair.Value))
        {
            var members = group.Select(pair => pair.Key).OrderBy(index => index).ToArray();
            for (var slot = 0; slot < members.Length; slot++)
            {
                result[members[slot]] = (slot, members.Length);
            }
        }

        return result;
    }

    private static bool InteriorPair(Target target, PatternSpecEvidence evidence, out PlanPoint first, out PlanPoint second)
    {
        first = default;
        second = default;
        var centre = Centre(target);
        var axis = target.OrientationTrusted && evidence.Axis is PlanPoint sourceAxis
            ? Normalize(RoomShapeMatcher.Orient(sourceAxis, target.Quarters, target.Mirror))
            : PrincipalAxis(target.Loop);
        var ratio = evidence.AxisSpread is double spread && spread > 0 ? Math.Clamp(spread, 0.2, 0.7) : 0.5;
        foreach (var direction in new[] { axis, new PlanPoint(-axis.Y, axis.X) })
        {
            var extent = PatternEvidence.Extent(target.Loop, direction);
            var half = Math.Clamp(ratio * extent / 2, MinPairHalf, MaxInteriorPairHalf);
            foreach (var factor in new[] { 1.0, 0.75, 0.5 })
            {
                var step = half * factor;
                if (step < MinPairHalf)
                {
                    break;
                }

                var a = new PlanPoint(centre.X - (direction.X * step), centre.Y - (direction.Y * step));
                var b = new PlanPoint(centre.X + (direction.X * step), centre.Y + (direction.Y * step));
                if (Accept(a, target.Loop, target.Holes, out _)
                    && Accept(b, target.Loop, target.Holes, out _)
                    && Clear(a, target) >= InteriorClearance
                    && Clear(b, target) >= InteriorClearance)
                {
                    first = a;
                    second = b;
                    return true;
                }
            }
        }

        return false;
    }

    private static PlanPoint PrincipalAxis(IReadOnlyList<PlanPoint> loop)
    {
        var best = new PlanPoint(1, 0);
        var longest = -1.0;
        for (var index = 0; index < loop.Count; index++)
        {
            var start = loop[index];
            var end = loop[(index + 1) % loop.Count];
            var length = Distance(start, end);
            if (length > longest + 1e-9)
            {
                longest = length;
                best = Direction(start, end);
            }
        }

        return best.X < -1e-9 || (Math.Abs(best.X) <= 1e-9 && best.Y < 0) ? new PlanPoint(-best.X, -best.Y) : best;
    }

    private static double PairHalf(PatternSpecEvidence evidence, PlanPoint start, PlanPoint end)
    {
        return evidence.EdgeSpread is double ratio && ratio > 0
            ? Math.Clamp(ratio * Distance(start, end) / 2, MinPairHalf, MaxPairHalf)
            : DefaultPairHalf;
    }

    private static string? AlongEdge(
        IReadOnlyList<PlanPoint> loop,
        PlanPoint start,
        PlanPoint end,
        double along,
        double half,
        double inset,
        List<PlanPoint> points)
    {
        var length = Distance(start, end);
        if (length < 1e-6)
        {
            return Failures.WallTooShort;
        }

        if (half > 0 && length < 2 * (half + WallMargin))
        {
            half = (length / 2) - WallMargin;
            if (half < 0.15)
            {
                return Failures.WallTooShort;
            }
        }

        along = Math.Clamp(along, Math.Min(half + WallMargin, length / 2), Math.Max(length - half - WallMargin, length / 2));
        var direction = Direction(start, end);
        var middle = new PlanPoint(start.X + (direction.X * along), start.Y + (direction.Y * along));
        if (!TryInward(loop, start, end, middle, out var inward))
        {
            return Failures.OutsideRoom;
        }

        if (half <= 0)
        {
            points.Add(new PlanPoint(middle.X + (inward.X * inset), middle.Y + (inward.Y * inset)));
            return null;
        }

        points.Add(new PlanPoint(middle.X - (direction.X * half) + (inward.X * inset), middle.Y - (direction.Y * half) + (inward.Y * inset)));
        points.Add(new PlanPoint(middle.X + (direction.X * half) + (inward.X * inset), middle.Y + (direction.Y * half) + (inward.Y * inset)));
        return null;
    }

    private static double AlongOf(PlanPoint point, PlanPoint start, PlanPoint end)
    {
        var direction = Direction(start, end);
        return ((point.X - start.X) * direction.X) + ((point.Y - start.Y) * direction.Y);
    }

    private readonly record struct Facing(PlanPoint Hit, PlanPoint Start, PlanPoint End, PlanPoint Inward);

    private static bool TryFacing(IReadOnlyList<PlanPoint> loop, PlanPoint door, out Facing facing)
    {
        facing = default;
        if (!TryNearestEdge(loop, door, out var doorStart, out var doorEnd)
            || !TryInward(loop, doorStart, doorEnd, Project(door, doorStart, doorEnd), out var normal))
        {
            return false;
        }

        var origin = Project(door, doorStart, doorEnd);
        var best = double.MaxValue;
        var found = false;
        for (var index = 0; index < loop.Count; index++)
        {
            var edgeStart = loop[index];
            var edgeEnd = loop[(index + 1) % loop.Count];
            if (!TryRay(origin, normal, edgeStart, edgeEnd, out var distance) || distance < 0.3 || distance >= best)
            {
                continue;
            }

            best = distance;
            facing = new Facing(
                new PlanPoint(origin.X + (normal.X * distance), origin.Y + (normal.Y * distance)),
                edgeStart,
                edgeEnd,
                default);
            found = true;
        }

        if (!found || !TryInward(loop, facing.Start, facing.End, facing.Hit, out var inward))
        {
            return false;
        }

        facing = facing with { Inward = inward };
        return true;
    }

    private static bool TryRay(PlanPoint origin, PlanPoint direction, PlanPoint start, PlanPoint end, out double distance)
    {
        distance = 0;
        var segmentX = end.X - start.X;
        var segmentY = end.Y - start.Y;
        var denominator = (direction.X * segmentY) - (direction.Y * segmentX);
        if (Math.Abs(denominator) < 1e-9)
        {
            return false;
        }

        var offsetX = start.X - origin.X;
        var offsetY = start.Y - origin.Y;
        var t = ((offsetX * segmentY) - (offsetY * segmentX)) / denominator;
        var u = ((offsetX * direction.Y) - (offsetY * direction.X)) / denominator;
        if (t <= 0 || u < 0 || u > 1)
        {
            return false;
        }

        distance = t;
        return true;
    }

    private static bool TryInward(IReadOnlyList<PlanPoint> loop, PlanPoint start, PlanPoint end, PlanPoint at, out PlanPoint inward)
    {
        var direction = Direction(start, end);
        var left = new PlanPoint(-direction.Y, direction.X);
        foreach (var candidate in new[] { left, new PlanPoint(-left.X, -left.Y) })
        {
            var probe = new PlanPoint(at.X + (candidate.X * 0.05), at.Y + (candidate.Y * 0.05));
            if (Inside(probe, loop))
            {
                inward = candidate;
                return true;
            }
        }

        inward = default;
        return false;
    }

    private static string? Hint(SourceRoomPattern source, PatternSpec spec, PatternSpecEvidence evidence)
    {
        foreach (var index in evidence.DeviceIndexes)
        {
            if (index >= 0 && index < source.Devices.Count && !string.IsNullOrWhiteSpace(source.Devices[index].FamilyHint))
            {
                return source.Devices[index].FamilyHint;
            }
        }

        return source.Devices.FirstOrDefault(device => device.Role == spec.Role && !string.IsNullOrWhiteSpace(device.FamilyHint))?.FamilyHint;
    }

    private static bool TryObserved(Target target, PatternSpecEvidence evidence, out PlanPoint point)
    {
        var indexes = evidence.DeviceIndexes.Where(index => index >= 0 && index < target.Projected.Count).ToArray();
        if (indexes.Length > 0)
        {
            point = new PlanPoint(
                indexes.Average(index => target.Projected[index].LocalX),
                indexes.Average(index => target.Projected[index].LocalY));
            return true;
        }

        point = default;
        return false;
    }

    private static bool TryDoor(Target target, out PlanPoint door)
    {
        if (target.SelectedDoor is PlanPoint chosen)
        {
            door = chosen;
            return true;
        }

        var doors = target.Openings.Where(opening => opening.Kind == "door").ToArray();
        door = default;
        if (doors.Length == 1)
        {
            door = new PlanPoint(doors[0].X, doors[0].Y);
            return true;
        }

        if (doors.Length == 0 || target.ProjectedEntrance is not PlanPoint preferred)
        {
            return false;
        }

        var best = double.MaxValue;
        foreach (var opening in doors)
        {
            var dx = opening.X - preferred.X;
            var dy = opening.Y - preferred.Y;
            var distance = (dx * dx) + (dy * dy);
            if (distance < best)
            {
                best = distance;
                door = new PlanPoint(opening.X, opening.Y);
            }
        }

        return true;
    }

    private static bool TryCorner(IReadOnlyList<PlanPoint> loop, PlanPoint point, out PlanPoint corner)
    {
        corner = loop.Count > 0 ? loop[0] : default;
        var best = double.MaxValue;
        foreach (var vertex in loop)
        {
            var distance = Distance(point, vertex);
            if (distance < best)
            {
                best = distance;
                corner = vertex;
            }
        }

        return loop.Count > 0;
    }

    private static bool TryNearestEdge(IReadOnlyList<PlanPoint> loop, PlanPoint point, out PlanPoint start, out PlanPoint end)
    {
        start = default;
        end = default;
        var best = double.MaxValue;
        var found = false;
        for (var index = 0; index < loop.Count; index++)
        {
            var edgeStart = loop[index];
            var edgeEnd = loop[(index + 1) % loop.Count];
            var distance = Distance(point, Project(point, edgeStart, edgeEnd));
            if (distance < best)
            {
                best = distance;
                start = edgeStart;
                end = edgeEnd;
                found = true;
            }
        }

        return found;
    }

    private static PlanPoint Project(PlanPoint point, PlanPoint start, PlanPoint end)
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

    private static bool InsetFrom(PlanPoint edge, PlanPoint centre, double distance, out PlanPoint point)
    {
        var dx = centre.X - edge.X;
        var dy = centre.Y - edge.Y;
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length < 1e-6)
        {
            point = edge;
            return false;
        }

        var step = Math.Min(distance, length * 0.5);
        point = new PlanPoint(edge.X + (dx / length * step), edge.Y + (dy / length * step));
        return true;
    }

    private static bool Accept(PlanPoint point, IReadOnlyList<PlanPoint> outer, IReadOnlyList<IReadOnlyList<PlanPoint>> holes, out PlanPoint accepted)
    {
        var rounded = new PlanPoint(Math.Round(point.X, 4, MidpointRounding.AwayFromZero), Math.Round(point.Y, 4, MidpointRounding.AwayFromZero));
        accepted = rounded;
        return Inside(rounded, outer) && holes.All(hole => !Inside(rounded, hole));
    }

    private static bool Inside(PlanPoint point, IReadOnlyList<PlanPoint> loop)
    {
        var inside = false;
        for (var index = 0; index < loop.Count; index++)
        {
            var start = loop[index];
            var end = loop[(index + 1) % loop.Count];
            var crosses = (start.Y > point.Y) != (end.Y > point.Y);
            if (!crosses)
            {
                continue;
            }

            var x = start.X + ((point.Y - start.Y) * (end.X - start.X) / (end.Y - start.Y));
            if (point.X < x)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static PlanPoint Normalize(PlanPoint vector)
    {
        var length = Math.Sqrt((vector.X * vector.X) + (vector.Y * vector.Y));
        return length < 1e-9 ? new PlanPoint(1, 0) : new PlanPoint(vector.X / length, vector.Y / length);
    }

    internal static PlanPoint Centre(Target target)
    {
        var fallback = new PlanPoint(target.CentroidX, target.CentroidY);
        var loop = target.Loop;
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
            return fallback;
        }

        var centroid = new PlanPoint(cx / (3 * twice), cy / (3 * twice));
        return Accept(centroid, loop, target.Holes, out var accepted) ? accepted : fallback;
    }

    private static double Clear(PlanPoint point, Target target)
    {
        var best = double.MaxValue;
        foreach (var loop in target.Holes.Prepend(target.Loop))
        {
            for (var index = 0; index < loop.Count; index++)
            {
                var start = loop[index];
                var end = loop[(index + 1) % loop.Count];
                var dx = end.X - start.X;
                var dy = end.Y - start.Y;
                var squared = (dx * dx) + (dy * dy);
                var t = squared < 1e-18 ? 0 : Math.Clamp((((point.X - start.X) * dx) + ((point.Y - start.Y) * dy)) / squared, 0, 1);
                best = Math.Min(best, Distance(point, new PlanPoint(start.X + (t * dx), start.Y + (t * dy))));
            }
        }

        return best;
    }

    private static double Distance(PlanPoint start, PlanPoint end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static PlanPoint Direction(PlanPoint start, PlanPoint end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        return length < 1e-6 ? new PlanPoint(1, 0) : new PlanPoint(dx / length, dy / length);
    }
}
