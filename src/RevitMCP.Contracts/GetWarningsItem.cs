using System.Text.Json.Serialization;

namespace RevitMCP.Contracts;

public sealed class GetWarningsItem
{
    [JsonPropertyName("failure_key")]
    public required string FailureKey { get; init; }

    [JsonPropertyName("severity")]
    public required WarningSeverity Severity { get; init; }

    [JsonPropertyName("description_text")]
    public required string DescriptionText { get; init; }

    [JsonPropertyName("description_truncated")]
    public required bool DescriptionTruncated { get; init; }

    [JsonPropertyName("has_resolutions")]
    public required bool HasResolutions { get; init; }

    [JsonPropertyName("elements")]
    public required IReadOnlyList<GetWarningsElement> Elements { get; init; }

    [JsonPropertyName("elements_truncated")]
    public required bool ElementsTruncated { get; init; }

    [JsonPropertyName("unresolved_element_count")]
    public required int UnresolvedElementCount { get; init; }
}
