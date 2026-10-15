using System.Text.Json.Serialization;

namespace BimIt.Revit.Contracts;

public sealed class GetSelectionRequest
{
    [JsonPropertyName("document_id")]
    public string? DocumentId { get; init; }

    [JsonPropertyName("limit")]
    public int? Limit { get; init; }
}
