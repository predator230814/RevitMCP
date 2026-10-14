using System.Text.Json.Serialization;

namespace BimIt.Revit.Contracts;

public sealed class RequestParameterUpdateReviewRequest
{
    [JsonPropertyName("intent_ref")]
    public required string IntentRef { get; init; }
}
