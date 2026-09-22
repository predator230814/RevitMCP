using System.Text.Json.Serialization;

namespace RevitMCP.Contracts;

public sealed class GetMepTopologyEdge
{
    [JsonPropertyName("element_ref_a")]
    public required string ElementRefA { get; init; }

    [JsonPropertyName("element_ref_b")]
    public required string ElementRefB { get; init; }

    [JsonPropertyName("domains")]
    public required IReadOnlyList<MepTopologyDomain> Domains { get; init; }
}
