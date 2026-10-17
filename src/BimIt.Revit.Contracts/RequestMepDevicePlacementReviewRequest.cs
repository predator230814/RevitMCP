using System.Text.Json.Serialization;

namespace BimIt.Revit.Contracts;

public sealed class RequestMepDevicePlacementReviewRequest
{
    [JsonPropertyName("intent_ref")]
    public required string IntentRef { get; init; }
}
