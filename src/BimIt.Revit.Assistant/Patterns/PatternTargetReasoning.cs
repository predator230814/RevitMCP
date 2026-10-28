using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using BimIt.Revit.Assistant.Ipc;

namespace BimIt.Revit.Assistant.Patterns;

internal interface IPatternTargetReasoner
{
    Task<PatternTargetReasoningResult> ReasonAsync(PatternTargetReasoningRequest request, CancellationToken cancellationToken);
}

internal sealed class PatternTargetDraft
{
    public required PatternTargetReasoningRequest Request { get; init; }

    public required PatternTargetSnapshot Snapshot { get; init; }
}

internal sealed record PatternTargetPending(
    string GenerationId,
    string InstanceId,
    string? DocumentId,
    string ArtifactId,
    int PageNumber,
    PatternTargetReasoningRequest Request,
    PatternTargetSnapshot Snapshot);

internal sealed class PatternTargetSnapshot
{
    public required SourceRoomPattern Source { get; init; }

    public required bool NamedRooms { get; init; }

    public string? RoomName { get; init; }

    public required IReadOnlyList<RoomShapeMatch> Matches { get; init; }

    public required IReadOnlyList<PatternTargetRoomSnapshot> Rooms { get; init; }

    public required RoomPatternReview.Review Review { get; init; }
}

internal sealed class PatternTargetRoomSnapshot
{
    public required string RoomKey { get; init; }

    public required string RoomRef { get; init; }

    public required CandidateRoom Room { get; init; }

    public required IReadOnlyList<PatternTargetFeatureSnapshot> Doors { get; init; }

    public required IReadOnlyList<PatternTargetEdgeSnapshot> Edges { get; init; }
}

internal sealed class PatternTargetFeatureSnapshot
{
    public required string Key { get; init; }

    public required PlanPoint Point { get; init; }

    public string? OtherSideName { get; init; }

    public string? OtherSideStatus { get; init; }
}

internal sealed class PatternTargetEdgeSnapshot
{
    public required string Key { get; init; }

    public required PlanPoint Start { get; init; }

    public required PlanPoint End { get; init; }
}

internal sealed class PatternTargetReasoningRequest
{
    public PatternTargetSourceContext? Source { get; init; }

    public IReadOnlyList<PatternTargetSpecContext> Patterns { get; init; } = [];

    public IReadOnlyList<PatternTargetRoomContext> Rooms { get; init; } = [];
}

internal sealed class PatternTargetSourceContext
{
    public IReadOnlyList<PatternTargetPoint> Outline { get; init; } = [];

    public IReadOnlyList<PatternTargetSourceEdgeContext> Edges { get; init; } = [];

    public PatternTargetSourceEntranceContext? Entrance { get; init; }
}

internal sealed class PatternTargetSourceEdgeContext
{
    public required string Key { get; init; }

    public string? Side { get; init; }

    public double StartX { get; init; }

    public double StartY { get; init; }

    public double EndX { get; init; }

    public double EndY { get; init; }

    public double RelativeLength { get; init; }
}

internal sealed class PatternTargetSourceEntranceContext
{
    public double X { get; init; }

    public double Y { get; init; }

    public string? EdgeKey { get; init; }
}

internal sealed class PatternTargetSpecContext
{
    public required string PatternKey { get; init; }

    public required string Role { get; init; }

    public required string Placement { get; init; }

    public required string Anchor { get; init; }

    public int Count { get; init; }

    public required string Arrangement { get; init; }

    public string? Basis { get; init; }

    public string? Group { get; init; }

    public PatternTargetEvidenceContext? Evidence { get; init; }
}

internal sealed class PatternTargetEvidenceContext
{
    public int DeviceCount { get; init; }

    public string? Placement { get; init; }

    public double? CentroidX { get; init; }

    public double? CentroidY { get; init; }

    public double? Spread { get; init; }

    public string? SourceEdgeKey { get; init; }

    public string? Side { get; init; }

    public double? Along { get; init; }
}

internal sealed class PatternTargetRoomContext
{
    public required string RoomKey { get; init; }

    public required string Label { get; init; }

    public string? Level { get; init; }

    public required string Source { get; init; }

    public double Aspect { get; init; }

    public IReadOnlyList<PatternTargetPoint> Outline { get; init; } = [];

    public IReadOnlyList<IReadOnlyList<PatternTargetPoint>> Holes { get; init; } = [];

    public IReadOnlyList<PatternTargetDoorContext> Doors { get; init; } = [];

    public IReadOnlyList<PatternTargetOpeningContext> Windows { get; init; } = [];

    public IReadOnlyList<PatternTargetEdgeContext> Edges { get; init; } = [];
}

internal sealed class PatternTargetPoint
{
    public double X { get; init; }

    public double Y { get; init; }
}

internal sealed class PatternTargetDoorContext
{
    public required string Key { get; init; }

    public double X { get; init; }

    public double Y { get; init; }

    public double? FacingX { get; init; }

    public double? FacingY { get; init; }

    public double? WidthMetres { get; init; }

    public string? OtherSideName { get; init; }

    public string? OtherSideNumber { get; init; }

    public string? OtherSideStatus { get; init; }
}

internal sealed class PatternTargetOpeningContext
{
    public required string Key { get; init; }

    public double X { get; init; }

    public double Y { get; init; }

    public double? WidthMetres { get; init; }
}

internal sealed class PatternTargetEdgeContext
{
    public required string Key { get; init; }

    public string? Side { get; init; }

    public double StartX { get; init; }

    public double StartY { get; init; }

    public double EndX { get; init; }

    public double EndY { get; init; }

    public double RelativeLength { get; init; }
}

internal sealed record PatternTargetRoomChoice(
    string RoomKey,
    string Assessment,
    string? EntranceFeatureKey,
    IReadOnlyList<PatternTargetEdgeBinding> Bindings);

internal sealed record PatternTargetEdgeBinding(string PatternKey, string EdgeKey);

internal sealed record PatternTargetReasoningResult(bool Unavailable, IReadOnlyList<PatternTargetRoomChoice> Rooms, string? Failure = null)
{
    public const string ProviderError = "provider_error";
    public const string UnreadableResponse = "unreadable_response";

    public static PatternTargetReasoningResult None { get; } = new(true, []);

    public static PatternTargetReasoningResult Failed(string failure) => new(true, [], failure);
}

internal static class PatternTargetContext
{
    public const int MaxRooms = 8;

    public const string PatternKeyPrefix = "pattern_";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static PatternTargetDraft? Build(
        SourceRoomPattern source,
        IReadOnlyList<CandidateRoom> candidates,
        IReadOnlyList<RoomShapeMatch> matches,
        RoomPatternReview.Review review,
        string? roomName)
    {
        var specs = source.Specs ?? [];
        if (specs.Count == 0)
        {
            return null;
        }

        var byRef = new Dictionary<string, CandidateRoom>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrEmpty(candidate.ElementRef))
            {
                byRef[candidate.ElementRef] = candidate;
            }
        }

        var eligible = matches
            .Where(match => !string.IsNullOrEmpty(match.RoomRef) && byRef.ContainsKey(match.RoomRef!) && IsEligible(match))
            .OrderByDescending(match => match.Score)
            .ThenBy(match => match.Label, StringComparer.Ordinal)
            .Take(MaxRooms)
            .ToArray();
        if (eligible.Length == 0)
        {
            return null;
        }

        var evidence = source.Evidence ?? PatternEvidence.Describe(source);
        var patternContexts = specs.Select((spec, index) => new PatternTargetSpecContext
        {
            PatternKey = PatternKeyPrefix + index.ToString(CultureInfo.InvariantCulture),
            Role = spec.Role,
            Placement = spec.Placement,
            Anchor = spec.Anchor,
            Count = spec.Count,
            Arrangement = spec.Arrangement,
            Basis = spec.Basis,
            Group = spec.Group,
            Evidence = Evidence(index < evidence.Count ? evidence[index] : null),
        }).ToArray();
        var snapshots = new List<PatternTargetRoomSnapshot>(eligible.Length);
        var contexts = new List<PatternTargetRoomContext>(eligible.Length);
        for (var index = 0; index < eligible.Length; index++)
        {
            var match = eligible[index];
            var room = byRef[match.RoomRef!];
            var roomKey = "room_" + index.ToString(CultureInfo.InvariantCulture);
            var doors = new List<PatternTargetFeatureSnapshot>();
            var doorContexts = new List<PatternTargetDoorContext>();
            var windows = new List<PatternTargetOpeningContext>();
            var doorIndex = 0;
            var windowIndex = 0;
            foreach (var opening in room.Openings)
            {
                var point = Unit(opening.X, opening.Y, room);
                if (opening.Kind == "door")
                {
                    var key = "door_" + doorIndex.ToString(CultureInfo.InvariantCulture);
                    doorIndex++;
                    doors.Add(new PatternTargetFeatureSnapshot
                    {
                        Key = key,
                        Point = new PlanPoint(opening.X, opening.Y),
                        OtherSideName = Safe(opening.OtherSideName),
                        OtherSideStatus = SafeStatus(opening.OtherSideStatus),
                    });
                    doorContexts.Add(new PatternTargetDoorContext
                    {
                        Key = key,
                        X = point.X,
                        Y = point.Y,
                        FacingX = opening.FacingX,
                        FacingY = opening.FacingY,
                        WidthMetres = opening.WidthMetres,
                        OtherSideName = Safe(opening.OtherSideName),
                        OtherSideNumber = Safe(opening.OtherSideNumber),
                        OtherSideStatus = SafeStatus(opening.OtherSideStatus),
                    });
                }
                else
                {
                    windows.Add(new PatternTargetOpeningContext
                    {
                        Key = "window_" + windowIndex.ToString(CultureInfo.InvariantCulture),
                        X = point.X,
                        Y = point.Y,
                        WidthMetres = opening.WidthMetres,
                    });
                    windowIndex++;
                }
            }

            var edges = new List<PatternTargetEdgeSnapshot>();
            var edgeContexts = new List<PatternTargetEdgeContext>();
            var loop = room.Loop;
            var perimeter = Perimeter(loop);
            for (var edgeIndex = 0; edgeIndex < loop.Count; edgeIndex++)
            {
                var start = loop[edgeIndex];
                var end = loop[(edgeIndex + 1) % loop.Count];
                var key = "edge_" + edgeIndex.ToString(CultureInfo.InvariantCulture);
                var startUnit = Unit(start.X, start.Y, room);
                var endUnit = Unit(end.X, end.Y, room);
                edges.Add(new PatternTargetEdgeSnapshot { Key = key, Start = start, End = end });
                edgeContexts.Add(new PatternTargetEdgeContext
                {
                    Key = key,
                    Side = PatternEvidence.Compass(loop, edgeIndex),
                    StartX = startUnit.X,
                    StartY = startUnit.Y,
                    EndX = endUnit.X,
                    EndY = endUnit.Y,
                    RelativeLength = Relative(Length(start, end), perimeter),
                });
            }

            snapshots.Add(new PatternTargetRoomSnapshot
            {
                RoomKey = roomKey,
                RoomRef = match.RoomRef!,
                Room = room,
                Doors = doors,
                Edges = edges,
            });
            var width = loop.Max(point => point.X) - loop.Min(point => point.X);
            var height = loop.Max(point => point.Y) - loop.Min(point => point.Y);
            contexts.Add(new PatternTargetRoomContext
            {
                RoomKey = roomKey,
                Label = Safe(match.Label) ?? "Room",
                Level = Safe(match.LevelName),
                Source = room.SourceKind == "link" ? "linked" : "host",
                Aspect = height < 1e-6 ? 0 : Math.Round(width / height, 3, MidpointRounding.AwayFromZero),
                Outline = loop.Select(point =>
                {
                    var unit = Unit(point.X, point.Y, room);
                    return new PatternTargetPoint { X = unit.X, Y = unit.Y };
                }).ToArray(),
                Holes = (room.Holes ?? []).Select(hole => (IReadOnlyList<PatternTargetPoint>)hole.Select(point =>
                {
                    var unit = Unit(point.X, point.Y, room);
                    return new PatternTargetPoint { X = unit.X, Y = unit.Y };
                }).ToArray()).ToArray(),
                Doors = doorContexts,
                Windows = windows,
                Edges = edgeContexts,
            });
        }

        return new PatternTargetDraft
        {
            Request = new PatternTargetReasoningRequest { Source = SourceContext(source), Patterns = patternContexts, Rooms = contexts },
            Snapshot = new PatternTargetSnapshot
            {
                Source = source with { Evidence = evidence },
                NamedRooms = roomName is not null,
                RoomName = roomName,
                Matches = matches.ToArray(),
                Rooms = snapshots,
                Review = review,
            },
        };
    }

    public const string ResponseContract =
        """
        Compare the source pattern with each candidate room. Door other_side tells which room a door connects to. A dwelling entrance usually connects to a corridor, hall, lobby, or the outside, not to a bathroom or closet.
        Each pattern names a role, a placement (boundary or interior), an anchor, a count, and an arrangement. Its evidence says where the group sits in the source: the source edge, the side of the source drawing, and the position along that edge. Source sides are drawing sides. Target edge sides are plan directions. Compare relations, not names: which wall holds the entrance, which wall faces it, and which walls are beside it.
        Reply with one JSON object and nothing else, in exactly this shape:
        {"rooms":[{"room_key":"room_0","assessment":"compatible","entrance_feature_key":"door_0","bindings":[{"pattern_key":"pattern_2","target_edge_key":"edge_3"}]}]}
        assessment is compatible, uncertain, or incompatible.
        entrance_feature_key is one door key of that room, or null when the entrance is not clear.
        bindings names the target edge for each pattern whose anchor is source_correspondence. Choose the target edge that plays the same role as the pattern's source edge. Independent patterns on different source edges should bind to different target edges. Bind two patterns to the same target edge only when their source evidence names the same source edge. Never bind an interior pattern or a pattern with another anchor. Omit a binding when no edge corresponds clearly.
        Use only keys from the request. Do not add other fields, coordinates, or explanations.
        """;

    public static string Serialize(PatternTargetReasoningRequest request)
    {
        return JsonSerializer.Serialize(request, Json);
    }

    public static string Compose(PatternTargetReasoningRequest request)
    {
        return ResponseContract + "\n\nRequest:\n" + Serialize(request);
    }

    private static PatternTargetSourceContext SourceContext(SourceRoomPattern source)
    {
        var loop = source.Boundary;
        var minX = loop.Min(point => point.X);
        var minY = loop.Min(point => point.Y);
        var width = Math.Max(loop.Max(point => point.X) - minX, 1e-9);
        var height = Math.Max(loop.Max(point => point.Y) - minY, 1e-9);
        PatternTargetPoint Normal(PlanPoint point) => new()
        {
            X = Round(Math.Clamp((point.X - minX) / width, 0, 1)),
            Y = Round(Math.Clamp((point.Y - minY) / height, 0, 1)),
        };

        var perimeter = Perimeter(loop);
        var edges = new List<PatternTargetSourceEdgeContext>(loop.Count);
        for (var index = 0; index < loop.Count; index++)
        {
            var start = Normal(loop[index]);
            var end = Normal(loop[(index + 1) % loop.Count]);
            edges.Add(new PatternTargetSourceEdgeContext
            {
                Key = SourceEdgeKey(index),
                Side = PatternEvidence.Side(loop, index),
                StartX = start.X,
                StartY = start.Y,
                EndX = end.X,
                EndY = end.Y,
                RelativeLength = Relative(Length(loop[index], loop[(index + 1) % loop.Count]), perimeter),
            });
        }

        PatternTargetSourceEntranceContext? entrance = null;
        if (source.Entrance is PlanPoint point)
        {
            var nearest = 0;
            var best = double.MaxValue;
            for (var index = 0; index < loop.Count; index++)
            {
                var distance = DistanceToSegment(point, loop[index], loop[(index + 1) % loop.Count]);
                if (distance < best)
                {
                    best = distance;
                    nearest = index;
                }
            }

            var unit = Normal(point);
            entrance = new PatternTargetSourceEntranceContext { X = unit.X, Y = unit.Y, EdgeKey = SourceEdgeKey(nearest) };
        }

        return new PatternTargetSourceContext
        {
            Outline = loop.Select(Normal).ToArray(),
            Edges = edges,
            Entrance = entrance,
        };
    }

    private static PatternTargetEvidenceContext? Evidence(PatternSpecEvidence? evidence)
    {
        if (evidence is null || evidence.DeviceIndexes.Count == 0)
        {
            return null;
        }

        return new PatternTargetEvidenceContext
        {
            DeviceCount = evidence.DeviceIndexes.Count,
            Placement = evidence.Placement,
            CentroidX = evidence.Centroid?.X,
            CentroidY = evidence.Centroid?.Y,
            Spread = evidence.Spread,
            SourceEdgeKey = evidence.EdgeIndex is int edge ? SourceEdgeKey(edge) : null,
            Side = evidence.Side,
            Along = evidence.Along,
        };
    }

    private static string SourceEdgeKey(int index) => "source_edge_" + index.ToString(CultureInfo.InvariantCulture);

    private static bool IsEligible(RoomShapeMatch match)
    {
        return match.Reason is null or "low_shape_similarity" or "aspect_mismatch" or "named_room";
    }

    private static double Perimeter(IReadOnlyList<PlanPoint> loop)
    {
        var perimeter = 0.0;
        for (var index = 0; index < loop.Count; index++)
        {
            perimeter += Length(loop[index], loop[(index + 1) % loop.Count]);
        }

        return perimeter;
    }

    private static double Relative(double length, double perimeter)
    {
        return perimeter < 1e-6 ? 0 : Math.Round(length / perimeter, 3, MidpointRounding.AwayFromZero);
    }

    private static double Length(PlanPoint start, PlanPoint end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static double DistanceToSegment(PlanPoint point, PlanPoint start, PlanPoint end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var squared = (dx * dx) + (dy * dy);
        var t = squared < 1e-18 ? 0 : Math.Clamp((((point.X - start.X) * dx) + ((point.Y - start.Y) * dy)) / squared, 0, 1);
        return Length(point, new PlanPoint(start.X + (t * dx), start.Y + (t * dy)));
    }

    private static double Round(double value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);

    private static PlanPoint Unit(double x, double y, CandidateRoom room)
    {
        var minX = double.MaxValue;
        var minY = double.MaxValue;
        var maxX = double.MinValue;
        var maxY = double.MinValue;
        foreach (var point in room.Loop)
        {
            var localX = point.X - room.CentroidX;
            var localY = point.Y - room.CentroidY;
            minX = Math.Min(minX, localX);
            minY = Math.Min(minY, localY);
            maxX = Math.Max(maxX, localX);
            maxY = Math.Max(maxY, localY);
        }

        var width = Math.Max(maxX - minX, 1e-6);
        var height = Math.Max(maxY - minY, 1e-6);
        var unitX = Math.Clamp((x - room.CentroidX - minX) / width, 0, 1);
        var unitY = Math.Clamp((y - room.CentroidY - minY) / height, 0, 1);
        return new PlanPoint(Math.Round(unitX, 4, MidpointRounding.AwayFromZero), Math.Round(unitY, 4, MidpointRounding.AwayFromZero));
    }

    internal static string? Safe(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Contains('\\', StringComparison.Ordinal)
            || trimmed.Contains('/', StringComparison.Ordinal)
            || trimmed.Contains(':', StringComparison.Ordinal)
            || trimmed.Contains(".rvt", StringComparison.OrdinalIgnoreCase)
            || trimmed.Any(char.IsControl))
        {
            return null;
        }

        return trimmed.Length <= 80 ? trimmed : trimmed[..80];
    }

    private static string? SafeStatus(string? status)
    {
        return status is "room" or "outside" or "unbounded" or "unknown" ? status : status is null ? null : "unknown";
    }
}

internal static class PatternTargetResponseParser
{
    private const int MaxCharacters = 20_000;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static bool TryParse(string? content, out PatternTargetReasoningResult result)
    {
        result = PatternTargetReasoningResult.None;
        if (string.IsNullOrWhiteSpace(content) || content.Length > MaxCharacters)
        {
            return false;
        }

        var first = content.IndexOf('{');
        var last = content.LastIndexOf('}');
        if (first < 0 || last <= first)
        {
            return false;
        }

        var json = content[first..(last + 1)];

        TargetBody? body;
        try
        {
            body = JsonSerializer.Deserialize<TargetBody>(json, Json);
        }
        catch (JsonException)
        {
            return false;
        }

        if (body?.Rooms is null || body.Rooms.Count > PatternTargetContext.MaxRooms)
        {
            return false;
        }

        var rooms = new List<PatternTargetRoomChoice>(body.Rooms.Count);
        foreach (var room in body.Rooms)
        {
            if (room.RoomKey is null || room.Assessment is not ("compatible" or "uncertain" or "incompatible"))
            {
                return false;
            }

            var bindings = new List<PatternTargetEdgeBinding>();
            foreach (var binding in room.Bindings ?? [])
            {
                if (binding.PatternKey is null || binding.TargetEdgeKey is null || bindings.Count >= PatternSpec.MaxSpecs * 2)
                {
                    return false;
                }

                bindings.Add(new PatternTargetEdgeBinding(binding.PatternKey, binding.TargetEdgeKey));
            }

            rooms.Add(new PatternTargetRoomChoice(room.RoomKey, room.Assessment, room.EntranceFeatureKey, bindings));
        }

        result = new PatternTargetReasoningResult(false, rooms);
        return true;
    }

    private sealed class TargetBody
    {
        public List<TargetRoom>? Rooms { get; set; }
    }

    private sealed class TargetRoom
    {
        public string? RoomKey { get; set; }

        public string? Assessment { get; set; }

        public string? EntranceFeatureKey { get; set; }

        public List<TargetBinding>? Bindings { get; set; }
    }

    private sealed class TargetBinding
    {
        public string? PatternKey { get; set; }

        public string? TargetEdgeKey { get; set; }
    }
}

internal sealed class PatternTargetOutcome
{
    public required RoomPatternReview.Review Review { get; init; }

    public required IReadOnlyList<RoomShapeMatch> Shown { get; init; }

    public int Resolved { get; init; }

    public int Partial { get; init; }

    public int Unresolved { get; init; }
}

internal static class PatternTargetResolution
{
    public static PatternTargetOutcome Apply(PatternTargetPending pending, PatternTargetReasoningResult result)
    {
        var choices = new Dictionary<string, PatternTargetRoomChoice>(StringComparer.Ordinal);
        foreach (var choice in result.Rooms)
        {
            choices.TryAdd(choice.RoomKey, choice);
        }

        var notes = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var updated = new List<RoomShapeMatch>(pending.Snapshot.Matches.Count);
        var statuses = new List<string>();
        foreach (var match in pending.Snapshot.Matches)
        {
            var snapshot = pending.Snapshot.Rooms.FirstOrDefault(room => room.RoomRef == match.RoomRef);
            if (snapshot is null)
            {
                updated.Add(match);
                continue;
            }

            choices.TryGetValue(snapshot.RoomKey, out var choice);
            var resolved = Resolve(pending.Snapshot.Source, match, snapshot, choice, out var lines);
            updated.Add(resolved);
            statuses.Add(resolved.AdaptationStatus ?? PatternAdaptationStatus.Unresolved);
            if (!string.IsNullOrEmpty(match.RoomRef))
            {
                notes[match.RoomRef] = lines;
            }
        }

        var shown = RoomPatternGrounding.ShownMatches(updated, pending.Snapshot.NamedRooms);
        return new PatternTargetOutcome
        {
            Review = Rebuild(pending.Snapshot.Review, updated, shown, notes, pending.Snapshot.NamedRooms),
            Shown = shown,
            Resolved = statuses.Count(status => status == PatternAdaptationStatus.Resolved),
            Partial = statuses.Count(status => status == PatternAdaptationStatus.Partial),
            Unresolved = statuses.Count(status => status == PatternAdaptationStatus.Unresolved),
        };
    }

    private static RoomShapeMatch Resolve(
        SourceRoomPattern source,
        RoomShapeMatch match,
        PatternTargetRoomSnapshot snapshot,
        PatternTargetRoomChoice? choice,
        out List<string> lines)
    {
        var specs = source.Specs ?? [];
        var selected = Select(snapshot, choice, specs, out var inventedEntrance, out var bindings, out var rejected);
        var adaptation = SemanticPatternAdapter.Adapt(source, new SemanticPatternAdapter.Target
        {
            Loop = snapshot.Room.Loop,
            Holes = snapshot.Room.Holes ?? [],
            CentroidX = snapshot.Room.CentroidX,
            CentroidY = snapshot.Room.CentroidY,
            Openings = snapshot.Room.Openings,
            Projected = match.Devices,
            SelectedDoor = selected,
            Bindings = bindings,
            RejectedBindings = rejected,
            Quarters = match.RotationDegrees / 90,
            Mirror = match.Mirrored,
            OrientationTrusted = match.Band == "high",
        });
        var devices = adaptation.Placements.Select(placement =>
        {
            var unit = Unit(placement.X, placement.Y, snapshot.Room);
            return new ProjectedDevice(placement.Role, placement.PlacementKind, placement.X, placement.Y, unit.X, unit.Y, placement.FamilyHint);
        }).ToArray();
        lines = Explain(snapshot, choice, selected, inventedEntrance, adaptation);
        return match with { Devices = devices, AdaptationStatus = adaptation.Status };
    }

    private static PlanPoint? Select(
        PatternTargetRoomSnapshot snapshot,
        PatternTargetRoomChoice? choice,
        IReadOnlyList<PatternSpec> specs,
        out bool inventedEntrance,
        out Dictionary<int, SemanticPatternAdapter.EdgeSegment> bindings,
        out HashSet<int> rejected)
    {
        inventedEntrance = false;
        bindings = new Dictionary<int, SemanticPatternAdapter.EdgeSegment>();
        rejected = [];
        if (choice is null || choice.Assessment != "compatible")
        {
            return null;
        }

        PlanPoint? door = null;
        if (!string.IsNullOrEmpty(choice.EntranceFeatureKey))
        {
            var feature = snapshot.Doors.FirstOrDefault(item => item.Key == choice.EntranceFeatureKey);
            if (feature is null)
            {
                inventedEntrance = true;
            }
            else
            {
                door = feature.Point;
            }
        }

        foreach (var binding in choice.Bindings)
        {
            if (!binding.PatternKey.StartsWith(PatternTargetContext.PatternKeyPrefix, StringComparison.Ordinal)
                || !int.TryParse(binding.PatternKey[PatternTargetContext.PatternKeyPrefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                || index >= specs.Count)
            {
                continue;
            }

            var edge = snapshot.Edges.FirstOrDefault(item => item.Key == binding.EdgeKey);
            if (edge is null || specs[index].Anchor != PatternAnchors.SourceCorrespondence || specs[index].Placement != PatternPlacements.Boundary)
            {
                rejected.Add(index);
                continue;
            }

            var segment = new SemanticPatternAdapter.EdgeSegment(edge.Start, edge.End);
            if (bindings.TryGetValue(index, out var existing) && existing != segment)
            {
                rejected.Add(index);
                continue;
            }

            bindings[index] = segment;
        }

        foreach (var index in rejected)
        {
            bindings.Remove(index);
        }

        return door;
    }

    private static List<string> Explain(
        PatternTargetRoomSnapshot snapshot,
        PatternTargetRoomChoice? choice,
        PlanPoint? selected,
        bool inventedEntrance,
        SemanticPatternAdapter.Adaptation adaptation)
    {
        var lines = new List<string>();
        var needsEntrance = adaptation.Outcomes.Any(outcome => outcome.Spec.Anchor is PatternAnchors.Entrance or PatternAnchors.OppositeBoundary);
        if (needsEntrance)
        {
            if (choice is null || choice.Assessment is "uncertain" or "incompatible" || inventedEntrance || (selected is null && snapshot.Doors.Count != 1))
            {
                lines.Add(snapshot.Doors.Count > 1
                    ? snapshot.Doors.Count == 2
                        ? "Two possible entrance doors remain plausible"
                        : "Several possible entrance doors remain plausible"
                    : "No deterministic entrance door was established");
            }
            else if (selected is PlanPoint door)
            {
                var feature = snapshot.Doors.FirstOrDefault(item => item.Point.X == door.X && item.Point.Y == door.Y);
                var other = feature?.OtherSideStatus == "room" && !string.IsNullOrEmpty(feature.OtherSideName)
                    ? feature.OtherSideName
                    : feature?.OtherSideStatus == "outside" ? "the outside" : null;
                lines.Add(other is null ? "Entrance: selected door" : "Entrance: door connected to " + other);
            }
        }

        var resolved = adaptation.Outcomes.Where(outcome => outcome.Resolved).ToArray();
        var unresolved = adaptation.Outcomes.Where(outcome => !outcome.Resolved).ToArray();
        if (resolved.Length > 0)
        {
            lines.Add("Resolved");
            lines.AddRange(resolved.Select(outcome => "✓ " + PatternAdaptationText.Resolved(outcome, snapshot.Room.Loop)));
        }

        if (unresolved.Length > 0)
        {
            lines.Add("Unresolved");
            lines.AddRange(unresolved.Select(outcome => "! " + PatternAdaptationText.Unresolved(outcome)));
        }

        return lines.Take(12).ToList();
    }

    private static RoomPatternReview.Review Rebuild(
        RoomPatternReview.Review template,
        IReadOnlyList<RoomShapeMatch> matches,
        IReadOnlyList<RoomShapeMatch> shown,
        IReadOnlyDictionary<string, List<string>> notes,
        bool namedRooms)
    {
        template.CandidateCount = matches.Count;
        template.HighCount = matches.Count(match => match.Band == "high");
        template.LowerCount = matches.Count(match => match.Band == "lower");
        template.RejectedCount = matches.Count(match => match.Band == "rejected");
        template.Rooms = shown.Select(match => new RoomPatternReview.RoomCard
        {
            Label = match.Label,
            Level = match.LevelName,
            Band = match.Band,
            Score = match.Score,
            RotationDegrees = match.RotationDegrees,
            Mirrored = match.Mirrored,
            Reason = match.Reason,
            Outline = match.Outline.Select(point => new RoomPatternReview.Point { X = point.X, Y = point.Y }).ToList(),
            Devices = match.Devices.Select(device => new RoomPatternReview.DevicePoint
            {
                Role = device.Role,
                Placement = device.PlacementKind,
                X = device.NormalizedX,
                Y = device.NormalizedY,
            }).ToList(),
            Source = match.SourceKind,
            LinkLabel = match.LinkLabel,
            AdaptationStatus = match.AdaptationStatus,
            Reasoning = match.RoomRef is not null && notes.TryGetValue(match.RoomRef, out var lines) ? lines : null,
        }).ToList();
        template.SuggestedRooms = shown
            .Select((match, index) => (match, index))
            .Where(item => item.match.Band != "rejected"
                && !string.IsNullOrEmpty(item.match.RoomRef)
                && PatternAdaptationStatus.Selectable(item.match.AdaptationStatus)
                && (namedRooms || item.match.Band == "high"))
            .Select(item => item.index)
            .Take(8)
            .ToList();
        return template;
    }

    private static PlanPoint Unit(double x, double y, CandidateRoom room)
    {
        var minX = double.MaxValue;
        var minY = double.MaxValue;
        var maxX = double.MinValue;
        var maxY = double.MinValue;
        foreach (var point in room.Loop)
        {
            minX = Math.Min(minX, point.X - room.CentroidX);
            minY = Math.Min(minY, point.Y - room.CentroidY);
            maxX = Math.Max(maxX, point.X - room.CentroidX);
            maxY = Math.Max(maxY, point.Y - room.CentroidY);
        }

        var width = Math.Max(maxX - minX, 1e-6);
        var height = Math.Max(maxY - minY, 1e-6);
        return new PlanPoint(
            Math.Clamp((x - room.CentroidX - minX) / width, 0, 1),
            Math.Clamp((y - room.CentroidY - minY) / height, 0, 1));
    }
}

internal static class PatternAdaptationText
{
    public static string Resolved(SemanticPatternAdapter.SpecOutcome outcome, IReadOnlyList<PlanPoint> loop)
    {
        var spec = outcome.Spec;
        var subject = Subject(spec);
        return spec.Anchor switch
        {
            PatternAnchors.RoomCenter => spec.IsPair
                ? $"{spec.Count} {PatternUnderstanding.Noun(spec.Role, spec.Count)} around room centre"
                : $"{subject} at room centre",
            PatternAnchors.Entrance => subject + " at the entrance",
            PatternAnchors.OppositeBoundary => subject + " opposite the entrance",
            PatternAnchors.NearestBoundary => subject + " on the nearest wall",
            PatternAnchors.Corner => subject + " near a corner",
            PatternAnchors.SourceCorrespondence => subject + " → " + Wall(outcome, loop),
            _ => subject,
        };
    }

    public static string Unresolved(SemanticPatternAdapter.SpecOutcome outcome)
    {
        var spec = outcome.Spec;
        var where = spec.Anchor switch
        {
            PatternAnchors.RoomCenter => "at room centre",
            PatternAnchors.Entrance => "at the entrance",
            PatternAnchors.OppositeBoundary => "opposite the entrance",
            PatternAnchors.NearestBoundary => "on the nearest wall",
            PatternAnchors.Corner => "near a corner",
            PatternAnchors.SourceCorrespondence => "on its corresponding wall",
            _ => "in the room",
        };
        var reason = outcome.Failure switch
        {
            SemanticPatternAdapter.Failures.EntranceAmbiguous => "entrance is ambiguous",
            SemanticPatternAdapter.Failures.NoFacingWall => "no facing wall was found",
            SemanticPatternAdapter.Failures.NoCorrespondingWall => "no corresponding wall was chosen",
            SemanticPatternAdapter.Failures.IncompatibleBinding => "the proposed wall was not compatible",
            SemanticPatternAdapter.Failures.NoObservedDevice => "no observed device to anchor it",
            SemanticPatternAdapter.Failures.WallTooShort => "the wall is too short",
            SemanticPatternAdapter.Failures.UnsupportedArrangement => "this arrangement is not supported yet",
            _ => "no valid position inside the room",
        };
        return $"{Subject(spec)} {where}: {reason}";
    }

    private static string Subject(PatternSpec spec)
    {
        return spec.IsPair
            ? PatternUnderstanding.Noun(spec.Role, 1) + " pair"
            : $"{spec.Count} {PatternUnderstanding.Noun(spec.Role, spec.Count)}";
    }

    private static string Wall(SemanticPatternAdapter.SpecOutcome outcome, IReadOnlyList<PlanPoint> loop)
    {
        var side = outcome.Edge is SemanticPatternAdapter.EdgeSegment edge ? PatternEvidence.Compass(loop, edge.Start, edge.End) : null;
        return side is null ? "corresponding wall" : side + " wall";
    }
}

internal sealed record PatternTargetCompletion(
    int CandidateCount,
    string? RoomName,
    bool Unavailable,
    int Resolved,
    int Partial,
    int Unresolved);

internal static class PatternTargetCompletionText
{
    public static string Compose(PatternTargetCompletion completion)
    {
        ArgumentNullException.ThrowIfNull(completion);
        var count = completion.CandidateCount;
        var kind = completion.RoomName is null ? "" : completion.RoomName.Trim() + " ";
        var lines = new List<string>
        {
            $"BIM-IT found {count} candidate {kind}{(count == 1 ? "room" : "rooms")}.",
        };
        if (completion.Unavailable)
        {
            lines.Add("Target reasoning was unavailable, so Pattern Review shows the geometric comparison only.");
        }
        else
        {
            var clauses = new List<string>();
            if (completion.Resolved > 0)
            {
                clauses.Add($"completed for {completion.Resolved}");
            }

            if (completion.Partial > 0)
            {
                clauses.Add($"is partial for {completion.Partial}");
            }

            if (completion.Unresolved > 0)
            {
                clauses.Add($"is unresolved for {completion.Unresolved}");
            }

            lines.Add(clauses.Count == 0
                ? "No candidate room could be adapted."
                : "Target adaptation " + Join(clauses) + ".");
        }

        lines.Add("Review the Pattern Review panel for the proposed layouts.");
        lines.Add("No Revit changes were made.");
        return string.Join("\n", lines);
    }

    private static string Join(IReadOnlyList<string> clauses)
    {
        return clauses.Count switch
        {
            1 => clauses[0],
            2 => clauses[0] + " and " + clauses[1],
            _ => string.Join(", ", clauses.Take(clauses.Count - 1)) + ", and " + clauses[^1],
        };
    }
}

internal static class PatternTargetOrchestrator
{
    public const string UnavailableLine = "Target reasoning was unavailable (target_reasoning_unavailable).";

    public static async Task<PatternTargetCompletion?> CompleteAsync(RoomPatternStore store, IPatternTargetReasoner reasoner, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(reasoner);
        if (!store.TryCopyPending(out var pending))
        {
            return null;
        }

        PatternTargetReasoningResult result;
        try
        {
            result = await reasoner.ReasonAsync(pending.Request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (store.PendingMatches(pending))
            {
                store.MarkTargetReasoningUnavailable(pending.GenerationId, UnavailableLine);
            }

            throw;
        }
        catch (Exception)
        {
            result = PatternTargetReasoningResult.None;
        }

        if (!store.PendingMatches(pending))
        {
            return null;
        }

        var candidates = pending.Snapshot.Matches.Count;
        if (result.Unavailable)
        {
            return store.MarkTargetReasoningUnavailable(pending.GenerationId, UnavailableText(result.Failure))
                ? new PatternTargetCompletion(candidates, pending.Snapshot.RoomName, true, 0, 0, 0)
                : null;
        }

        var outcome = PatternTargetResolution.Apply(pending, result);
        return store.CompleteTargetReasoning(pending.GenerationId, outcome)
            ? new PatternTargetCompletion(candidates, pending.Snapshot.RoomName, false, outcome.Resolved, outcome.Partial, outcome.Unresolved)
            : null;
    }

    internal static string UnavailableText(string? failure)
    {
        return failure is PatternTargetReasoningResult.ProviderError or PatternTargetReasoningResult.UnreadableResponse
            ? "Target reasoning was unavailable (target_reasoning_unavailable: " + failure + ")."
            : UnavailableLine;
    }
}
