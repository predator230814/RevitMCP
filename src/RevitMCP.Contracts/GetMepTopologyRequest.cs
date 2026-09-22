using System.Text.Json.Serialization;

namespace RevitMCP.Contracts;

public sealed class GetMepTopologyRequest
{
    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    [JsonPropertyName("seed_element_refs")]
    public required IReadOnlyList<string> SeedElementRefs { get; init; }

    [JsonPropertyName("domain")]
    public MepTopologyDomain? Domain { get; init; }

    [JsonPropertyName("max_depth")]
    public int? MaxDepth { get; init; }

    [JsonPropertyName("max_elements")]
    public int? MaxElements { get; init; }

    [JsonPropertyName("max_edges")]
    public int? MaxEdges { get; init; }
}
