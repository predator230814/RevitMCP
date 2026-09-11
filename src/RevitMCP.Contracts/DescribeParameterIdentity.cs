using System.Text.Json.Serialization;

namespace RevitMCP.Contracts;

public sealed class DescribeParameterIdentity
{
    [JsonPropertyName("kind")]
    public required DescribeParameterIdentityKind Kind { get; init; }

    [JsonPropertyName("parameter_type_id")]
    public string? ParameterTypeId { get; init; }

    [JsonPropertyName("guid")]
    public string? Guid { get; init; }
}
