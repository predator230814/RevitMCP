using System.Text.Json.Serialization;

namespace BimIt.Revit.Contracts;

public sealed class PreviewMepDevicePlacementsRequest
{
    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    [JsonPropertyName("template_element_ref")]
    public required string TemplateElementRef { get; init; }

    [JsonPropertyName("length_unit_id")]
    public required string LengthUnitId { get; init; }

    [JsonPropertyName("placements")]
    public required IReadOnlyList<PreviewMepDevicePlacement> Placements { get; init; }
}
