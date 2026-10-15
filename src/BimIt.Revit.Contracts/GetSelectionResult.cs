using System.Text.Json.Serialization;

namespace BimIt.Revit.Contracts;

public sealed class GetSelectionResult
{
    [JsonPropertyName("context")]
    public required GetSelectionContext Context { get; init; }

    [JsonPropertyName("selected_count")]
    public required int SelectedCount { get; init; }

    [JsonPropertyName("truncated")]
    public required bool Truncated { get; init; }

    [JsonPropertyName("element_refs")]
    public required IReadOnlyList<string> ElementRefs { get; init; }
}
