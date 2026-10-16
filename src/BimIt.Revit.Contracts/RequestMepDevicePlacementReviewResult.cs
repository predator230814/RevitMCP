using System.Text.Json.Serialization;

namespace BimIt.Revit.Contracts;

public sealed class RequestMepDevicePlacementReviewResult
{
    [JsonPropertyName("status")]
    public required RequestMepDevicePlacementReviewStatus Status { get; init; }
}
