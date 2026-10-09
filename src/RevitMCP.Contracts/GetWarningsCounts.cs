using System.Text.Json.Serialization;

namespace RevitMCP.Contracts;

public sealed class GetWarningsCounts
{
    [JsonPropertyName("warning")]
    public required int Warning { get; init; }

    [JsonPropertyName("error")]
    public required int Error { get; init; }

    [JsonPropertyName("document_corruption")]
    public required int DocumentCorruption { get; init; }

    [JsonPropertyName("other")]
    public required int Other { get; init; }
}
