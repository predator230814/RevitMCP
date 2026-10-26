using System.Text.Json.Serialization;

namespace BimIt.Revit.Contracts;

public enum PreviewRoomPatternItemStatus
{
    Ok,
    RoomNotFound,
    TypeNotFound,
    CategoryMismatch,
    DocumentMismatch,
    UnsupportedPlacement,
    OutsideRoom,
    AmbiguousWall,
    CurvedWall,
    NoWall,
    UnsupportedFace,
    Occupied,
}

public sealed class PreviewRoomPatternPlacementsContext
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }
}

public sealed class PreviewRoomPatternItemResult
{
    [JsonPropertyName("position")]
    public required int Position { get; init; }

    [JsonPropertyName("status")]
    public required PreviewRoomPatternItemStatus Status { get; init; }
}

public sealed class PreviewRoomPatternPlacementsResult
{
    public const string Metres = "m";

    [JsonPropertyName("context")]
    public required PreviewRoomPatternPlacementsContext Context { get; init; }

    [JsonPropertyName("length_unit")]
    public required string LengthUnit { get; init; }

    [JsonPropertyName("ready")]
    public required bool Ready { get; init; }

    [JsonPropertyName("room_count")]
    public required int RoomCount { get; init; }

    [JsonPropertyName("items")]
    public required IReadOnlyList<PreviewRoomPatternItemResult> Items { get; init; }

    [JsonPropertyName("intent_ref")]
    public string? IntentRef { get; init; }

    [JsonPropertyName("intent_fingerprint")]
    public string? IntentFingerprint { get; init; }

    [JsonPropertyName("expires_at")]
    [JsonConverter(typeof(PreviewExpiresAtJsonConverter))]
    public DateTimeOffset? ExpiresAt { get; init; }
}
