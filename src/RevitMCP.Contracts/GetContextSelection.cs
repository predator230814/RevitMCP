using System.Text.Json.Serialization;

namespace RevitMCP.Contracts;

public sealed class GetContextSelection
{
    [JsonPropertyName("count")]
    public required int Count { get; init; }
}
