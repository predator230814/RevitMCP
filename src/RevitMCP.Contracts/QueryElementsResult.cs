using System.Text.Json.Serialization;

namespace RevitMCP.Contracts;

public sealed class QueryElementsResult
{
    [JsonPropertyName("context")]
    public required QueryElementsContext Context { get; init; }

    [JsonPropertyName("matched_count")]
    public required int MatchedCount { get; init; }

    [JsonPropertyName("truncated")]
    public required bool Truncated { get; init; }

    [JsonPropertyName("element_refs")]
    public required IReadOnlyList<string> ElementRefs { get; init; }
}
