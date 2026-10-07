using System.Text.Json.Serialization;

namespace RevitMCP.Contracts;

public sealed class RequestParameterUpdateReviewResult
{
    [JsonPropertyName("status")]
    public required RequestParameterUpdateReviewStatus Status { get; init; }
}
