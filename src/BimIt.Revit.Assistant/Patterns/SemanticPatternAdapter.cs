namespace BimIt.Revit.Assistant.Patterns;

internal static class SemanticPatternAdapter
{
    public readonly record struct Placement(string Role, string PlacementKind, double X, double Y, string? FamilyHint);

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
        var placed = new List<Placement>(rules.Count);
        var complete = true;
        var consumed = new bool[devices.Count];
        foreach (var rule in rules)
        {
            if (rule.Anchor == PatternAnchors.PairedSymmetric)
            {
                if (!TryPair(rule, loop, centroidX, centroidY, openings, projectedEntrance, out var first, out var second)
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

            if (!TryOne(rule, devices, consumed, loop, centroidX, centroidY, openings, geometric, projectedEntrance, out var point, out var kind, out var hint))
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
            return TryDoor(openings, projectedEntrance, out var door) && InsetFrom(door, centroidX, centroidY, inset, out point);
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
            return TryDoor(openings, projectedEntrance, out var door)
                && TryOpposite(loop, door, out var start, out var end)
                && InsetFrom(Mid(start, end), centroidX, centroidY, inset, out point);
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
        out PlanPoint left,
        out PlanPoint right)
    {
        left = default;
        right = default;
        if (!TryDoor(openings, projectedEntrance, out var door) || !TryOpposite(loop, door, out var start, out var end))
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
        var mid = Mid(start, end);
        var inset = 0.15;
        return InsetFrom(new PlanPoint(mid.X - (alongX * half), mid.Y - (alongY * half)), centroidX, centroidY, inset, out left)
            && InsetFrom(new PlanPoint(mid.X + (alongX * half), mid.Y + (alongY * half)), centroidX, centroidY, inset, out right);
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

    private static bool TryDoor(IReadOnlyList<CandidateOpening> openings, PlanPoint? preferred, out PlanPoint door)
    {
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

    private static bool TryOpposite(IReadOnlyList<PlanPoint> loop, PlanPoint door, out PlanPoint start, out PlanPoint end)
    {
        start = default;
        end = default;
        if (!TryNearestEdge(loop, door, out var doorStart, out var doorEnd))
        {
            return false;
        }

        var doorVector = Direction(doorStart, doorEnd);
        var best = double.MinValue;
        var found = false;
        for (var index = 0; index < loop.Count; index++)
        {
            var edgeStart = loop[index];
            var edgeEnd = loop[(index + 1) % loop.Count];
            if (Same(edgeStart, doorStart) && Same(edgeEnd, doorEnd))
            {
                continue;
            }

            var edgeVector = Direction(edgeStart, edgeEnd);
            var parallel = Math.Abs((doorVector.X * edgeVector.X) + (doorVector.Y * edgeVector.Y));
            if (parallel < 0.85)
            {
                continue;
            }

            var mid = Mid(edgeStart, edgeEnd);
            var dx = mid.X - door.X;
            var dy = mid.Y - door.Y;
            var distance = (dx * dx) + (dy * dy);
            if (distance > best)
            {
                best = distance;
                start = edgeStart;
                end = edgeEnd;
                found = true;
            }
        }

        return found;
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

    private static bool Same(PlanPoint left, PlanPoint right)
    {
        return Math.Abs(left.X - right.X) < 1e-6 && Math.Abs(left.Y - right.Y) < 1e-6;
    }
}
