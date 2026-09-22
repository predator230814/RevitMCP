using System.Text.Json.Serialization;

namespace RevitMCP.Contracts;

public sealed class GetMepTopologyResult
{
    [JsonPropertyName("context")]
    public required GetMepTopologyContext Context { get; init; }

    [JsonPropertyName("seeds")]
    public required IReadOnlyList<GetMepTopologySeed> Seeds { get; init; }

    [JsonPropertyName("nodes")]
    public required IReadOnlyList<GetMepTopologyNode> Nodes { get; init; }

    [JsonPropertyName("edges")]
    public required IReadOnlyList<GetMepTopologyEdge> Edges { get; init; }

    [JsonPropertyName("truncated")]
    public required bool Truncated { get; init; }

    [JsonPropertyName("truncation_reasons")]
    public required IReadOnlyList<MepTopologyTruncationReason> TruncationReasons { get; init; }
}
