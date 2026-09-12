using System.Text.Json.Serialization;

namespace RevitMCP.Contracts;

public sealed class DescribeParametersResult
{
    [JsonPropertyName("context")]
    public required DescribeParametersContext Context { get; init; }

    [JsonPropertyName("elements")]
    public required IReadOnlyList<DescribeParameterElementResult> Elements { get; init; }

    [JsonPropertyName("matched_count")]
    public required int MatchedCount { get; init; }

    [JsonPropertyName("truncated")]
    public required bool Truncated { get; init; }

    [JsonPropertyName("parameters")]
    public required IReadOnlyList<DescribeParameterDescriptor> Parameters { get; init; }
}
