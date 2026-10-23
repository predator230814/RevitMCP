using System.Text.Json;
using System.Text.Json.Serialization;

namespace BimIt.Revit.Assistant.Ipc;

public static class RoomPatternReview
{
    public const int MaxCharacters = 120_000;

    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static bool TryParse(string? json, out JsonElement review)
    {
        review = default;
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxCharacters || !IsClosed(json))
        {
            return false;
        }

        try
        {
            var body = JsonSerializer.Deserialize<Review>(json, Options);
            if (body is null || !Accept(body))
            {
                return false;
            }

            review = JsonSerializer.SerializeToElement(body, Options);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static string? Serialize(Review review)
    {
        ArgumentNullException.ThrowIfNull(review);
        var json = JsonSerializer.Serialize(review, Options);
        return TryParse(json, out _) ? json : null;
    }

    private static bool IsClosed(string json)
    {
        return !json.Contains("element_ref", StringComparison.Ordinal)
            && !json.Contains("elementRef", StringComparison.Ordinal)
            && !json.Contains("instance_id", StringComparison.Ordinal)
            && !json.Contains("instanceId", StringComparison.Ordinal)
            && !json.Contains("document_id", StringComparison.Ordinal)
            && !json.Contains("documentId", StringComparison.Ordinal)
            && !json.Contains("uniqueId", StringComparison.Ordinal)
            && !json.Contains("family_type_ref", StringComparison.Ordinal)
            && !json.Contains("familyTypeRef", StringComparison.Ordinal)
            && !json.Contains("room_ref", StringComparison.Ordinal)
            && !json.Contains("roomRef", StringComparison.Ordinal)
            && !json.Contains(":\\", StringComparison.Ordinal)
            && !json.Contains("://", StringComparison.Ordinal)
            && !json.Contains("\\\\", StringComparison.Ordinal);
    }

    private static bool Accept(Review review)
    {
        if (review.PageNumber < 1
            || review.CandidateCount < 0
            || review.HighCount < 0
            || review.LowerCount < 0
            || review.RejectedCount < 0
            || review.Boundary is null
            || review.Boundary.Count is < 3 or > 32
            || review.Devices is null
            || review.Devices.Count is < 1 or > 20
            || review.Rooms is null
            || review.Rooms.Count > 8)
        {
            return false;
        }

        if (review.ReadState is not ("matched" or "no_candidates" or "read_failed"))
        {
            return false;
        }

        if (review.ImageBase64 is { Length: > 70_000 } || !IsImage(review))
        {
            return false;
        }

        if (!review.Boundary.All(IsUnit) || (review.Entrance is not null && !IsUnit(review.Entrance)))
        {
            return false;
        }

        if (review.Devices.Any(device => !IsRole(device.Role) || !IsPlacement(device.Placement) || !IsUnit(device)))
        {
            return false;
        }

        if (review.Generation is not null && !IsGeneration(review.Generation))
        {
            return false;
        }

        if (review.SuggestedRooms is not null && !IsSuggested(review.SuggestedRooms))
        {
            return false;
        }

        return review.Rooms.All(IsRoom) && (review.Roles is null || review.Roles.All(IsRoleCard));
    }

    private static bool IsGeneration(string value)
    {
        return value.Length == 32 && value.All(static character => char.IsAsciiHexDigit(character));
    }

    private static bool IsSuggested(List<int> rooms)
    {
        return rooms.Count <= 8 && rooms.Distinct().Count() == rooms.Count && rooms.All(index => index is >= 0 and <= 7);
    }

    private static bool IsRoleCard(RoleCard card)
    {
        if (!IsRole(card.Role) || !IsLabel(card.FamilyHint) || !IsClassification(card.PlacementClassification))
        {
            return false;
        }

        if (card.Options is null || card.Options.Count > 8 || card.Options.Any(option => !IsOption(option)))
        {
            return false;
        }

        return card.SuggestedOption is null || card.Options.Any(option => option.Key == card.SuggestedOption);
    }

    private static bool IsOption(TypeOption option)
    {
        return IsOptionKey(option.Key)
            && IsLabel(option.FamilyName)
            && IsLabel(option.TypeName)
            && IsLabel(option.CategoryName)
            && IsClassification(option.PlacementClassification);
    }

    private static bool IsOptionKey(string? key)
    {
        return key is not null
            && key.Length is >= 2 and <= 4
            && key[0] == 't'
            && key.Skip(1).All(static character => char.IsDigit(character));
    }

    private static bool IsClassification(string? value)
    {
        return value is null or "" or "one_level_based" or "one_level_based_hosted" or "work_plane_based";
    }

    private static bool IsImage(Review review)
    {
        var hasMime = review.ImageMime is not null;
        var hasBytes = review.ImageBase64 is not null;
        if (hasMime != hasBytes)
        {
            return false;
        }

        return review.ImageMime is null || review.ImageMime is "image/jpeg" or "image/png";
    }

    private static bool IsRoom(RoomCard room)
    {
        return IsLabel(room.Label)
            && IsLabel(room.Level)
            && room.Band is "high" or "lower" or "rejected"
            && room.Score is >= 0 and <= 1
            && room.RotationDegrees is 0 or 90 or 180 or 270
            && IsReason(room.Reason)
            && room.Outline is { Count: <= 24 }
            && room.Outline.All(IsUnit)
            && room.Devices is { Count: <= 20 }
            && room.Devices.All(device => IsRole(device.Role) && IsPlacement(device.Placement) && IsUnit(device));
    }

    private static bool IsReason(string? reason)
    {
        return reason is null
            or "multiple_boundary_loops"
            or "boundary_too_complex"
            or "degenerate_boundary"
            or "self_intersecting_boundary"
            or "non_orthogonal_boundary"
            or "aspect_mismatch"
            or "low_shape_similarity"
            or "not_found"
            or "not_a_room"
            or "boundary_unavailable";
    }

    private static bool IsRole(string? role)
    {
        return role is "receptacle" or "switch" or "luminaire" or "smoke_detector" or "thermostat" or "data" or "other";
    }

    private static bool IsPlacement(string? placement) => placement is "boundary" or "interior";

    private static bool IsUnit(Point point)
    {
        return point is not null && point.X is >= 0 and <= 1 && point.Y is >= 0 and <= 1;
    }

    private static bool IsUnit(DevicePoint point)
    {
        return point.X is >= 0 and <= 1 && point.Y is >= 0 and <= 1;
    }

    private static bool IsLabel(string? value)
    {
        return value is null || (value.Length <= 120 && !value.Any(char.IsControl));
    }

    public sealed class Review
    {
        public int PageNumber { get; set; }

        public string? ImageMime { get; set; }

        public string? ImageBase64 { get; set; }

        public List<Point>? Boundary { get; set; }

        public Point? Entrance { get; set; }

        public List<DevicePoint>? Devices { get; set; }

        public int CandidateCount { get; set; }

        public int HighCount { get; set; }

        public int LowerCount { get; set; }

        public int RejectedCount { get; set; }

        public bool CandidatesTruncated { get; set; }

        public string? ReadState { get; set; }

        public string? Generation { get; set; }

        public List<int>? SuggestedRooms { get; set; }

        public List<RoleCard>? Roles { get; set; }

        public List<RoomCard>? Rooms { get; set; }
    }

    public sealed class RoleCard
    {
        public string? Role { get; set; }

        public string? FamilyHint { get; set; }

        public string? SuggestedOption { get; set; }

        public string? PlacementClassification { get; set; }

        public bool Ambiguous { get; set; }

        public List<TypeOption>? Options { get; set; }
    }

    public sealed class TypeOption
    {
        public string? Key { get; set; }

        public string? FamilyName { get; set; }

        public string? TypeName { get; set; }

        public string? CategoryName { get; set; }

        public string? PlacementClassification { get; set; }
    }

    public sealed class Point
    {
        public double X { get; set; }

        public double Y { get; set; }
    }

    public sealed class DevicePoint
    {
        public string? Role { get; set; }

        public string? Placement { get; set; }

        public double X { get; set; }

        public double Y { get; set; }
    }

    public sealed class RoomCard
    {
        public string? Label { get; set; }

        public string? Level { get; set; }

        public string? Band { get; set; }

        public double Score { get; set; }

        public int RotationDegrees { get; set; }

        public bool Mirrored { get; set; }

        public string? Reason { get; set; }

        public List<Point>? Outline { get; set; }

        public List<DevicePoint>? Devices { get; set; }
    }
}
