using System.Text.Json.Serialization;

namespace RevitMCP.Contracts;

public sealed class GetElementsRequest
{
    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    [JsonPropertyName("element_refs")]
    public required IReadOnlyList<string> ElementRefs { get; init; }

    [JsonPropertyName("projection")]
    public required GetElementsProjection Projection { get; init; }
}
