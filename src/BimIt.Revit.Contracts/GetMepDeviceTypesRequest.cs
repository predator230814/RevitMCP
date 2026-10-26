using System.Text.Json.Serialization;

namespace BimIt.Revit.Contracts;

public sealed class GetMepDeviceTypesRequest
{
    [JsonPropertyName("document_id")]
    public string? DocumentId { get; init; }

    [JsonPropertyName("category_tokens")]
    public IReadOnlyList<string>? CategoryTokens { get; init; }

    [JsonPropertyName("search")]
    public string? Search { get; init; }

    [JsonPropertyName("limit")]
    public int? Limit { get; init; }
}
