using System.Text.Json.Serialization;

namespace RevitMCP.Contracts;

public sealed class GetContextActiveView
{
    [JsonPropertyName("element_id")]
    public required string ElementId { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("view_type")]
    public required string ViewType { get; init; }
}
