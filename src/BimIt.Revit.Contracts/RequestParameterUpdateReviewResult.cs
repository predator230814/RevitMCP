using System.Text.Json.Serialization;

namespace BimIt.Revit.Contracts;

public sealed class RequestParameterUpdateReviewResult
{
    [JsonPropertyName("status")]
    public required RequestParameterUpdateReviewStatus Status { get; init; }
}
