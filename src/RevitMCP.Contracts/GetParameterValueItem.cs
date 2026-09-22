using System.Text.Json.Serialization;

namespace RevitMCP.Contracts;

public sealed class GetParameterValueItem
{
    [JsonPropertyName("element_ref")]
    public required string ElementRef { get; init; }

    [JsonPropertyName("parameter_ref")]
    public required string ParameterRef { get; init; }

    [JsonPropertyName("status")]
    public required GetParameterValueStatus Status { get; init; }

    [JsonPropertyName("data_type")]
    public DescribeParameterDataType? DataType { get; init; }

    [JsonPropertyName("has_value")]
    public bool? HasValue { get; init; }

    [JsonPropertyName("value")]
    public GetParameterValue? Value { get; init; }
}
