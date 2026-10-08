using System.Text.Json.Serialization;

namespace RevitMCP.Contracts;

public sealed class ApplyParameterUpdatesRequest
{
    [JsonPropertyName("intent_ref")]
    public required string IntentRef { get; init; }
}
