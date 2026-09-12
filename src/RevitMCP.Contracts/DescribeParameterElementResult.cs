using System.Text.Json.Serialization;

namespace RevitMCP.Contracts;

public sealed class DescribeParameterElementResult
{
    [JsonPropertyName("element_ref")]
    public required string ElementRef { get; init; }

    [JsonPropertyName("status")]
    public required GetElementResultStatus Status { get; init; }
}
