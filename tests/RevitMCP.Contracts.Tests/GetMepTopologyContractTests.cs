using System.Text.Json;
using Xunit;

namespace RevitMCP.Contracts.Tests;

public sealed class GetMepTopologyContractTests
{
    [Fact]
    public void Serialization_uses_closed_snake_case_names()
    {
        var json = JsonSerializer.Serialize(CreateResult(), ContractJson.Options);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(
            ["context", "edges", "nodes", "seeds", "truncated", "truncation_reasons"],
            root.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());
        Assert.Equal("ok", root.GetProperty("seeds")[0].GetProperty("status").GetString());
        Assert.Equal("not_found", root.GetProperty("seeds")[1].GetProperty("status").GetString());
        Assert.Equal("no_connectors", root.GetProperty("seeds")[2].GetProperty("status").GetString());
        Assert.Equal("cable_tray_conduit", root.GetProperty("edges")[0].GetProperty("domains")[0].GetString());
        Assert.Equal("depth", root.GetProperty("truncation_reasons")[0].GetString());
        Assert.Equal("edges", root.GetProperty("truncation_reasons")[1].GetString());
        Assert.Equal("elements", root.GetProperty("truncation_reasons")[2].GetString());
        Assert.DoesNotContain("ElementId", json, StringComparison.Ordinal);
        Assert.DoesNotContain("connector_ref", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"connector\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Omitted_optional_input_round_trips_without_trimming_opaque_ids()
    {
        var request = JsonSerializer.Deserialize<GetMepTopologyRequest>(
            """
            {
              "document_id": " ",
              "seed_element_refs": ["", "\t"]
            }
            """,
            ContractJson.Options);

        Assert.NotNull(request);
        Assert.Equal(" ", request.DocumentId);
        Assert.Equal(["", "\t"], request.SeedElementRefs);
        Assert.Null(request.Domain);
        Assert.Null(request.MaxDepth);
        Assert.Null(request.MaxElements);
        Assert.Null(request.MaxEdges);

        var json = JsonSerializer.Serialize(request, ContractJson.Options);
        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.TryGetProperty("instance_id", out _));
        Assert.False(document.RootElement.TryGetProperty("domain", out _));
        Assert.False(document.RootElement.TryGetProperty("max_depth", out _));
    }

    [Fact]
    public void Request_and_result_have_exactly_the_accepted_fields()
    {
        Assert.Equal(
            [
                nameof(GetMepTopologyRequest.DocumentId),
                nameof(GetMepTopologyRequest.Domain),
                nameof(GetMepTopologyRequest.MaxDepth),
                nameof(GetMepTopologyRequest.MaxEdges),
                nameof(GetMepTopologyRequest.MaxElements),
                nameof(GetMepTopologyRequest.SeedElementRefs)
            ],
            typeof(GetMepTopologyRequest).GetProperties().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());
        Assert.Equal(
            [
                nameof(GetMepTopologyResult.Context),
                nameof(GetMepTopologyResult.Edges),
                nameof(GetMepTopologyResult.Nodes),
                nameof(GetMepTopologyResult.Seeds),
                nameof(GetMepTopologyResult.Truncated),
                nameof(GetMepTopologyResult.TruncationReasons)
            ],
            typeof(GetMepTopologyResult).GetProperties().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());
        Assert.Equal("INVALID_MEP_TOPOLOGY", CapabilityErrorCodes.InvalidMepTopology);
    }

    private static GetMepTopologyResult CreateResult()
    {
        return new GetMepTopologyResult
        {
            Context = new GetMepTopologyContext { InstanceId = "instance", DocumentId = "document" },
            Seeds =
            [
                new GetMepTopologySeed { ElementRef = "a", Status = MepTopologySeedStatus.Ok },
                new GetMepTopologySeed { ElementRef = "b", Status = MepTopologySeedStatus.NotFound },
                new GetMepTopologySeed { ElementRef = "c", Status = MepTopologySeedStatus.NoConnectors }
            ],
            Nodes = [new GetMepTopologyNode { ElementRef = "a", Depth = 0 }],
            Edges =
            [
                new GetMepTopologyEdge
                {
                    ElementRefA = "a",
                    ElementRefB = "d",
                    Domains = [MepTopologyDomain.CableTrayConduit]
                }
            ],
            Truncated = true,
            TruncationReasons =
            [
                MepTopologyTruncationReason.Depth,
                MepTopologyTruncationReason.Edges,
                MepTopologyTruncationReason.Elements
            ]
        };
    }
}
