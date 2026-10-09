using System.Text.Json.Serialization;

namespace RevitMCP.Contracts;

public sealed class GetWarningsDefinition
{
    [JsonPropertyName("failure_key")]
    public required string FailureKey { get; init; }

    [JsonPropertyName("severity")]
    public required WarningSeverity Severity { get; init; }

    [JsonPropertyName("matched_count")]
    public required int MatchedCount { get; init; }
}
