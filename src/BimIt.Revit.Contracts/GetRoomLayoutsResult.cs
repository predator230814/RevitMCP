using System.Text.Json.Serialization;

namespace BimIt.Revit.Contracts;

public enum RoomLayoutItemStatus
{
    Ok,
    NotFound,
    NotARoom,
    BoundaryUnavailable,
}

public enum RoomFeatureKind
{
    Door,
    Window,
}

public sealed class GetRoomLayoutsContext
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }
}

public sealed class GetRoomLayoutVertex
{
    [JsonPropertyName("x_m")]
    public required double XMetres { get; init; }

    [JsonPropertyName("y_m")]
    public required double YMetres { get; init; }
}

public sealed class GetRoomLayoutLoop
{
    [JsonPropertyName("vertices_truncated")]
    public required bool VerticesTruncated { get; init; }

    [JsonPropertyName("vertices")]
    public required IReadOnlyList<GetRoomLayoutVertex> Vertices { get; init; }
}

public sealed class GetRoomLayoutFeature
{
    [JsonPropertyName("kind")]
    public required RoomFeatureKind Kind { get; init; }

    [JsonPropertyName("x_m")]
    public required double XMetres { get; init; }

    [JsonPropertyName("y_m")]
    public required double YMetres { get; init; }

    [JsonPropertyName("facing_x")]
    public double? FacingX { get; init; }

    [JsonPropertyName("facing_y")]
    public double? FacingY { get; init; }

    [JsonPropertyName("width_m")]
    public double? WidthMetres { get; init; }
}

public sealed class GetRoomLayoutItem
{
    [JsonPropertyName("element_ref")]
    public required string ElementRef { get; init; }

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

public sealed class GetRoomLayoutsResult
{
    public const string Metres = "m";

    [JsonPropertyName("context")]
    public required GetRoomLayoutsContext Context { get; init; }

    [JsonPropertyName("length_unit")]
    public required string LengthUnit { get; init; }

    [JsonPropertyName("chord_tolerance_m")]
    public required double ChordToleranceMetres { get; init; }

    [JsonPropertyName("opening_scan_truncated")]
    public required bool OpeningScanTruncated { get; init; }

    [JsonPropertyName("items")]
    public required IReadOnlyList<GetRoomLayoutItem> Items { get; init; }
}
