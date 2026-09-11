using System.Text.Json.Serialization;

namespace RevitMCP.Contracts;

public sealed class GetElementsResult
{
    [JsonPropertyName("context")]
    public required GetElementsContext Context { get; init; }

    [JsonPropertyName("elements")]
    public required IReadOnlyList<GetElementResult> Elements { get; init; }
}
