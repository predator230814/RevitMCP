using System.Text.Json.Serialization;

namespace BimIt.Revit.Contracts;

public sealed class GetElementsContext
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }
}
