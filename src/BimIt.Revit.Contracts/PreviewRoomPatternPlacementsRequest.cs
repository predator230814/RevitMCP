using System.Text.Json.Serialization;

namespace BimIt.Revit.Contracts;

public sealed class PreviewRoomPatternPlacementsRequest
{
    [JsonPropertyName("document_id")]
    public string? DocumentId { get; init; }

    [JsonPropertyName("placements")]
    public IReadOnlyList<PreviewRoomPatternPlacement>? Placements { get; init; }
}

public sealed class PreviewRoomPatternPlacement
{
    [JsonPropertyName("room_ref")]
    public string? RoomRef { get; init; }

    [JsonPropertyName("family_type_ref")]
    public string? FamilyTypeRef { get; init; }

    [JsonPropertyName("role")]
    public string? Role { get; init; }

    [JsonPropertyName("placement_kind")]
    public string? PlacementKind { get; init; }

    [JsonPropertyName("x_m")]
    public double XMetres { get; init; }

    [JsonPropertyName("y_m")]
    public double YMetres { get; init; }

    [JsonPropertyName("mounting_height_m")]
    public double MountingHeightMetres { get; init; }

    [JsonPropertyName("quarter_turns")]
    public int QuarterTurns { get; init; }
}
