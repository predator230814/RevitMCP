using System.Text.Json.Serialization;

namespace RevitMCP.Contracts;

public sealed class QueryElementFilters
{
    [JsonPropertyName("category_names")]
    public IReadOnlyList<string>? CategoryNames { get; init; }

    [JsonPropertyName("family_names")]
    public IReadOnlyList<string>? FamilyNames { get; init; }

    [JsonPropertyName("type_names")]
    public IReadOnlyList<string>? TypeNames { get; init; }

    [JsonPropertyName("level_names")]
    public IReadOnlyList<string>? LevelNames { get; init; }

    [JsonPropertyName("text_contains")]
    public string? TextContains { get; init; }
}
