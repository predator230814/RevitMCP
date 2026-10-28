using System.Text.Json.Serialization;

namespace BimIt.Revit.Contracts;

public sealed class ReadSpatialRoomsRequest
{
    [JsonPropertyName("document_id")]
    public string? DocumentId { get; init; }

    [JsonPropertyName("name_contains")]
    public string? NameContains { get; init; }

    [JsonPropertyName("limit")]
    public int? Limit { get; init; }
}

public sealed class SpatialLinkReport
{
    [JsonPropertyName("label")]
    public required string Label { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }
}

public sealed class SpatialRoomItem
{
    [JsonPropertyName("spatial_room_ref")]
    public required string SpatialRoomRef { get; init; }

    [JsonPropertyName("source")]
    public required string Source { get; init; }

    [JsonPropertyName("link_label")]
    public string? LinkLabel { get; init; }

    [JsonPropertyName("status")]
    public required RoomLayoutItemStatus Status { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("number")]
    public string? Number { get; init; }

    [JsonPropertyName("level_name")]
    public string? LevelName { get; init; }

    [JsonPropertyName("centroid_x_m")]
    public double? CentroidXMetres { get; init; }

    [JsonPropertyName("centroid_y_m")]
    public double? CentroidYMetres { get; init; }

    [JsonPropertyName("loops_truncated")]
    public bool? LoopsTruncated { get; init; }

    [JsonPropertyName("features_truncated")]
    public bool? FeaturesTruncated { get; init; }

    [JsonPropertyName("loops")]
    public IReadOnlyList<GetRoomLayoutLoop>? Loops { get; init; }

    [JsonPropertyName("features")]
    public IReadOnlyList<GetRoomLayoutFeature>? Features { get; init; }
}

public sealed class ReadSpatialRoomsResult
{
    [JsonPropertyName("context")]
    public required GetRoomLayoutsContext Context { get; init; }

    [JsonPropertyName("length_unit")]
    public required string LengthUnit { get; init; }

    [JsonPropertyName("chord_tolerance_m")]
    public required double ChordToleranceMetres { get; init; }

    [JsonPropertyName("truncated")]
    public required bool Truncated { get; init; }

    [JsonPropertyName("links")]
    public required IReadOnlyList<SpatialLinkReport> Links { get; init; }

    [JsonPropertyName("items")]
    public required IReadOnlyList<SpatialRoomItem> Items { get; init; }
}
