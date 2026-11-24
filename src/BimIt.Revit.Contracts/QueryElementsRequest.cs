using System.Text.Json.Serialization;

namespace BimIt.Revit.Contracts;

public sealed class QueryElementsRequest
{
    [JsonPropertyName("document_id")]
    public string? DocumentId { get; init; }

    [JsonPropertyName("scope")]
    public required QueryScope Scope { get; init; }

    [JsonPropertyName("filters")]
    public required QueryElementFilters Filters { get; init; }

    [JsonPropertyName("predicates")]
    public IReadOnlyList<QueryElementPredicate>? Predicates { get; init; }

    [JsonPropertyName("limit")]
    public required int Limit { get; init; }

    [JsonIgnore]
    public bool HasPredicates => Predicates is { Count: > 0 };
}
