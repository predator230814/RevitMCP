using System.Text.Json.Serialization;

namespace BimIt.Revit.Contracts;

public sealed class ApplyParameterUpdatesResult
{
    [JsonPropertyName("status")]
    public required ApplyParameterUpdatesStatus Status { get; init; }
}
