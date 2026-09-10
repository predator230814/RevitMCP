using System.Text.Json.Serialization;

namespace RevitMCP.Contracts;

public sealed class GetContextDocument
{
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("kind")]
    public required GetContextDocumentKind Kind { get; init; }

    [JsonPropertyName("is_workshared")]
    public required bool IsWorkshared { get; init; }

    [JsonPropertyName("is_model_in_cloud")]
    public required bool IsModelInCloud { get; init; }

    [JsonPropertyName("is_read_only")]
    public required bool IsReadOnly { get; init; }

    [JsonPropertyName("is_modified")]
    public required bool IsModified { get; init; }
}
