using System.Text.Json.Serialization;

namespace RevitMCP.Contracts;

public sealed class DescribeParametersContext
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }
}
