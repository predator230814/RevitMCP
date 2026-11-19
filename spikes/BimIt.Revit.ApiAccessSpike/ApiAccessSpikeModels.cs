using System.Text.Json.Serialization;

namespace BimIt.Revit.Addin.ReadOperations;

internal sealed class ApiMetadataLookupRequest
{
    [JsonPropertyName("query")]
    public string? Query { get; init; }

    [JsonPropertyName("type_name")]
    public string? TypeName { get; init; }
}

internal sealed class ApiElementReadRequest
{
    [JsonPropertyName("type_name")]
    public string? TypeName { get; init; }

    [JsonPropertyName("properties")]
    public string[]? Properties { get; init; }
}

internal sealed class ApiMetadataSummary
{
    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("matched_type_count")]
    public required int MatchedTypeCount { get; init; }
}

internal sealed class ApiMetadataProperty
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("return_type")]
    public required string ReturnType { get; init; }

    [JsonPropertyName("can_project")]
    public required bool CanProject { get; init; }

    [JsonPropertyName("rejection")]
    public string? Rejection { get; init; }
}

internal sealed class ApiMetadataMethod
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("return_type")]
    public required string ReturnType { get; init; }

    [JsonPropertyName("parameter_count")]
    public required int ParameterCount { get; init; }

    [JsonPropertyName("executable")]
    public bool Executable { get; init; }
}

internal sealed class ApiMetadataType
{
    [JsonPropertyName("type_name")]
    public required string TypeName { get; init; }

    [JsonPropertyName("base_type_name")]
    public string? BaseTypeName { get; init; }

    [JsonPropertyName("is_element")]
    public required bool IsElement { get; init; }

    [JsonPropertyName("is_abstract")]
    public required bool IsAbstract { get; init; }

    [JsonPropertyName("can_collect")]
    public required bool CanCollect { get; init; }

    [JsonPropertyName("matched_members")]
    public required IReadOnlyList<string> MatchedMembers { get; init; }

    [JsonPropertyName("properties")]
    public required IReadOnlyList<ApiMetadataProperty> Properties { get; init; }

    [JsonPropertyName("properties_truncated")]
    public required bool PropertiesTruncated { get; init; }

    [JsonPropertyName("methods")]
    public required IReadOnlyList<ApiMetadataMethod> Methods { get; init; }

    [JsonPropertyName("methods_truncated")]
    public required bool MethodsTruncated { get; init; }
}

internal sealed class ApiMetadataResult
{
    [JsonPropertyName("summary")]
    public required ApiMetadataSummary Summary { get; init; }

    [JsonPropertyName("returned_count")]
    public required int ReturnedCount { get; init; }

    [JsonPropertyName("types")]
    public required IReadOnlyList<ApiMetadataType> Types { get; init; }
}

internal sealed class ApiReadSummary
{
    [JsonPropertyName("type_name")]
    public required string TypeName { get; init; }

    [JsonPropertyName("matched_element_count")]
    public required int MatchedElementCount { get; init; }
}

internal sealed class ApiScalarValue
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("boolean")]
    public bool? Boolean { get; init; }

    [JsonPropertyName("integer")]
    public long? Integer { get; init; }

    [JsonPropertyName("number")]
    public double? Number { get; init; }
}

internal sealed class ApiElementRow
{
    [JsonPropertyName("values")]
    public required IReadOnlyList<ApiScalarValue> Values { get; init; }
}

internal sealed class ApiElementReadResult
{
    [JsonPropertyName("summary")]
    public required ApiReadSummary Summary { get; init; }

    [JsonPropertyName("returned_count")]
    public required int ReturnedCount { get; init; }

    [JsonPropertyName("rows")]
    public required IReadOnlyList<ApiElementRow> Rows { get; init; }
}
