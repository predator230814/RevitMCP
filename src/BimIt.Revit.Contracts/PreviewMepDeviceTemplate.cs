using System.Text.Json.Serialization;

namespace BimIt.Revit.Contracts;

public sealed class PreviewMepDeviceTemplate
{
    [JsonPropertyName("element_ref")]
    public required string ElementRef { get; init; }

    [JsonPropertyName("status")]
    public required PreviewMepDeviceTemplateStatus Status { get; init; }

    [JsonPropertyName("placement_mode")]
    public PreviewMepDevicePlacementMode? PlacementMode { get; init; }

    [JsonPropertyName("category_name")]
    public string? CategoryName { get; init; }

    [JsonPropertyName("family_name")]
    public string? FamilyName { get; init; }

    [JsonPropertyName("type_name")]
    public string? TypeName { get; init; }

    [JsonPropertyName("level_name")]
    public string? LevelName { get; init; }

    [JsonPropertyName("host_element_ref")]
    public string? HostElementRef { get; init; }

    [JsonPropertyName("host_name")]
    public string? HostName { get; init; }
}
