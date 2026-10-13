using System.Text.Json.Serialization;

namespace BimIt.Revit.Contracts;

public sealed class GetMepTopologySeed
{
    [JsonPropertyName("element_ref")]
    public required string ElementRef { get; init; }

    [JsonPropertyName("status")]
    public required MepTopologySeedStatus Status { get; init; }
}
