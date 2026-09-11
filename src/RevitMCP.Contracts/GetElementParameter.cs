using System.Text.Json.Serialization;

namespace RevitMCP.Contracts;

public sealed class GetElementParameter
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("source")]
    public required GetElementParameterSource Source { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    [JsonPropertyName("value_text")]
    public string? ValueText { get; init; }

    [JsonPropertyName("value_truncated")]
    public required bool ValueTruncated { get; init; }
}
