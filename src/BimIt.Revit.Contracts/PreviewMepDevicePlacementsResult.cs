using System.Text.Json.Serialization;

namespace BimIt.Revit.Contracts;

public sealed class PreviewMepDevicePlacementsResult
{
    [JsonPropertyName("context")]
    public required PreviewMepDevicePlacementsContext Context { get; init; }

    [JsonPropertyName("ready")]
    public required bool Ready { get; init; }

    [JsonPropertyName("template")]
    public required PreviewMepDeviceTemplate Template { get; init; }

    [JsonPropertyName("items")]
    public required IReadOnlyList<PreviewMepDevicePlacementItem> Items { get; init; }

    [JsonPropertyName("intent_ref")]
    public string? IntentRef { get; init; }

    [JsonPropertyName("intent_fingerprint")]
    public string? IntentFingerprint { get; init; }

    [JsonPropertyName("expires_at")]
    [JsonConverter(typeof(PreviewExpiresAtJsonConverter))]
    public DateTimeOffset? ExpiresAt { get; init; }
}
