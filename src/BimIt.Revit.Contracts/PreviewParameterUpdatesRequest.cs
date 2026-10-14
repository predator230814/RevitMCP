using System.Text.Json.Serialization;

namespace BimIt.Revit.Contracts;

public sealed class PreviewParameterUpdatesRequest
{
    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    [JsonPropertyName("updates")]
    public required IReadOnlyList<PreviewParameterUpdate> Updates { get; init; }
}
