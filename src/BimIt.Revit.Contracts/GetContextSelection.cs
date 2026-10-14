using System.Text.Json.Serialization;

namespace BimIt.Revit.Contracts;

public sealed class GetContextSelection
{
    [JsonPropertyName("count")]
    public required int Count { get; init; }
}
