using System.Text.Json.Serialization;

namespace RevitMCP.Contracts;

public sealed class PreviewParameterUpdatesResult
{
    [JsonPropertyName("context")]
    public required DescribeParametersContext Context { get; init; }

    [JsonPropertyName("ready")]
    public required bool Ready { get; init; }

    [JsonPropertyName("items")]
    public required IReadOnlyList<PreviewParameterUpdateItem> Items { get; init; }

    [JsonPropertyName("intent_ref")]
    public string? IntentRef { get; init; }

    [JsonPropertyName("intent_fingerprint")]
    public string? IntentFingerprint { get; init; }

    [JsonPropertyName("expires_at")]
    [JsonConverter(typeof(PreviewExpiresAtJsonConverter))]
    public DateTimeOffset? ExpiresAt { get; init; }
}
