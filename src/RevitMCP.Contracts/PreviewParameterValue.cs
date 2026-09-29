using System.Text.Json.Serialization;

namespace RevitMCP.Contracts;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(PreviewParameterStringValue), "string")]
[JsonDerivedType(typeof(PreviewParameterIntegerValue), "integer")]
[JsonDerivedType(typeof(PreviewParameterQuantityValue), "quantity")]
public abstract class PreviewParameterValue
{
}

public sealed class PreviewParameterStringValue : PreviewParameterValue
{
    [JsonPropertyName("value")]
    public required string Value { get; init; }
}

public sealed class PreviewParameterIntegerValue : PreviewParameterValue
{
    [JsonPropertyName("value")]
    public required int Value { get; init; }
}

public sealed class PreviewParameterQuantityValue : PreviewParameterValue
{
    [JsonPropertyName("value")]
    public required double Value { get; init; }

    [JsonPropertyName("unit_type_id")]
    public required string UnitTypeId { get; init; }
}
