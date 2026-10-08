using System.Text.Json.Serialization;

namespace RevitMCP.Contracts;

public sealed class ApplyParameterUpdatesResult
{
    [JsonPropertyName("status")]
    public required ApplyParameterUpdatesStatus Status { get; init; }
}
