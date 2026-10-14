using System.Text.Json.Serialization;

namespace BimIt.Revit.Contracts;

public sealed class GetWarningsElement
{
    [JsonPropertyName("element_ref")]
    public required string ElementRef { get; init; }

    [JsonPropertyName("role")]
    public required WarningElementRole Role { get; init; }
}
