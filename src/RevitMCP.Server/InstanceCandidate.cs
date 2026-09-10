using System.Text.Json.Serialization;

namespace RevitMCP.Server;

internal sealed class InstanceCandidate
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("revit_version")]
    public required string RevitVersion { get; init; }

    [JsonPropertyName("revit_build")]
    public required string RevitBuild { get; init; }
}
