using System.Text.Json.Serialization;

namespace BimIt.Revit.Contracts;

public sealed class ApplyMepDevicePlacementsResult
{
    [JsonPropertyName("status")]
    public required ApplyMepDevicePlacementsStatus Status { get; init; }

    [JsonPropertyName("created_count")]
    public int? CreatedCount { get; init; }

    [JsonPropertyName("created_element_refs")]
    public IReadOnlyList<string>? CreatedElementRefs { get; init; }
}
