using System.Text.Json.Serialization;

namespace RevitMCP.Contracts;

public sealed class QueryElementsContext
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }
}
