using System.Text.Json.Serialization;

namespace BimIt.Revit.Contracts;

public sealed class PreviewMepDevicePlacement
{
    [JsonPropertyName("offset_along_host")]
    public required double OffsetAlongHost { get; init; }

    [JsonPropertyName("vertical_offset")]
    public required double VerticalOffset { get; init; }
}
