using System.Text.Json;
using BimIt.Revit.Assistant.Ipc;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Assistant.Patterns;

internal static class RoomPatternGrounding
{
    public const int QueryLimit = 100;

    public static async Task<Grounding> ReadAsync(
        IAssistantToolSession session,
        string instanceId,
        SourceRoomPattern display,
        SourceRoomPattern geometry,
        string? roomName,
        CancellationToken cancellationToken)
    {
        var context = await session.CallAsync(
            AssistantTools.GetContext,
            new Dictionary<string, object?> { ["instance_id"] = instanceId },
            cancellationToken).ConfigureAwait(false);
        if (!TryDocumentId(context, out var documentId))
        {
            return Grounding.Failed(display, "read_failed");
        }

        var spatial = await session.CallAsync(
            AssistantTools.ReadSpatialRooms,
            new Dictionary<string, object?>
            {
                ["instance_id"] = instanceId,
                ["document_id"] = documentId,
                ["name_contains"] = roomName,
                ["limit"] = QueryLimit,
            },
            cancellationToken).ConfigureAwait(false);
        if (spatial.IsError || !TrySpatial(spatial.Structured, out var result))
        {
            return Grounding.Failed(display, "read_failed");
        }

        var candidates = new List<CandidateRoom>();
        var rejected = new List<RoomShapeMatch>();
        foreach (var item in result.Items)
        {
            if (TryCandidate(item, out var candidate))
            {
                candidates.Add(candidate);
            }
            else
            {
                rejected.Add(new RoomShapeMatch(
                    Label(item),
                    item.LevelName,
                    "rejected",
                    0,
                    0,
                    false,
                    StatusReason(item.Status),
                    [],
                    [],
                    null,
                    item.Source,
                    item.LinkLabel));
            }
        }

        var matched = PromoteNamedRooms(RoomShapeMatcher.Match(geometry, candidates), roomName);
        var all = matched.Concat(rejected).ToArray();
        var state = result.Items.Count == 0 ? "no_candidates" : "matched";
        var shown = ShownMatches(all, roomName is not null);
        return new Grounding(state, result.Truncated, all, BuildReview(display, state, result.Truncated, all, shown), ModelResult(display, state, all, roomName), shown, documentId, roomName);
    }

    private static RoomPatternReview.Review BuildReview(
        SourceRoomPattern pattern,
        string state,
        bool truncated,
        IReadOnlyList<RoomShapeMatch> matches,
        IReadOnlyList<RoomShapeMatch> shown)
    {
        return new RoomPatternReview.Review
        {
            PageNumber = 0,
            ReadState = state,
            CandidateCount = matches.Count,
            HighCount = matches.Count(match => match.Band == "high"),
            LowerCount = matches.Count(match => match.Band == "lower"),
            RejectedCount = matches.Count(match => match.Band == "rejected"),
            CandidatesTruncated = truncated,
            Boundary = pattern.Boundary.Select(Point).ToList(),
            Entrance = pattern.Entrance is PlanPoint entrance ? Point(entrance) : null,
            Devices = pattern.Devices.Select(device => new RoomPatternReview.DevicePoint
            {
                Role = device.Role,
                Placement = device.PlacementKind,
                X = device.X,
                Y = device.Y,
            }).ToList(),
            Rooms = shown.Select(Card).ToList(),
            Understood = PatternUnderstanding.Summarize(pattern.Rules, pattern.Anchors).ToList(),
        };
    }

    internal static IReadOnlyList<RoomShapeMatch> PromoteNamedRooms(IReadOnlyList<RoomShapeMatch> matches, string? roomName)
    {
        if (string.IsNullOrWhiteSpace(roomName))
        {
            return matches;
        }

        return matches.Select(match => match.Band == "rejected" && match.Reason is "low_shape_similarity" or "aspect_mismatch"
            ? match with { Band = "lower", Reason = "named_room" }
            : match).ToArray();
    }

    internal static IReadOnlyList<RoomShapeMatch> ShownMatches(IReadOnlyList<RoomShapeMatch> matches, bool namedRooms = false)
    {
        var ordered = matches
            .OrderBy(match => BandRank(match.Band))
            .ThenByDescending(match => match.Score)
            .ThenBy(match => match.Label, StringComparer.Ordinal)
            .ToArray();
        if (namedRooms)
        {
            return ordered.Where(match => match.Band != "rejected").Take(8)
                .Concat(ordered.Where(match => match.Band == "rejected").Take(2))
                .ToArray();
        }

        return ordered.Where(match => match.Band == "high").Take(4)
            .Concat(ordered.Where(match => match.Band == "lower").Take(2))
            .Concat(ordered.Where(match => match.Band == "rejected").Take(2))
            .ToArray();
    }

    private static IReadOnlyDictionary<string, object?> ModelResult(
        SourceRoomPattern pattern,
        string state,
        IReadOnlyList<RoomShapeMatch> matches,
        string? roomName)
    {
        return new Dictionary<string, object?>
        {
            ["stored"] = true,
            ["page_number"] = 0,
            ["device_count"] = pattern.Devices.Count,
            ["candidate_count"] = matches.Count,
            ["high_confidence_count"] = matches.Count(match => match.Band == "high"),
            ["lower_confidence_count"] = matches.Count(match => match.Band == "lower"),
            ["rejected_count"] = matches.Count(match => match.Band == "rejected"),
            ["mirrored_count"] = matches.Count(match => match.Band != "rejected" && match.Mirrored),
            ["rotated_count"] = matches.Count(match => match.Band != "rejected" && match.RotationDegrees != 0),
            ["match_state"] = state,
            ["room_name"] = roomName,
            ["understood"] = PatternUnderstanding.Summarize(pattern.Rules, pattern.Anchors),
            ["matches"] = matches
                .OrderBy(match => BandRank(match.Band))
                .ThenByDescending(match => match.Score)
                .Take(8)
                .Select(match => new Dictionary<string, object?>
                {
                    ["band"] = match.Band,
                    ["score"] = match.Score,
                    ["rotation_degrees"] = match.RotationDegrees,
                    ["mirrored"] = match.Mirrored,
                    ["reason"] = match.Reason,
                })
                .ToArray(),
        };
    }

    private static RoomPatternReview.RoomCard Card(RoomShapeMatch match)
    {
        return new RoomPatternReview.RoomCard
        {
            Label = match.Label,
            Level = match.LevelName,
            Band = match.Band,
            Score = match.Score,
            RotationDegrees = match.RotationDegrees,
            Mirrored = match.Mirrored,
            Reason = match.Reason,
            Outline = match.Outline.Select(Point).ToList(),
            Devices = match.Devices.Select(device => new RoomPatternReview.DevicePoint
            {
                Role = device.Role,
                Placement = device.PlacementKind,
                X = device.NormalizedX,
                Y = device.NormalizedY,
            }).ToList(),
            Source = match.SourceKind,
            LinkLabel = match.LinkLabel,
        };
    }

    private static RoomPatternReview.Point Point(PlanPoint point)
    {
        return new RoomPatternReview.Point { X = point.X, Y = point.Y };
    }

    private static int BandRank(string band) => band switch
    {
        "high" => 0,
        "lower" => 1,
        _ => 2,
    };

    private static string StatusReason(RoomLayoutItemStatus status) => status switch
    {
        RoomLayoutItemStatus.NotFound => "not_found",
        RoomLayoutItemStatus.NotARoom => "not_a_room",
        _ => "boundary_unavailable",
    };

    private static bool TryDocumentId(AssistantToolCall call, out string documentId)
    {
        documentId = "";
        if (call.IsError || call.Structured is not JsonElement json)
        {
            return false;
        }

        var context = json.Deserialize<GetContextResult>(ContractJson.Options);
        var id = context?.Document?.DocumentId;
        if (string.IsNullOrEmpty(id))
        {
            return false;
        }

        documentId = id;
        return true;
    }

    private static bool TrySpatial(JsonElement? structured, out ReadSpatialRoomsResult result)
    {
        result = null!;
        if (structured is not JsonElement json)
        {
            return false;
        }

        var parsed = json.Deserialize<ReadSpatialRoomsResult>(ContractJson.Options);
        if (parsed?.Items is null || parsed.Links is null || parsed.LengthUnit != GetRoomLayoutsResult.Metres)
        {
            return false;
        }

        result = parsed;
        return true;
    }

    private static bool TryCandidate(SpatialRoomItem item, out CandidateRoom candidate)
    {
        candidate = null!;
        if (item.Status != RoomLayoutItemStatus.Ok
            || item.Loops is null
            || item.Loops.Count == 0
            || item.CentroidXMetres is not double centroidX
            || item.CentroidYMetres is not double centroidY
            || item.Source is not ("host" or "link"))
        {
            return false;
        }

        var loops = item.Loops.Select(loop => (IReadOnlyList<PlanPoint>)loop.Vertices.Select(vertex => new PlanPoint(vertex.XMetres, vertex.YMetres)).ToArray()).ToArray();
        if (!BoundaryLoopSelector.TrySelect(loops, out var outer, out var holes, out var failure))
        {
            candidate = new CandidateRoom(
                item.Name,
                item.Number,
                item.LevelName,
                centroidX,
                centroidY,
                [],
                item.Loops[0].VerticesTruncated,
                [],
                RoomRef(item),
                holes,
                item.Source,
                item.LinkLabel,
                failure);
            return true;
        }

        var openings = (item.Features ?? [])
            .Select(feature => new CandidateOpening(feature.XMetres, feature.YMetres, feature.Kind == RoomFeatureKind.Door ? "door" : "window"))
            .ToArray();
        candidate = new CandidateRoom(
            item.Name,
            item.Number,
            item.LevelName,
            centroidX,
            centroidY,
            outer,
            item.LoopsTruncated == true,
            openings,
            RoomRef(item),
            holes,
            item.Source,
            item.LinkLabel);
        return true;
    }

    private static string? RoomRef(SpatialRoomItem item)
    {
        if (item.Source == "host" && SpatialRoomRefCodec.TryHost(item.SpatialRoomRef, out var uniqueId))
        {
            return uniqueId;
        }

        return item.SpatialRoomRef;
    }

    private static string Label(SpatialRoomItem item)
    {
        var name = string.IsNullOrWhiteSpace(item.Name) ? null : item.Name.Trim();
        var number = string.IsNullOrWhiteSpace(item.Number) ? null : item.Number.Trim();
        var label = name is null ? number : number is null ? name : name + " " + number;
        return string.IsNullOrWhiteSpace(label) ? "Room" : label;
    }
}

internal sealed record Grounding(
    string State,
    bool CandidatesTruncated,
    IReadOnlyList<RoomShapeMatch> Matches,
    RoomPatternReview.Review Review,
    IReadOnlyDictionary<string, object?> Model,
    IReadOnlyList<RoomShapeMatch> Shown,
    string? DocumentId = null,
    string? RoomName = null)
{
    public static Grounding Failed(SourceRoomPattern pattern, string state)
    {
        var review = new RoomPatternReview.Review
        {
            ReadState = state,
            Boundary = pattern.Boundary.Select(point => new RoomPatternReview.Point { X = point.X, Y = point.Y }).ToList(),
            Entrance = pattern.Entrance is PlanPoint entrance ? new RoomPatternReview.Point { X = entrance.X, Y = entrance.Y } : null,
            Devices = pattern.Devices.Select(device => new RoomPatternReview.DevicePoint
            {
                Role = device.Role,
                Placement = device.PlacementKind,
                X = device.X,
                Y = device.Y,
            }).ToList(),
            Rooms = [],
        };
        var model = new Dictionary<string, object?>
        {
            ["stored"] = true,
            ["device_count"] = pattern.Devices.Count,
            ["candidate_count"] = 0,
            ["high_confidence_count"] = 0,
            ["lower_confidence_count"] = 0,
            ["rejected_count"] = 0,
            ["mirrored_count"] = 0,
            ["rotated_count"] = 0,
            ["match_state"] = state,
            ["matches"] = Array.Empty<object>(),
        };
        return new Grounding(state, false, [], review, model, []);
    }
}
