using System.Text.Json.Serialization;

namespace BimIt.Revit.Contracts;

public sealed class PreviewMepDevicePlacementItem
{
    [JsonPropertyName("offset_x")]
    public required double OffsetX { get; init; }

    [JsonPropertyName("offset_y")]
    public required double OffsetY { get; init; }

    [JsonPropertyName("vertical_offset")]
    public required double VerticalOffset { get; init; }

    [JsonPropertyName("status")]
    public required PreviewMepDevicePlacementItemStatus Status { get; init; }
}
