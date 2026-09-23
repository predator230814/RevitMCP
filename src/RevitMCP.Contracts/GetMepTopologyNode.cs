using System.Text.Json.Serialization;

namespace RevitMCP.Contracts;

public sealed class GetMepTopologyNode
{
    [JsonPropertyName("element_ref")]
    public required string ElementRef { get; init; }

    [JsonPropertyName("depth")]
    public required int Depth { get; init; }
}
