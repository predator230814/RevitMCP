using System.Text.Json.Serialization;

namespace BimIt.Revit.Contracts;

/// <summary>
/// CAP-0015 transport-neutral request. No <c>instance_id</c>: routing stays a Server concern.
/// </summary>
public sealed class QueryParameterBindingsRequest
{
    [JsonPropertyName("document_id")]
    public string? DocumentId { get; init; }

    [JsonPropertyName("name_contains")]
    public string? NameContains { get; init; }

    [JsonPropertyName("identity_kind")]
    public string? IdentityKind { get; init; }

    [JsonPropertyName("binding_kind")]
    public string? BindingKind { get; init; }

    [JsonPropertyName("category_tokens")]
    public IReadOnlyList<string>? CategoryTokens { get; init; }

    [JsonPropertyName("page_size")]
    public int? PageSize { get; init; }

    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }
}

public enum QueryParameterBindingKind
{
    Instance,
    Type
}

public sealed class QueryParameterBindingsContext
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }
}

public sealed class QueryParameterBindingsSummary
{
    [JsonPropertyName("matched_definition_count")]
    public required int MatchedDefinitionCount { get; init; }

    [JsonPropertyName("shared_count")]
    public required int SharedCount { get; init; }

    [JsonPropertyName("local_count")]
    public required int LocalCount { get; init; }

    [JsonPropertyName("built_in_count")]
    public required int BuiltInCount { get; init; }

    [JsonPropertyName("instance_bound_count")]
    public required int InstanceBoundCount { get; init; }

    [JsonPropertyName("type_bound_count")]
    public required int TypeBoundCount { get; init; }

    [JsonPropertyName("distinct_category_count")]
    public required int DistinctCategoryCount { get; init; }
}

public sealed class QueryParameterBindingCategory
{
    [JsonPropertyName("category_token")]
    public required string CategoryToken { get; init; }

    [JsonPropertyName("category_name")]
    public required string CategoryName { get; init; }
}

public sealed class QueryParameterBindingRow
{
    [JsonPropertyName("parameter_ref")]
    public required string ParameterRef { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("identity")]
    public required DescribeParameterIdentity Identity { get; init; }

    [JsonPropertyName("data_type")]
    public required DescribeParameterDataType DataType { get; init; }

    [JsonPropertyName("binding_kind")]
    public required QueryParameterBindingKind BindingKind { get; init; }

    [JsonPropertyName("categories")]
    public required IReadOnlyList<QueryParameterBindingCategory> Categories { get; init; }
}

public sealed class QueryParameterBindingsResult
{
    [JsonPropertyName("context")]
    public required QueryParameterBindingsContext Context { get; init; }

    [JsonPropertyName("summary")]
    public required QueryParameterBindingsSummary Summary { get; init; }

    [JsonPropertyName("returned_count")]
    public required int ReturnedCount { get; init; }

    [JsonPropertyName("complete")]
    public required bool Complete { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    [JsonPropertyName("next_cursor")]
    public string? NextCursor { get; init; }

    [JsonPropertyName("bindings")]
    public required IReadOnlyList<QueryParameterBindingRow> Bindings { get; init; }
}
