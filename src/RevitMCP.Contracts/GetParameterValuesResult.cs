using System.Text.Json.Serialization;

namespace RevitMCP.Contracts;

public sealed class GetParameterValuesResult
{
    [JsonPropertyName("context")]
    public required DescribeParametersContext Context { get; init; }

    [JsonPropertyName("items")]
    public required IReadOnlyList<GetParameterValueItem> Items { get; init; }
}
