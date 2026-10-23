using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.Intents;

internal readonly record struct PatternWallHit(PatternWallFacts Wall, double InsideX, double InsideY);

internal static class PatternBoundaryResolver
{
    public static bool TryResolve(
        double x,
        double y,
        IReadOnlyList<PatternPlanPoint> loop,
        IReadOnlyList<PatternWallFacts> walls,
        double toleranceFeet,
        out PatternWallHit hit,
        out PreviewRoomPatternItemStatus failure)
    {
        hit = default;
        failure = PreviewRoomPatternItemStatus.NoWall;
        if (loop.Count < 3 || walls.Count == 0)
        {
            return false;
        }

        var edges = NearEdges(x, y, loop, toleranceFeet);
        if (edges.Count == 0)
        {
            failure = PreviewRoomPatternItemStatus.NoWall;
            return false;
        }

        if (!Collinear(edges))
        {
            failure = PreviewRoomPatternItemStatus.AmbiguousWall;
            return false;
        }

        var edge = edges[0];
        var matches = new List<PatternWallFacts>();
        foreach (var wall in walls)
        {
            if (Overlaps(edge, wall, toleranceFeet))
            {
                matches.Add(wall);
            }
        }

        if (matches.Count == 0)
        {
            failure = PreviewRoomPatternItemStatus.NoWall;
            return false;
        }

        if (matches.Any(wall => !wall.Straight))
        {
            failure = matches.Count == 1 && !matches[0].Straight
                ? PreviewRoomPatternItemStatus.CurvedWall
                : PreviewRoomPatternItemStatus.AmbiguousWall;
            return false;
        }

        if (matches.Count != 1)
        {
            failure = PreviewRoomPatternItemStatus.AmbiguousWall;
            return false;
        }

        var chosen = matches[0];
        var inside = InsideNormal(loop, chosen);
        hit = new PatternWallHit(chosen, inside.X, inside.Y);
        return true;
    }

    public static bool Contains(double x, double y, IReadOnlyList<PatternPlanPoint> loop, double toleranceFeet)
    {
        if (loop.Count < 3)
        {
            return false;
        }

        if (DistanceToLoop(x, y, loop) <= toleranceFeet)
        {
            return true;
        }

        var inside = false;
        for (var index = 0; index < loop.Count; index++)
        {
            var a = loop[index];
            var b = loop[(index + 1) % loop.Count];
            var crosses = (a.Y > y) != (b.Y > y)
                && x < ((b.X - a.X) * (y - a.Y) / ((b.Y - a.Y) == 0 ? 1e-12 : (b.Y - a.Y))) + a.X;
            if (crosses)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static List<(PatternPlanPoint A, PatternPlanPoint B)> NearEdges(
        double x,
        double y,
        IReadOnlyList<PatternPlanPoint> loop,
        double toleranceFeet)
    {
        var edges = new List<(PatternPlanPoint A, PatternPlanPoint B)>();
        for (var index = 0; index < loop.Count; index++)
        {
            var a = loop[index];
            var b = loop[(index + 1) % loop.Count];
            if (SegmentDistance(x, y, a, b) <= toleranceFeet)
            {
                edges.Add((a, b));
            }
        }

        return edges;
    }

    private static bool Collinear(IReadOnlyList<(PatternPlanPoint A, PatternPlanPoint B)> edges)
    {
        var direction = Direction(edges[0].A, edges[0].B);
        for (var index = 1; index < edges.Count; index++)
        {
            var other = Direction(edges[index].A, edges[index].B);
            if (Math.Abs(Cross(direction, other)) > 0.05)
            {
                return false;
            }
        }

        return true;
    }

    private static bool Overlaps(
        (PatternPlanPoint A, PatternPlanPoint B) edge,
        PatternWallFacts wall,
        double toleranceFeet)
    {
        var edgeDirection = Direction(edge.A, edge.B);
        var wallDirection = Direction(wall.Start, wall.End);
        if (Math.Abs(Cross(edgeDirection, wallDirection)) > 0.05)
        {
            return false;
        }

        if (SegmentDistance(wall.Start.X, wall.Start.Y, edge.A, edge.B) > toleranceFeet
            && SegmentDistance(wall.End.X, wall.End.Y, edge.A, edge.B) > toleranceFeet
            && SegmentDistance((wall.Start.X + wall.End.X) / 2, (wall.Start.Y + wall.End.Y) / 2, edge.A, edge.B) > toleranceFeet)
        {
            return false;
        }

        var origin = edge.A;
        var edgeSpan = Project(edge.B, origin, edgeDirection) - Project(edge.A, origin, edgeDirection);
        var wallA = Project(wall.Start, origin, edgeDirection);
        var wallB = Project(wall.End, origin, edgeDirection);
        var edgeMin = Math.Min(0, edgeSpan);
        var edgeMax = Math.Max(0, edgeSpan);
        var wallMin = Math.Min(wallA, wallB);
        var wallMax = Math.Max(wallA, wallB);
        var overlap = Math.Min(edgeMax, wallMax) - Math.Max(edgeMin, wallMin);
        return overlap > toleranceFeet;
    }

    private static (double X, double Y) InsideNormal(IReadOnlyList<PatternPlanPoint> loop, PatternWallFacts wall)
    {
        var direction = Direction(wall.Start, wall.End);
        var left = (-direction.Y, direction.X);
        var centroid = Centroid(loop);
        var midX = (wall.Start.X + wall.End.X) / 2;
        var midY = (wall.Start.Y + wall.End.Y) / 2;
        var toward = (centroid.X - midX, centroid.Y - midY);
        return Dot(left, toward) >= 0 ? left : (direction.Y, -direction.X);
    }

    private static PatternPlanPoint Centroid(IReadOnlyList<PatternPlanPoint> loop)
    {
        double x = 0;
        double y = 0;
        foreach (var point in loop)
        {
            x += point.X;
            y += point.Y;
        }

        return new PatternPlanPoint(x / loop.Count, y / loop.Count);
    }

    private static double DistanceToLoop(double x, double y, IReadOnlyList<PatternPlanPoint> loop)
    {
        var best = double.MaxValue;
        for (var index = 0; index < loop.Count; index++)
        {
            best = Math.Min(best, SegmentDistance(x, y, loop[index], loop[(index + 1) % loop.Count]));
        }

        return best;
    }

    private static double SegmentDistance(double x, double y, PatternPlanPoint a, PatternPlanPoint b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lengthSquared = (dx * dx) + (dy * dy);
        if (lengthSquared <= 1e-12)
        {
            return Math.Sqrt(((x - a.X) * (x - a.X)) + ((y - a.Y) * (y - a.Y)));
        }

        var t = Math.Clamp((((x - a.X) * dx) + ((y - a.Y) * dy)) / lengthSquared, 0, 1);
        var px = a.X + (t * dx);
        var py = a.Y + (t * dy);
        return Math.Sqrt(((x - px) * (x - px)) + ((y - py) * (y - py)));
    }

    private static (double X, double Y) Direction(PatternPlanPoint a, PatternPlanPoint b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length <= 1e-12)
        {
            return (1, 0);
        }

        return (dx / length, dy / length);
    }

    private static double Project(PatternPlanPoint point, PatternPlanPoint origin, (double X, double Y) direction)
    {
        return ((point.X - origin.X) * direction.X) + ((point.Y - origin.Y) * direction.Y);
    }

    private static double Cross((double X, double Y) a, (double X, double Y) b) => (a.X * b.Y) - (a.Y * b.X);

    private static double Dot((double X, double Y) a, (double X, double Y) b) => (a.X * b.X) + (a.Y * b.Y);
}
