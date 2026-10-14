using System.Text.Json.Serialization;

namespace BimIt.Revit.Contracts;

public sealed class GetParameterValuesRequest
{
    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    [JsonPropertyName("reads")]
    public required IReadOnlyList<GetParameterValueRead> Reads { get; init; }
}
