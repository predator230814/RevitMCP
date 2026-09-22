using System.Text.Json.Serialization;

namespace RevitMCP.Contracts;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(GetParameterStringValue), "string")]
[JsonDerivedType(typeof(GetParameterIntegerValue), "integer")]
[JsonDerivedType(typeof(GetParameterQuantityValue), "quantity")]
[JsonDerivedType(typeof(GetParameterElementReferenceValue), "element_reference")]
public abstract class GetParameterValue
{
}

public sealed class GetParameterStringValue : GetParameterValue
{
    [JsonPropertyName("value")]
    public required string Value { get; init; }

    [JsonPropertyName("truncated")]
    public required bool Truncated { get; init; }
}

public sealed class GetParameterIntegerValue : GetParameterValue
{
    [JsonPropertyName("value")]
    public required int Value { get; init; }
}

public sealed class GetParameterQuantityValue : GetParameterValue
{
    [JsonPropertyName("value")]
    public required double Value { get; init; }

    [JsonPropertyName("unit_type_id")]
    public required string UnitTypeId { get; init; }
}

public sealed class GetParameterElementReferenceValue : GetParameterValue
{
    [JsonPropertyName("resolved")]
    public required bool Resolved { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("element_ref")]
    public string? ElementRef { get; init; }
}
