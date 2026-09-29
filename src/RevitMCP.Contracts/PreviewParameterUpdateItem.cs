using System.Text.Json.Serialization;

namespace RevitMCP.Contracts;

public sealed class PreviewParameterUpdateItem
{
    [JsonPropertyName("element_ref")]
    public required string ElementRef { get; init; }

    [JsonPropertyName("parameter_ref")]
    public required string ParameterRef { get; init; }

    [JsonPropertyName("status")]
    public required PreviewParameterUpdateStatus Status { get; init; }

    [JsonPropertyName("element_name")]
    public string? ElementName { get; init; }

    [JsonPropertyName("element_name_truncated")]
    public bool? ElementNameTruncated { get; init; }

    [JsonPropertyName("category_name")]
    public string? CategoryName { get; init; }

    [JsonPropertyName("category_name_truncated")]
    public bool? CategoryNameTruncated { get; init; }

    [JsonPropertyName("parameter_name")]
    public string? ParameterName { get; init; }

    [JsonPropertyName("parameter_name_truncated")]
    public bool? ParameterNameTruncated { get; init; }

    [JsonPropertyName("data_type")]
    public DescribeParameterDataType? DataType { get; init; }

    [JsonPropertyName("before")]
    public PreviewParameterBefore? Before { get; init; }

    [JsonPropertyName("proposed")]
    public PreviewParameterValue? Proposed { get; init; }
}
