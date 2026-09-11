using System.Text.Json.Serialization;

namespace RevitMCP.Contracts;

public sealed class GetElementResult
{
    [JsonPropertyName("element_ref")]
    public required string ElementRef { get; init; }

    [JsonPropertyName("status")]
    public required GetElementResultStatus Status { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    [JsonPropertyName("name")]
    public ProjectedString Name { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    [JsonPropertyName("category_name")]
    public ProjectedString CategoryName { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    [JsonPropertyName("family_name")]
    public ProjectedString FamilyName { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    [JsonPropertyName("type_name")]
    public ProjectedString TypeName { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    [JsonPropertyName("level_name")]
    public ProjectedString LevelName { get; init; }

    [JsonPropertyName("parameters")]
    public IReadOnlyList<GetElementParameter>? Parameters { get; init; }

    [JsonPropertyName("parameters_truncated")]
    public bool? ParametersTruncated { get; init; }
}
