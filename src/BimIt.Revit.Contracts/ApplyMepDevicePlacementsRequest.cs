using System.Text.Json.Serialization;

namespace BimIt.Revit.Contracts;

public sealed class ApplyMepDevicePlacementsRequest
{
    [JsonPropertyName("intent_ref")]
    public required string IntentRef { get; init; }
}
