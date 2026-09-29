using System.Text.Json.Serialization;

namespace RevitMCP.Contracts;

public sealed class PreviewParameterBefore
{
    [JsonPropertyName("has_value")]
    public required bool HasValue { get; init; }

    [JsonPropertyName("value")]
    public PreviewParameterValue? Value { get; init; }
}
