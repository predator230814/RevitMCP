using System.Text.Json.Serialization;

namespace BimIt.Revit.Contracts;

public sealed class GetSelectionContext
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }
}
