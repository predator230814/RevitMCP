using System.Text.Json.Serialization;

namespace RevitMCP.Contracts;

public sealed class DescribeParametersRequest
{
    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    [JsonPropertyName("element_refs")]
    public required IReadOnlyList<string> ElementRefs { get; init; }

    [JsonPropertyName("source")]
    public DescribeParameterSource Source { get; init; } = DescribeParameterSource.Both;

    [JsonPropertyName("name_contains")]
    public string? NameContains { get; init; }

    [JsonPropertyName("limit")]
    public int Limit { get; init; } = 50;
}
