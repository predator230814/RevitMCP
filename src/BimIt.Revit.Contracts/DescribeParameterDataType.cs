using System.Text.Json.Serialization;

namespace BimIt.Revit.Contracts;

public sealed class DescribeParameterDataType
{
    [JsonPropertyName("kind")]
    public required DescribeParameterDataTypeKind Kind { get; init; }

    [JsonPropertyName("forge_type_id")]
    public string? ForgeTypeId { get; init; }
}
