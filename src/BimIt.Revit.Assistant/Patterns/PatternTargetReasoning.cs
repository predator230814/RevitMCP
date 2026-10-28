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
    public IReadOnlyList<PatternTargetRuleContext> Rules { get; init; } = [];

    public IReadOnlyList<PatternTargetRoomContext> Rooms { get; init; } = [];
}

internal sealed class PatternTargetRuleContext
{
    public required string RuleKey { get; init; }

    public required string Role { get; init; }

    public required string Anchor { get; init; }

    public string? Basis { get; init; }

    public string? Group { get; init; }
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

internal sealed record PatternTargetEdgeBinding(string RuleKey, string EdgeKey);

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
        bool namedRooms)
    {
        var rules = source.Rules ?? [];
        if (rules.Count == 0)
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

        var ruleContexts = rules.Select((rule, index) => new PatternTargetRuleContext
        {
            RuleKey = "rule_" + index.ToString(CultureInfo.InvariantCulture),
            Role = rule.Role,
            Anchor = rule.Anchor,
            Basis = rule.Basis,
            Group = rule.Group,
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
            var perimeter = 0.0;
            for (var edgeIndex = 0; edgeIndex < loop.Count; edgeIndex++)
            {
                var start = loop[edgeIndex];
                var end = loop[(edgeIndex + 1) % loop.Count];
                var dx = end.X - start.X;
                var dy = end.Y - start.Y;
                perimeter += Math.Sqrt((dx * dx) + (dy * dy));
            }

            for (var edgeIndex = 0; edgeIndex < loop.Count; edgeIndex++)
            {
                var start = loop[edgeIndex];
                var end = loop[(edgeIndex + 1) % loop.Count];
                var key = "edge_" + edgeIndex.ToString(CultureInfo.InvariantCulture);
                var startUnit = Unit(start.X, start.Y, room);
                var endUnit = Unit(end.X, end.Y, room);
                var dx = end.X - start.X;
                var dy = end.Y - start.Y;
                var length = Math.Sqrt((dx * dx) + (dy * dy));
                edges.Add(new PatternTargetEdgeSnapshot { Key = key, Start = start, End = end });
                edgeContexts.Add(new PatternTargetEdgeContext
                {
                    Key = key,
                    StartX = startUnit.X,
                    StartY = startUnit.Y,
                    EndX = endUnit.X,
                    EndY = endUnit.Y,
                    RelativeLength = perimeter < 1e-6 ? 0 : Math.Round(length / perimeter, 3, MidpointRounding.AwayFromZero),
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
            Request = new PatternTargetReasoningRequest { Rules = ruleContexts, Rooms = contexts },
            Snapshot = new PatternTargetSnapshot
            {
                Source = source,
                NamedRooms = namedRooms,
                Matches = matches.ToArray(),
                Rooms = snapshots,
                Review = review,
            },
        };
    }

    public const string ResponseContract =
        """
        Compare the pattern rules with each candidate room. Door other_side tells which room a door connects to. A dwelling entrance usually connects to a corridor, hall, lobby, or the outside, not to a bathroom or closet.
        Reply with one JSON object and nothing else, in exactly this shape:
        {"rooms":[{"room_key":"room_0","assessment":"compatible","entrance_feature_key":"door_0","bindings":[{"rule_key":"rule_2","target_edge_key":"edge_3"}]}]}
        assessment is compatible, uncertain, or incompatible.
        entrance_feature_key is one door key of that room, or null when the entrance is not clear.
        bindings is optional. Use it only to name the target edge for a paired_symmetric rule. Otherwise use [].
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

    private static bool IsEligible(RoomShapeMatch match)
    {
        return match.Reason is null or "low_shape_similarity" or "aspect_mismatch" or "named_room" or "semantic_match";
    }

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
                if (binding.RuleKey is null || binding.TargetEdgeKey is null)
                {
                    return false;
                }

                bindings.Add(new PatternTargetEdgeBinding(binding.RuleKey, binding.TargetEdgeKey));
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
        public string? RuleKey { get; set; }

        public string? TargetEdgeKey { get; set; }
    }
}

internal sealed class PatternTargetOutcome
{
    public required RoomPatternReview.Review Review { get; init; }

    public required IReadOnlyList<RoomShapeMatch> Shown { get; init; }
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
        };
    }

    private static RoomShapeMatch Resolve(
        SourceRoomPattern source,
        RoomShapeMatch match,
        PatternTargetRoomSnapshot snapshot,
        PatternTargetRoomChoice? choice,
        out List<string> lines)
    {
        var rules = source.Rules ?? [];
        var selected = Select(snapshot, choice, out var inventedEntrance, out var edges);
        var adapted = SemanticPatternAdapter.TryPlace(
            rules,
            source.Devices,
            snapshot.Room.Loop,
            snapshot.Room.Holes ?? [],
            snapshot.Room.CentroidX,
            snapshot.Room.CentroidY,
            snapshot.Room.Openings,
            match.Devices,
            null,
            selected,
            edges,
            out var placements);
        var devices = placements.Select(placement =>
        {
            var unit = Unit(placement.X, placement.Y, snapshot.Room);
            return new ProjectedDevice(placement.Role, placement.PlacementKind, placement.X, placement.Y, unit.X, unit.Y, placement.FamilyHint);
        }).ToArray();
        var reason = match.Reason;
        var band = match.Band;
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

        lines = Explain(snapshot, choice, selected, inventedEntrance, devices, rules);
        return match with { Band = band, Reason = reason, Devices = devices };
    }

    private static PlanPoint? Select(
        PatternTargetRoomSnapshot snapshot,
        PatternTargetRoomChoice? choice,
        out bool inventedEntrance,
        out Dictionary<int, SemanticPatternAdapter.EdgeSegment> edges)
    {
        inventedEntrance = false;
        edges = new Dictionary<int, SemanticPatternAdapter.EdgeSegment>();
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
            if (!binding.RuleKey.StartsWith("rule_", StringComparison.Ordinal)
                || !int.TryParse(binding.RuleKey["rule_".Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var index))
            {
                continue;
            }

            var edge = snapshot.Edges.FirstOrDefault(item => item.Key == binding.EdgeKey);
            if (edge is null)
            {
                continue;
            }

            edges[index] = new SemanticPatternAdapter.EdgeSegment(edge.Start, edge.End);
        }

        return door;
    }

    private static List<string> Explain(
        PatternTargetRoomSnapshot snapshot,
        PatternTargetRoomChoice? choice,
        PlanPoint? selected,
        bool inventedEntrance,
        IReadOnlyList<ProjectedDevice> devices,
        IReadOnlyList<PatternRule> rules)
    {
        var lines = new List<string> { "Target reasoning" };
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

        if (devices.Any(device => device.Role == "receptacle"))
        {
            lines.Add("Pair mapped onto the target wall");
        }

        if (devices.Any(device => device.Role == "data"))
        {
            lines.Add("Data mapped opposite the entrance");
        }

        var resolved = ResolvedRoles(devices);
        var missing = rules
            .Select(rule => rule.Anchor == PatternAnchors.PairedSymmetric ? "receptacle pair" : rule.Role)
            .Where(role => role == "receptacle pair" ? !devices.Any(device => device.Role == "receptacle") : !devices.Any(device => device.Role == role))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (resolved.Length > 0)
        {
            lines.Add("Resolved: " + string.Join(", ", resolved));
        }

        if (missing.Length > 0)
        {
            lines.Add("Unresolved: " + string.Join(", ", missing));
        }

        return lines.Take(8).ToList();
    }

    private static string[] ResolvedRoles(IReadOnlyList<ProjectedDevice> devices)
    {
        var roles = new List<string>();
        if (devices.Any(device => device.Role == "switch"))
        {
            roles.Add("switch");
        }

        if (devices.Any(device => device.Role == "luminaire"))
        {
            roles.Add("luminaire");
        }

        if (devices.Count(device => device.Role == "receptacle") >= 2)
        {
            roles.Add("receptacle pair");
        }

        if (devices.Any(device => device.Role == "data"))
        {
            roles.Add("data");
        }

        return roles.ToArray();
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
            Reasoning = match.RoomRef is not null && notes.TryGetValue(match.RoomRef, out var lines) ? lines : null,
        }).ToList();
        template.SuggestedRooms = shown
            .Select((match, index) => (match, index))
            .Where(item => item.match.Band != "rejected"
                && !string.IsNullOrEmpty(item.match.RoomRef)
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

internal static class PatternTargetOrchestrator
{
    public const string UnavailableLine = "Target reasoning was unavailable (target_reasoning_unavailable).";

    public static async Task CompleteAsync(RoomPatternStore store, IPatternTargetReasoner reasoner, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(reasoner);
        if (!store.TryCopyPending(out var pending))
        {
            return;
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
            return;
        }

        if (result.Unavailable)
        {
            store.MarkTargetReasoningUnavailable(pending.GenerationId, UnavailableText(result.Failure));
            return;
        }

        var outcome = PatternTargetResolution.Apply(pending, result);
        store.CompleteTargetReasoning(pending.GenerationId, outcome);
    }

    internal static string UnavailableText(string? failure)
    {
        return failure is PatternTargetReasoningResult.ProviderError or PatternTargetReasoningResult.UnreadableResponse
            ? "Target reasoning was unavailable (target_reasoning_unavailable: " + failure + ")."
            : UnavailableLine;
    }
}
