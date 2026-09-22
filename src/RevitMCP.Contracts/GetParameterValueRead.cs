using System.Text.Json.Serialization;

namespace RevitMCP.Contracts;

public sealed class GetParameterValueRead
{
    [JsonPropertyName("element_ref")]
    public required string ElementRef { get; init; }

    [JsonPropertyName("parameter_ref")]
    public required string ParameterRef { get; init; }
}
