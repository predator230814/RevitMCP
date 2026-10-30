using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.Intents;

internal enum PatternLinkedRoomLookupStatus
{
    Ok,
    LinkMissing,
    LinkUnloaded,
    RoomMissing,
}

internal sealed record PatternLinkedFaceCandidate(PatternShellSide Side, bool Planar, MepPoint Normal, MepPoint Origin);

internal sealed class PatternLinkedWall
{
    public required string WallRef { get; init; }

    public required string WallName { get; init; }

    public required bool LocationStraight { get; init; }

    public required IReadOnlyList<PatternLinkedFaceCandidate> Faces { get; init; }
}

internal sealed class PatternLinkedSegment
{
    public required MepPoint Start { get; init; }

    public required MepPoint End { get; init; }

    public required bool Straight { get; init; }

    public PatternLinkedWall? Wall { get; init; }
}

internal sealed class PatternLinkedRoomSnapshot
{
    public required PatternLinkedRoomLookupStatus Status { get; init; }

    public string LinkLabel { get; init; } = "Link";

    public PatternLinkTransform Transform { get; init; } = PatternLinkTransform.Identity;

    public string Label { get; init; } = "Room";

    public string LevelName { get; init; } = "Level";

    public double LevelElevationFeet { get; init; }

    public bool BoundaryTruncated { get; init; }

    public IReadOnlyList<IReadOnlyList<PatternLinkedSegment>> Loops { get; init; } = [];

    public static PatternLinkedRoomSnapshot Unavailable(PatternLinkedRoomLookupStatus status) => new() { Status = status };
}

internal readonly record struct PatternHostLevel(string LevelRef, string Name, double ElevationFeet);

internal static class PatternLinkedRoomFactsBuilder
{
    public const int MaxLoops = 4;

    public const int MinSegments = 3;

    public const int MaxSegments = 32;

    public const double AmbiguousAreaRatio = 0.85;

    public const double HostLevelToleranceFeet = 1.0 / 304.8;

    public const double FacePlaneToleranceFeet = 20.0 / 304.8;

    public const double FaceVerticalTolerance = 1e-3;

    public static readonly double FaceNormalMinimumDot = Math.Cos(5.0 * Math.PI / 180.0);

    public static bool TryBuild(
        string roomRef,
        string linkInstanceRef,
        string linkedRoomRef,
        PatternLinkedRoomSnapshot snapshot,
        IReadOnlyList<PatternHostLevel> hostLevels,
        out PatternRoomFacts facts,
        out PreviewRoomPatternItemStatus failure)
    {
        facts = null!;
        failure = PreviewRoomPatternItemStatus.LinkedRoomUnavailable;
        if (snapshot.Status != PatternLinkedRoomLookupStatus.Ok)
        {
            return false;
        }

        failure = PreviewRoomPatternItemStatus.LinkedBoundaryUnsupported;
        var transform = snapshot.Transform;
        if (!transform.IsPlanRigid
            || snapshot.BoundaryTruncated
            || snapshot.Loops.Count is < 1 or > MaxLoops
            || snapshot.Loops.Any(loop => loop is null || loop.Count is < MinSegments or > MaxSegments))
        {
            return false;
        }

        var measured = new List<(IReadOnlyList<PatternLinkedSegment> Segments, IReadOnlyList<PatternPlanPoint> Points, double Area)>();
        foreach (var segments in snapshot.Loops)
        {
            var points = segments.Select(segment => Plan(transform.Apply(segment.Start.X, segment.Start.Y, segment.Start.Z))).ToArray();
            var area = SignedArea(points);
            if (!double.IsFinite(area) || Math.Abs(area) <= 1e-6)
            {
                continue;
            }

            measured.Add((segments, points, area));
        }

        if (measured.Count == 0)
        {
            return false;
        }

        measured.Sort(static (left, right) => Math.Abs(right.Area).CompareTo(Math.Abs(left.Area)));
        if (measured.Count > 1 && Math.Abs(measured[1].Area) / Math.Abs(measured[0].Area) >= AmbiguousAreaRatio)
        {
            return false;
        }

        var outer = measured[0];
        var counterClockwise = outer.Area > 0;
        var supported = true;
        var walls = new List<PatternWallFacts>();
        foreach (var segment in outer.Segments)
        {
            var start = transform.Apply(segment.Start.X, segment.Start.Y, segment.Start.Z);
            var end = transform.Apply(segment.End.X, segment.End.Y, segment.End.Z);
            if (!segment.Straight)
            {
                supported = false;
            }

            if (segment.Wall is not { } wall || string.IsNullOrEmpty(wall.WallRef))
            {
                continue;
            }

            var straight = wall.LocationStraight && segment.Straight;
            if (!straight)
            {
                supported = false;
            }

            var face = straight ? SelectFace(wall.Faces, transform, Plan(start), Plan(end), counterClockwise) : null;
            walls.Add(new PatternWallFacts
            {
                WallRef = wall.WallRef,
                WallName = wall.WallName,
                Start = Plan(start),
                End = Plan(end),
                Straight = straight,
                FaceResolved = face is not null,
                LinkedFace = face,
            });
        }

        var hostElevation = transform.Apply(0, 0, snapshot.LevelElevationFeet).Z;
        var levelStatus = MatchHostLevel(hostElevation, hostLevels, out var hostLevel);
        facts = new PatternRoomFacts
        {
            RoomRef = roomRef,
            Label = string.IsNullOrWhiteSpace(snapshot.Label) ? "Room" : snapshot.Label,
            LevelRef = levelStatus == PatternHostLevelStatus.Resolved ? hostLevel.LevelRef : string.Empty,
            LevelName = levelStatus == PatternHostLevelStatus.Resolved ? hostLevel.Name : snapshot.LevelName,
            LevelElevationFeet = levelStatus == PatternHostLevelStatus.Resolved ? hostLevel.ElevationFeet : hostElevation,
            BoundarySupported = supported,
            Loop = outer.Points,
            Holes = measured.Skip(1).Select(item => item.Points).ToArray(),
            Walls = walls,
            SourceKind = PatternRoomSourceKind.Link,
            HostLevelStatus = levelStatus,
            Link = new PatternLinkedRoomSource(linkInstanceRef, linkedRoomRef, snapshot.LinkLabel, transform),
        };
        return true;
    }

    public static PatternHostLevelStatus MatchHostLevel(
        double hostElevationFeet,
        IReadOnlyList<PatternHostLevel> hostLevels,
        out PatternHostLevel level)
    {
        level = default;
        var matches = hostLevels
            .Where(candidate => !string.IsNullOrEmpty(candidate.LevelRef)
                && double.IsFinite(candidate.ElevationFeet)
                && Math.Abs(candidate.ElevationFeet - hostElevationFeet) <= HostLevelToleranceFeet)
            .ToArray();
        if (matches.Length == 0)
        {
            return PatternHostLevelStatus.NotFound;
        }

        if (matches.Length > 1)
        {
            return PatternHostLevelStatus.Ambiguous;
        }

        level = matches[0];
        return PatternHostLevelStatus.Resolved;
    }

    public static PatternLinkedFaceFacts? SelectFace(
        IReadOnlyList<PatternLinkedFaceCandidate> candidates,
        PatternLinkTransform transform,
        PatternPlanPoint start,
        PatternPlanPoint end,
        bool counterClockwise)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        if (!double.IsFinite(length) || length <= 1e-9)
        {
            return null;
        }

        var inwardX = counterClockwise ? -dy / length : dy / length;
        var inwardY = counterClockwise ? dx / length : -dx / length;
        var compatible = new List<PatternLinkedFaceFacts>();
        foreach (var candidate in candidates)
        {
            if (!candidate.Planar)
            {
                continue;
            }

            var normal = transform.ApplyVector(candidate.Normal.X, candidate.Normal.Y, candidate.Normal.Z);
            var planar = Math.Sqrt((normal.X * normal.X) + (normal.Y * normal.Y));
            if (!double.IsFinite(planar) || planar <= 1e-9 || Math.Abs(normal.Z) > FaceVerticalTolerance)
            {
                continue;
            }

            var nx = normal.X / planar;
            var ny = normal.Y / planar;
            if ((nx * inwardX) + (ny * inwardY) < FaceNormalMinimumDot)
            {
                continue;
            }

            var origin = transform.Apply(candidate.Origin.X, candidate.Origin.Y, candidate.Origin.Z);
            var face = new PatternLinkedFaceFacts(candidate.Side, nx, ny, (origin.X * nx) + (origin.Y * ny));
            if (Math.Abs(face.Distance(start.X, start.Y)) > FacePlaneToleranceFeet
                || Math.Abs(face.Distance(end.X, end.Y)) > FacePlaneToleranceFeet)
            {
                continue;
            }

            compatible.Add(face);
        }

        return compatible.Count == 1 ? compatible[0] : null;
    }

    private static PatternPlanPoint Plan(MepPoint point) => new(point.X, point.Y);

    private static double SignedArea(IReadOnlyList<PatternPlanPoint> loop)
    {
        double twice = 0;
        for (var index = 0; index < loop.Count; index++)
        {
            var current = loop[index];
            var next = loop[(index + 1) % loop.Count];
            twice += (current.X * next.Y) - (next.X * current.Y);
        }

        return twice * 0.5;
    }
}
