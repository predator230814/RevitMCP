namespace BimIt.Revit.Assistant.Patterns;

internal static class SemanticPatternAdapter
{
    public readonly record struct Placement(string Role, string PlacementKind, double X, double Y, string? FamilyHint);

    public readonly record struct EdgeSegment(PlanPoint Start, PlanPoint End);

    public static bool TryPlace(
        IReadOnlyList<PatternRule> rules,
        IReadOnlyList<SourceDevice> devices,
        IReadOnlyList<PlanPoint> loop,
        IReadOnlyList<IReadOnlyList<PlanPoint>> holes,
        double centroidX,
        double centroidY,
        IReadOnlyList<CandidateOpening> openings,
        IReadOnlyList<ProjectedDevice> geometric,
        PlanPoint? projectedEntrance,
        out IReadOnlyList<Placement> placements)
    {
        return TryPlace(rules, devices, loop, holes, centroidX, centroidY, openings, geometric, projectedEntrance, null, null, out placements);
    }

    public static bool TryPlace(
        IReadOnlyList<PatternRule> rules,
        IReadOnlyList<SourceDevice> devices,
        IReadOnlyList<PlanPoint> loop,
        IReadOnlyList<IReadOnlyList<PlanPoint>> holes,
        double centroidX,
        double centroidY,
        IReadOnlyList<CandidateOpening> openings,
        IReadOnlyList<ProjectedDevice> geometric,
        PlanPoint? projectedEntrance,
        PlanPoint? selectedDoor,
        IReadOnlyDictionary<int, EdgeSegment>? boundEdges,
        out IReadOnlyList<Placement> placements)
    {
        var placed = new List<Placement>(rules.Count);
        var complete = true;
        var consumed = new bool[devices.Count];
        for (var index = 0; index < rules.Count; index++)
        {
            var rule = rules[index];
            EdgeSegment? edge = boundEdges is not null && boundEdges.TryGetValue(index, out var bound) ? bound : null;
            if (rule.Anchor == PatternAnchors.PairedSymmetric)
            {
                if (!TryPair(rule, loop, centroidX, centroidY, openings, projectedEntrance, selectedDoor, edge, out var first, out var second)
                    || !Accept(first, loop, holes, out first)
                    || !Accept(second, loop, holes, out second))
                {
                    complete = false;
                    continue;
                }

                var pairHint = Hint(devices, consumed, rule.Role);
                placed.Add(new Placement(rule.Role, "boundary", first.X, first.Y, pairHint));
                placed.Add(new Placement(rule.Role, "boundary", second.X, second.Y, pairHint));
                continue;
            }

            if (!TryOne(rule, devices, consumed, loop, centroidX, centroidY, openings, geometric, projectedEntrance, selectedDoor, out var point, out var kind, out var hint))
            {
                complete = false;
                continue;
            }

            if (!Accept(point, loop, holes, out point))
            {
                complete = false;
                continue;
            }

            placed.Add(new Placement(rule.Role, kind, point.X, point.Y, hint));
        }

        placements = placed;
        return complete;
    }

    private static bool TryOne(
        PatternRule rule,
        IReadOnlyList<SourceDevice> devices,
        bool[] consumed,
        IReadOnlyList<PlanPoint> loop,
        double centroidX,
        double centroidY,
        IReadOnlyList<CandidateOpening> openings,
        IReadOnlyList<ProjectedDevice> geometric,
        PlanPoint? projectedEntrance,
        PlanPoint? selectedDoor,
        out PlanPoint point,
        out string kind,
        out string? hint)
    {
        point = default;
        kind = "interior";
        hint = Hint(devices, consumed, rule.Role);
        var inset = rule.OffsetMetres > 0 ? rule.OffsetMetres : 0.15;
        if (rule.Anchor == PatternAnchors.RoomCenter)
        {
            point = new PlanPoint(centroidX, centroidY);
            return true;
        }

        kind = "boundary";
        if (rule.Anchor == PatternAnchors.Entrance)
        {
            return TryDoor(openings, projectedEntrance, selectedDoor, out var door) && InsetFrom(door, centroidX, centroidY, inset, out point);
        }

        if (!TryDevicePoint(geometric, devices, rule.Role, out var observed))
        {
            return false;
        }

        if (rule.Anchor == PatternAnchors.NearestBoundary)
        {
            return TryNearest(loop, observed, out var edge) && InsetFrom(edge, centroidX, centroidY, inset, out point);
        }

        if (rule.Anchor == PatternAnchors.Corner)
        {
            return TryCorner(loop, observed, out var corner) && InsetFrom(corner, centroidX, centroidY, inset, out point);
        }

        if (rule.Anchor == PatternAnchors.OppositeBoundary)
        {
            if (!TryDoor(openings, projectedEntrance, selectedDoor, out var door)
                || !TryFacing(loop, door, out var facing))
            {
                return false;
            }

            point = new PlanPoint(facing.Hit.X + (facing.Inward.X * inset), facing.Hit.Y + (facing.Inward.Y * inset));
            return true;
        }

        return false;
    }

    private static bool TryPair(
        PatternRule rule,
        IReadOnlyList<PlanPoint> loop,
        double centroidX,
        double centroidY,
        IReadOnlyList<CandidateOpening> openings,
        PlanPoint? projectedEntrance,
        PlanPoint? selectedDoor,
        EdgeSegment? boundEdge,
        out PlanPoint left,
        out PlanPoint right)
    {
        left = default;
        right = default;
        PlanPoint start;
        PlanPoint end;
        PlanPoint centre;
        PlanPoint inward;
        if (boundEdge is EdgeSegment edge)
        {
            start = edge.Start;
            end = edge.End;
            centre = Mid(start, end);
            if (!TryInward(loop, start, end, centre, out inward))
            {
                return false;
            }
        }
        else if (TryDoor(openings, projectedEntrance, selectedDoor, out var door) && TryFacing(loop, door, out var facing))
        {
            start = facing.Start;
            end = facing.End;
            centre = facing.Hit;
            inward = facing.Inward;
        }
        else
        {
            return false;
        }

        var half = rule.OffsetMetres > 0 ? rule.OffsetMetres : 0.6;
        var alongX = end.X - start.X;
        var alongY = end.Y - start.Y;
        var length = Math.Sqrt((alongX * alongX) + (alongY * alongY));
        if (length < 1e-6)
        {
            return false;
        }

        alongX /= length;
        alongY /= length;
        var along = ((centre.X - start.X) * alongX) + ((centre.Y - start.Y) * alongY);
        const double margin = 0.15;
        if (length >= (2 * (half + margin)))
        {
            along = Math.Clamp(along, half + margin, length - half - margin);
        }

        var middle = new PlanPoint(start.X + (alongX * along), start.Y + (alongY * along));
        const double inset = 0.15;
        left = new PlanPoint(middle.X - (alongX * half) + (inward.X * inset), middle.Y - (alongY * half) + (inward.Y * inset));
        right = new PlanPoint(middle.X + (alongX * half) + (inward.X * inset), middle.Y + (alongY * half) + (inward.Y * inset));
        return true;
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

    private static string? Hint(IReadOnlyList<SourceDevice> devices, bool[] consumed, string role)
    {
        for (var index = 0; index < devices.Count; index++)
        {
            if (!consumed[index] && devices[index].Role == role)
            {
                consumed[index] = true;
                return devices[index].FamilyHint;
            }
        }

        return null;
    }

    private static bool TryDevicePoint(IReadOnlyList<ProjectedDevice> geometric, IReadOnlyList<SourceDevice> devices, string role, out PlanPoint point)
    {
        point = default;
        for (var index = 0; index < devices.Count && index < geometric.Count; index++)
        {
            if (devices[index].Role == role)
            {
                point = new PlanPoint(geometric[index].LocalX, geometric[index].LocalY);
                return true;
            }
        }

        return false;
    }

    private static bool TryDoor(IReadOnlyList<CandidateOpening> openings, PlanPoint? preferred, PlanPoint? selected, out PlanPoint door)
    {
        if (selected is PlanPoint chosen)
        {
            door = chosen;
            return true;
        }

        var doors = openings.Where(opening => opening.Kind == "door").ToArray();
        door = default;
        if (doors.Length == 0 || (doors.Length > 1 && preferred is null))
        {
            return doors.Length == 1 && Assign(doors[0], out door);
        }

        if (doors.Length == 1)
        {
            return Assign(doors[0], out door);
        }

        var best = double.MaxValue;
        var found = false;
        foreach (var opening in doors)
        {
            var dx = opening.X - preferred!.Value.X;
            var dy = opening.Y - preferred.Value.Y;
            var distance = (dx * dx) + (dy * dy);
            if (distance < best)
            {
                best = distance;
                door = new PlanPoint(opening.X, opening.Y);
                found = true;
            }
        }

        return found;
    }

    private static bool Assign(CandidateOpening opening, out PlanPoint door)
    {
        door = new PlanPoint(opening.X, opening.Y);
        return true;
    }

    private static bool TryNearest(IReadOnlyList<PlanPoint> loop, PlanPoint point, out PlanPoint projected)
    {
        projected = loop[0];
        var best = double.MaxValue;
        var found = false;
        for (var index = 0; index < loop.Count; index++)
        {
            var candidate = Project(point, loop[index], loop[(index + 1) % loop.Count]);
            var dx = point.X - candidate.X;
            var dy = point.Y - candidate.Y;
            var distance = (dx * dx) + (dy * dy);
            if (distance < best)
            {
                best = distance;
                projected = candidate;
                found = true;
            }
        }

        return found;
    }

    private static bool TryCorner(IReadOnlyList<PlanPoint> loop, PlanPoint point, out PlanPoint corner)
    {
        corner = loop[0];
        var best = double.MaxValue;
        foreach (var vertex in loop)
        {
            var dx = point.X - vertex.X;
            var dy = point.Y - vertex.Y;
            var distance = (dx * dx) + (dy * dy);
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
            var projected = Project(point, edgeStart, edgeEnd);
            var dx = point.X - projected.X;
            var dy = point.Y - projected.Y;
            var distance = (dx * dx) + (dy * dy);
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

    private static bool InsetFrom(PlanPoint edge, double centroidX, double centroidY, double distance, out PlanPoint point)
    {
        var dx = centroidX - edge.X;
        var dy = centroidY - edge.Y;
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

    private static PlanPoint Mid(PlanPoint start, PlanPoint end) => new((start.X + end.X) * 0.5, (start.Y + end.Y) * 0.5);

    private static PlanPoint Direction(PlanPoint start, PlanPoint end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        return length < 1e-6 ? new PlanPoint(1, 0) : new PlanPoint(dx / length, dy / length);
    }
}
