using System.Text.Json.Serialization;

namespace RevitMCP.Contracts;

public sealed class GetWarningsResult
{
    [JsonPropertyName("context")]
    public required GetWarningsContext Context { get; init; }

    [JsonPropertyName("matched_count")]
    public required int MatchedCount { get; init; }

    [JsonPropertyName("counts_by_severity")]
    public required GetWarningsCounts CountsBySeverity { get; init; }

    [JsonPropertyName("definitions")]
    public required IReadOnlyList<GetWarningsDefinition> Definitions { get; init; }

    [JsonPropertyName("warnings")]
    public required IReadOnlyList<GetWarningsItem> Warnings { get; init; }

    [JsonPropertyName("unmatched_element_refs")]
    public IReadOnlyList<string>? UnmatchedElementRefs { get; init; }

    [JsonPropertyName("truncated")]
    public required bool Truncated { get; init; }

    [JsonPropertyName("truncation_reasons")]
    public required IReadOnlyList<WarningTruncationReason> TruncationReasons { get; init; }
}
