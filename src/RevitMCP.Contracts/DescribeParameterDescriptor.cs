using System.Text.Json.Serialization;

namespace RevitMCP.Contracts;

public sealed class DescribeParameterDescriptor
{
    [JsonPropertyName("parameter_ref")]
    public required string ParameterRef { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("source")]
    public required GetElementParameterSource Source { get; init; }

    [JsonPropertyName("identity")]
    public required DescribeParameterIdentity Identity { get; init; }

    [JsonPropertyName("data_type")]
    public required DescribeParameterDataType DataType { get; init; }

    [JsonPropertyName("present_on_count")]
    public required int PresentOnCount { get; init; }

    [JsonPropertyName("read_only_on_count")]
    public required int ReadOnlyOnCount { get; init; }
}
