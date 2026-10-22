using System.Text.Json.Serialization;

namespace BimIt.Revit.Contracts;

public sealed class GetRoomLayoutsRequest
{
    [JsonPropertyName("document_id")]
    public string? DocumentId { get; init; }

    [JsonPropertyName("element_refs")]
    public IReadOnlyList<string>? ElementRefs { get; init; }
}
