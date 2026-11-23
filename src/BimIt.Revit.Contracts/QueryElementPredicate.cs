using System.Text.Json.Serialization;

namespace BimIt.Revit.Contracts;

public sealed class QueryElementPredicate
{
    [JsonPropertyName("source")]
    public string? Source { get; init; }

    [JsonPropertyName("parameter_ref")]
    public string? ParameterRef { get; init; }

    [JsonPropertyName("operator")]
    public string? Operator { get; init; }

    [JsonPropertyName("value")]
    public QueryPredicateValue? Value { get; init; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(QueryPredicateStringValue), "string")]
[JsonDerivedType(typeof(QueryPredicateIntegerValue), "integer")]
[JsonDerivedType(typeof(QueryPredicateQuantityValue), "quantity")]
public abstract class QueryPredicateValue
{
}

public sealed class QueryPredicateStringValue : QueryPredicateValue
{
    [JsonPropertyName("value")]
    public required string Value { get; init; }
}

public sealed class QueryPredicateIntegerValue : QueryPredicateValue
{
    [JsonPropertyName("value")]
    public required int Value { get; init; }
}

public sealed class QueryPredicateQuantityValue : QueryPredicateValue
{
    [JsonPropertyName("value")]
    public required double Value { get; init; }

    [JsonPropertyName("unit_type_id")]
    public required string UnitTypeId { get; init; }
}
