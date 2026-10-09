using System.Text.Json.Serialization;

namespace RevitMCP.Contracts;

public sealed class GetWarningsRequest
{
    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    [JsonPropertyName("severity")]
    public WarningSeverity? Severity { get; init; }

    [JsonPropertyName("failure_key")]
    public string? FailureKey { get; init; }

    [JsonPropertyName("element_refs")]
    public IReadOnlyList<string>? ElementRefs { get; init; }

    [JsonPropertyName("max_warnings")]
    public int? MaxWarnings { get; init; }

    [JsonPropertyName("max_elements_per_warning")]
    public int? MaxElementsPerWarning { get; init; }
}
