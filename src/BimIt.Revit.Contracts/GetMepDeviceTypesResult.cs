using System.Text.Json.Serialization;

namespace BimIt.Revit.Contracts;

public sealed class GetMepDeviceTypesContext
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }
}

public sealed class MepDeviceTypeItem
{
    [JsonPropertyName("family_type_ref")]
    public required string FamilyTypeRef { get; init; }

    [JsonPropertyName("category_token")]
    public required string CategoryToken { get; init; }

    [JsonPropertyName("category_name")]
    public required string CategoryName { get; init; }

    [JsonPropertyName("family_name")]
    public required string FamilyName { get; init; }

    [JsonPropertyName("type_name")]
    public required string TypeName { get; init; }

    [JsonPropertyName("placement_classification")]
    public required string PlacementClassification { get; init; }

    [JsonPropertyName("active")]
    public required bool Active { get; init; }
}

public sealed class GetMepDeviceTypesResult
{
    [JsonPropertyName("context")]
    public required GetMepDeviceTypesContext Context { get; init; }

    [JsonPropertyName("truncated")]
    public required bool Truncated { get; init; }

    [JsonPropertyName("items")]
    public required IReadOnlyList<MepDeviceTypeItem> Items { get; init; }
}
