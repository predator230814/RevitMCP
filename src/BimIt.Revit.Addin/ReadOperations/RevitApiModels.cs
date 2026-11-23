using System.Text.Json.Serialization;

namespace BimIt.Revit.Addin.ReadOperations;

internal sealed class RevitApiMetadataRequest
{
    [JsonPropertyName("query")]
    public string? Query { get; init; }

    [JsonPropertyName("type_name")]
    public string? TypeName { get; init; }
}

internal sealed class RevitApiElementPropertiesRequest
{
    [JsonPropertyName("type_name")]
    public string? TypeName { get; init; }

    [JsonPropertyName("properties")]
    public string[]? Properties { get; init; }
}

internal sealed class RevitApiMetadataSummary
{
    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("matched_type_count")]
    public required int MatchedTypeCount { get; init; }
}

internal sealed class RevitApiMetadataProperty
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

internal sealed class RevitApiMetadataType
{
    [JsonPropertyName("type_name")]
    public required string TypeName { get; init; }

    [JsonPropertyName("base_type_name")]
    public string? BaseTypeName { get; init; }

    [JsonPropertyName("is_element")]
    public required bool IsElement { get; init; }

    [JsonPropertyName("is_abstract")]
    public required bool IsAbstract { get; init; }

    [JsonPropertyName("matched_members")]
    public required IReadOnlyList<string> MatchedMembers { get; init; }

    [JsonPropertyName("matched_members_omitted_count")]
    public required int MatchedMembersOmittedCount { get; init; }

    [JsonPropertyName("properties")]
    public required IReadOnlyList<RevitApiMetadataProperty> Properties { get; init; }

    [JsonPropertyName("rejected_omitted_count")]
    public required int RejectedOmittedCount { get; init; }
}

internal sealed class RevitApiMetadataResult
{
    [JsonPropertyName("summary")]
    public required RevitApiMetadataSummary Summary { get; init; }

    [JsonPropertyName("returned_count")]
    public required int ReturnedCount { get; init; }

    [JsonPropertyName("types")]
    public required IReadOnlyList<RevitApiMetadataType> Types { get; init; }
}

internal sealed class RevitApiElementPropertiesSummary
{
    [JsonPropertyName("type_name")]
    public required string TypeName { get; init; }

    [JsonPropertyName("matched_element_count")]
    public required int MatchedElementCount { get; init; }
}

internal sealed class RevitApiScalar
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

internal sealed class RevitApiElementRow
{
    [JsonPropertyName("element_ref")]
    public required string ElementRef { get; init; }

    [JsonPropertyName("values")]
    public required IReadOnlyList<RevitApiScalar> Values { get; init; }
}

internal sealed class RevitApiElementPropertiesResult
{
    [JsonPropertyName("summary")]
    public required RevitApiElementPropertiesSummary Summary { get; init; }

    [JsonPropertyName("returned_count")]
    public required int ReturnedCount { get; init; }

    [JsonPropertyName("elements")]
    public required IReadOnlyList<RevitApiElementRow> Elements { get; init; }
}
