using System.Text.Json.Serialization;

namespace BimIt.Revit.Addin.ReadOperations;

internal sealed class RevitApiSymbolRequest
{
    [JsonPropertyName("query")]
    public string? Query { get; init; }

    [JsonPropertyName("type_name")]
    public string? TypeName { get; init; }

    [JsonPropertyName("member")]
    public string? Member { get; init; }
}

internal sealed class RevitApiSymbolSummary
{
    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("matched_type_count")]
    public required int MatchedTypeCount { get; init; }
}

internal sealed class RevitApiSymbolSignature
{
    [JsonPropertyName("text")]
    public required string Text { get; init; }

    [JsonPropertyName("obsolete")]
    public required bool Obsolete { get; init; }

    [JsonPropertyName("obsolete_message")]
    public string? ObsoleteMessage { get; init; }

    [JsonPropertyName("obsolete_message_truncated")]
    public required bool ObsoleteMessageTruncated { get; init; }

    [JsonPropertyName("text_truncated")]
    public required bool TextTruncated { get; init; }
}

internal sealed class RevitApiSymbolGroup
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("is_static")]
    public required bool IsStatic { get; init; }

    [JsonPropertyName("signatures")]
    public required IReadOnlyList<RevitApiSymbolSignature> Signatures { get; init; }

    [JsonPropertyName("signatures_omitted_count")]
    public required int SignaturesOmittedCount { get; init; }
}

internal sealed class RevitApiSymbolMember
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("is_static")]
    public required bool IsStatic { get; init; }

    [JsonPropertyName("signatures")]
    public required IReadOnlyList<RevitApiSymbolSignature> Signatures { get; init; }

    [JsonPropertyName("signatures_omitted_count")]
    public required int SignaturesOmittedCount { get; init; }
}

internal sealed class RevitApiSymbolType
{
    [JsonPropertyName("full_name")]
    public required string FullName { get; init; }

    [JsonPropertyName("simple_name")]
    public required string SimpleName { get; init; }

    [JsonPropertyName("namespace")]
    public required string Namespace { get; init; }

    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("base_type_name")]
    public string? BaseTypeName { get; init; }

    [JsonPropertyName("assembly_name")]
    public required string AssemblyName { get; init; }

    [JsonPropertyName("assembly_version")]
    public required string AssemblyVersion { get; init; }

    [JsonPropertyName("obsolete")]
    public required bool Obsolete { get; init; }

    [JsonPropertyName("obsolete_message")]
    public string? ObsoleteMessage { get; init; }

    [JsonPropertyName("obsolete_message_truncated")]
    public required bool ObsoleteMessageTruncated { get; init; }

    [JsonPropertyName("matched_members")]
    public required IReadOnlyList<string> MatchedMembers { get; init; }

    [JsonPropertyName("matched_members_omitted_count")]
    public required int MatchedMembersOmittedCount { get; init; }

    [JsonPropertyName("constructors")]
    public required IReadOnlyList<RevitApiSymbolSignature> Constructors { get; init; }

    [JsonPropertyName("constructors_omitted_count")]
    public required int ConstructorsOmittedCount { get; init; }

    [JsonPropertyName("properties")]
    public required IReadOnlyList<RevitApiSymbolSignature> Properties { get; init; }

    [JsonPropertyName("properties_omitted_count")]
    public required int PropertiesOmittedCount { get; init; }

    [JsonPropertyName("fields")]
    public required IReadOnlyList<RevitApiSymbolSignature> Fields { get; init; }

    [JsonPropertyName("fields_omitted_count")]
    public required int FieldsOmittedCount { get; init; }

    [JsonPropertyName("methods")]
    public required IReadOnlyList<RevitApiSymbolGroup> Methods { get; init; }

    [JsonPropertyName("methods_omitted_count")]
    public required int MethodsOmittedCount { get; init; }

    [JsonPropertyName("factories")]
    public required IReadOnlyList<RevitApiSymbolSignature> Factories { get; init; }

    [JsonPropertyName("factories_omitted_count")]
    public required int FactoriesOmittedCount { get; init; }

    [JsonPropertyName("member")]
    public RevitApiSymbolMember? Member { get; init; }
}

internal sealed class RevitApiSymbolResult
{
    [JsonPropertyName("summary")]
    public required RevitApiSymbolSummary Summary { get; init; }

    [JsonPropertyName("returned_count")]
    public required int ReturnedCount { get; init; }

    [JsonPropertyName("types")]
    public required IReadOnlyList<RevitApiSymbolType> Types { get; init; }
}
