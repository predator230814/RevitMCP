using System.Text.Json.Serialization;

namespace BimIt.Revit.Contracts;

public sealed class QueryFamilyTypesRequest
{
    [JsonPropertyName("document_id")]
    public string? DocumentId { get; init; }

    [JsonPropertyName("category_tokens")]
    public IReadOnlyList<string>? CategoryTokens { get; init; }

    [JsonPropertyName("family_name_contains")]
    public string? FamilyNameContains { get; init; }

    [JsonPropertyName("type_name_contains")]
    public string? TypeNameContains { get; init; }

    [JsonPropertyName("usage")]
    public string? Usage { get; init; }

    [JsonPropertyName("page_size")]
    public int? PageSize { get; init; }

    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }
}

public sealed class QueryFamilyTypesContext
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }
}

public sealed class QueryFamilyTypesSummary
{
    [JsonPropertyName("matched_type_count")]
    public required int MatchedTypeCount { get; init; }

    [JsonPropertyName("distinct_family_count")]
    public required int DistinctFamilyCount { get; init; }

    [JsonPropertyName("placed_type_count")]
    public required int PlacedTypeCount { get; init; }

    [JsonPropertyName("placed_instance_count")]
    public required int PlacedInstanceCount { get; init; }

    [JsonPropertyName("used_family_count")]
    public required int UsedFamilyCount { get; init; }

    [JsonPropertyName("unused_family_count")]
    public required int UnusedFamilyCount { get; init; }
}

public sealed class QueryFamilyTypeRow
{
    [JsonPropertyName("type_ref")]
    public required string TypeRef { get; init; }

    [JsonPropertyName("family_ref")]
    public required string FamilyRef { get; init; }

    [JsonPropertyName("category_token")]
    public required string CategoryToken { get; init; }

    [JsonPropertyName("category_name")]
    public required string CategoryName { get; init; }

    [JsonPropertyName("family_name")]
    public required string FamilyName { get; init; }

    [JsonPropertyName("type_name")]
    public required string TypeName { get; init; }

    [JsonPropertyName("family_placement_type")]
    public required string FamilyPlacementType { get; init; }

    [JsonPropertyName("family_is_in_place")]
    public required bool FamilyIsInPlace { get; init; }

    [JsonPropertyName("placed_instance_count")]
    public required int PlacedInstanceCount { get; init; }

    [JsonPropertyName("is_used")]
    public required bool IsUsed { get; init; }
}

public sealed class QueryFamilyTypesResult
{
    [JsonPropertyName("context")]
    public required QueryFamilyTypesContext Context { get; init; }

    [JsonPropertyName("summary")]
    public required QueryFamilyTypesSummary Summary { get; init; }

    [JsonPropertyName("returned_count")]
    public required int ReturnedCount { get; init; }

    [JsonPropertyName("complete")]
    public required bool Complete { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    [JsonPropertyName("next_cursor")]
    public string? NextCursor { get; init; }

    [JsonPropertyName("types")]
    public required IReadOnlyList<QueryFamilyTypeRow> Types { get; init; }
}
