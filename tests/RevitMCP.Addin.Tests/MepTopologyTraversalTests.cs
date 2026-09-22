using RevitMCP.Addin.Capabilities;
using RevitMCP.Addin.Inspection;
using RevitMCP.Addin.Topology;
using RevitMCP.Contracts;
using Xunit;

namespace RevitMCP.Addin.Tests;

public sealed class MepTopologyTraversalTests
{
    [Fact]
    public void Physical_filter_ignores_nonphysical_relationships_and_disconnected_connectors()
    {
        var connectors = new[]
        {
            Connector(ObservedConnectorClass.Logical, true, MepTopologyDomain.Hvac, Neighbor("logical", ObservedConnectorClass.End)),
            Connector(ObservedConnectorClass.Reference, true, MepTopologyDomain.Hvac, Neighbor("reference", ObservedConnectorClass.Physical)),
            Connector(ObservedConnectorClass.Family, true, MepTopologyDomain.Hvac, Neighbor("family", ObservedConnectorClass.End)),
            Connector(ObservedConnectorClass.Super, true, MepTopologyDomain.Hvac, Neighbor("super", ObservedConnectorClass.Curve)),
            Connector(ObservedConnectorClass.Other, true, MepTopologyDomain.Hvac, Neighbor("other", ObservedConnectorClass.End)),
            Connector(ObservedConnectorClass.End, true, MepTopologyDomain.Hvac, Neighbor("logical-ref", ObservedConnectorClass.Logical)),
            Connector(ObservedConnectorClass.End, false, MepTopologyDomain.Hvac, Neighbor("disconnected", ObservedConnectorClass.End)),
            Connector(ObservedConnectorClass.End, true, MepTopologyDomain.Piping, Neighbor("pipe", ObservedConnectorClass.End)),
            Connector(ObservedConnectorClass.Curve, true, MepTopologyDomain.Hvac, Neighbor("seed", ObservedConnectorClass.Physical)),
            Connector(ObservedConnectorClass.Physical, true, MepTopologyDomain.Electrical, Neighbor("equip", ObservedConnectorClass.End), Neighbor("equip", ObservedConnectorClass.Curve))
        };

        var described = PhysicalConnectionFilter.Describe("seed", connectors, domainFilter: null);

        Assert.Equal(MepTopologySeedStatus.Ok, described.Status);
        Assert.Equal(
            [
                new PhysicalNeighbor("equip", MepTopologyDomain.Electrical),
                new PhysicalNeighbor("equip", MepTopologyDomain.Electrical),
                new PhysicalNeighbor("pipe", MepTopologyDomain.Piping)
            ],
            described.Neighbors.OrderBy(neighbor => neighbor.ElementRef, StringComparer.Ordinal).ThenBy(neighbor => neighbor.Domain).ToArray());
    }

    [Fact]
    public void Domain_filter_can_leave_an_otherwise_connected_element_without_eligible_connectors()
    {
        var connectors = new[]
        {
            Connector(ObservedConnectorClass.End, true, MepTopologyDomain.Piping, Neighbor("pipe", ObservedConnectorClass.End))
        };

        var described = PhysicalConnectionFilter.Describe("seed", connectors, MepTopologyDomain.Hvac);

        Assert.Equal(MepTopologySeedStatus.NoConnectors, described.Status);
        Assert.Empty(described.Neighbors);
    }

    [Fact]
    public void Disconnected_eligible_connector_is_ok_with_degree_zero()
    {
        var described = PhysicalConnectionFilter.Describe(
            "seed",
            [Connector(ObservedConnectorClass.End, false, MepTopologyDomain.Hvac)],
            domainFilter: null);

        Assert.Equal(MepTopologySeedStatus.Ok, described.Status);
        Assert.Empty(described.Neighbors);
    }

    [Fact]
    public void ElementType_resolution_is_not_found_before_connector_inspection()
    {
        var reader = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "RevitMCP.Addin", "Topology", "RevitMepConnectorReader.cs"));
        Assert.Contains("element is null || element is ElementType", reader, StringComparison.Ordinal);
        Assert.Contains("MepTopologySeedStatus.NotFound", reader, StringComparison.Ordinal);
        Assert.DoesNotContain("new Transaction", reader, StringComparison.Ordinal);
    }

    [Fact]
    public void Traversal_preserves_seed_order_and_minimum_depth()
    {
        var graph = new Dictionary<string, ElementTopologyFacts>(StringComparer.Ordinal)
        {
            ["s2"] = Ok(Link("b", MepTopologyDomain.Hvac)),
            ["s1"] = Ok(Link("a", MepTopologyDomain.Hvac)),
            ["a"] = Ok(Link("b", MepTopologyDomain.Piping)),
            ["b"] = Ok(),
            ["missing"] = Missing(),
            ["plain"] = new ElementTopologyFacts(MepTopologySeedStatus.NoConnectors, [])
        };

        var result = MepTopologyTraversal.Traverse(
            "instance",
            "document",
            Request(["missing", "s1", "plain", "s2"]),
            elementRef => graph[elementRef]);

        Assert.Equal(
            [
                MepTopologySeedStatus.NotFound,
                MepTopologySeedStatus.Ok,
                MepTopologySeedStatus.NoConnectors,
                MepTopologySeedStatus.Ok
            ],
            result.Seeds.Select(seed => seed.Status).ToArray());
        Assert.Equal(["s1", "s2", "a", "b"], result.Nodes.Select(node => node.ElementRef).ToArray());
        Assert.Equal(0, result.Nodes.Single(node => node.ElementRef == "s1").Depth);
        Assert.Equal(0, result.Nodes.Single(node => node.ElementRef == "s2").Depth);
        Assert.Equal(1, result.Nodes.Single(node => node.ElementRef == "b").Depth);
        Assert.False(result.Truncated);
    }

    [Fact]
    public void Traversal_is_independent_of_neighbor_enumeration_order()
    {
        var forward = new Dictionary<string, ElementTopologyFacts>(StringComparer.Ordinal)
        {
            ["s"] = Ok(Link("a", MepTopologyDomain.Piping), Link("b", MepTopologyDomain.Hvac), Link("b", MepTopologyDomain.Electrical)),
            ["a"] = Ok(),
            ["b"] = Ok()
        };
        var reverse = new Dictionary<string, ElementTopologyFacts>(StringComparer.Ordinal)
        {
            ["s"] = Ok(Link("b", MepTopologyDomain.Electrical), Link("b", MepTopologyDomain.Hvac), Link("a", MepTopologyDomain.Piping)),
            ["a"] = Ok(),
            ["b"] = Ok()
        };

        var left = Traverse(forward, ["s"]);
        var right = Traverse(reverse, ["s"]);

        Assert.Equal(left.Edges.Select(EdgeText), right.Edges.Select(EdgeText));
        Assert.Equal(["a", "b", "s"], left.Nodes.OrderBy(node => node.ElementRef, StringComparer.Ordinal).Select(node => node.ElementRef));
        var edge = Assert.Single(left.Edges, item => item.ElementRefA == "b" && item.ElementRefB == "s");
        Assert.Equal([MepTopologyDomain.Electrical, MepTopologyDomain.Hvac], edge.Domains);
    }

    [Fact]
    public void Canonical_edges_collapse_multiplicity_and_omit_self_loops()
    {
        var graph = new Dictionary<string, ElementTopologyFacts>(StringComparer.Ordinal)
        {
            ["m"] = Ok(Link("a", MepTopologyDomain.Hvac), Link("m", MepTopologyDomain.Hvac), Link("a", MepTopologyDomain.CableTrayConduit)),
            ["a"] = Ok(Link("m", MepTopologyDomain.Piping))
        };

        var result = Traverse(graph, ["m"]);
        var edge = Assert.Single(result.Edges);

        Assert.Equal("a", edge.ElementRefA);
        Assert.Equal("m", edge.ElementRefB);
        Assert.Equal(
            [MepTopologyDomain.CableTrayConduit, MepTopologyDomain.Hvac, MepTopologyDomain.Piping],
            edge.Domains);
    }

    [Fact]
    public void Depth_truncation_requires_an_observed_deeper_neighbor()
    {
        var bounded = new Dictionary<string, ElementTopologyFacts>(StringComparer.Ordinal)
        {
            ["s"] = Ok(Link("a", MepTopologyDomain.Hvac)),
            ["a"] = Ok()
        };
        var deeper = new Dictionary<string, ElementTopologyFacts>(StringComparer.Ordinal)
        {
            ["s"] = Ok(Link("a", MepTopologyDomain.Hvac)),
            ["a"] = Ok(Link("b", MepTopologyDomain.Hvac)),
            ["b"] = Ok()
        };

        var atBound = Traverse(bounded, ["s"], depth: 1);
        var omitted = Traverse(deeper, ["s"], depth: 1);

        Assert.False(atBound.Truncated);
        Assert.Empty(atBound.TruncationReasons);
        Assert.Equal(["a", "s"], atBound.Nodes.Select(node => node.ElementRef).OrderBy(value => value, StringComparer.Ordinal));
        Assert.True(omitted.Truncated);
        Assert.Equal([MepTopologyTruncationReason.Depth], omitted.TruncationReasons);
        Assert.DoesNotContain(omitted.Nodes, node => node.ElementRef == "b");
    }

    [Fact]
    public void Max_elements_omits_nodes_without_claiming_depth_truncation()
    {
        var graph = GraphFrom("s", 11);
        var result = Traverse(graph, ["s"], elements: 10);

        Assert.Equal(10, result.Nodes.Count);
        Assert.Equal([MepTopologyTruncationReason.Elements], result.TruncationReasons);
        Assert.DoesNotContain(MepTopologyTruncationReason.Depth, result.TruncationReasons);
    }

    [Fact]
    public void Max_edges_does_not_admit_or_traverse_a_refused_neighbor()
    {
        var graph = GraphFrom("s1", 10);
        graph["s1"] = Ok(graph["s1"].Neighbors.Append(Link("s2", MepTopologyDomain.Hvac)).ToArray());
        graph["s2"] = Ok(Link("s1", MepTopologyDomain.Hvac), Link("hidden", MepTopologyDomain.Hvac));
        graph["n10"] = Ok(Link("hidden", MepTopologyDomain.Piping));
        graph["hidden"] = Ok(Link("deeper", MepTopologyDomain.Hvac));
        graph["deeper"] = Ok();

        var result = Traverse(graph, ["s1", "s2"], edges: 10);

        Assert.Contains(result.Nodes, node => node.ElementRef == "s1" && node.Depth == 0);
        Assert.Contains(result.Nodes, node => node.ElementRef == "s2" && node.Depth == 0);
        Assert.DoesNotContain(result.Nodes, node => node.ElementRef == "hidden");
        Assert.DoesNotContain(result.Nodes, node => node.ElementRef == "deeper");
        Assert.DoesNotContain(result.Edges, edge => edge.ElementRefA == "s1" && edge.ElementRefB == "s2");
        Assert.Contains(MepTopologyTruncationReason.Edges, result.TruncationReasons);
        AssertParentEdges(result);
    }

    [Fact]
    public void Existing_edge_domain_enrichment_does_not_consume_another_slot()
    {
        var graph = new Dictionary<string, ElementTopologyFacts>(StringComparer.Ordinal)
        {
            ["a"] = Ok(Link("b", MepTopologyDomain.Hvac)),
            ["b"] = Ok(Link("a", MepTopologyDomain.Electrical))
        };

        var result = Traverse(graph, ["a", "b"], edges: 10);
        var edge = Assert.Single(result.Edges);

        Assert.False(result.Truncated);
        Assert.Equal([MepTopologyDomain.Electrical, MepTopologyDomain.Hvac], edge.Domains);
    }

    [Fact]
    public void Revit_reader_uses_only_the_accepted_connector_manager_surfaces()
    {
        var reader = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "RevitMCP.Addin", "Topology", "RevitMepConnectorReader.cs"));
        Assert.Contains("MEPCurve curve => curve.ConnectorManager", reader, StringComparison.Ordinal);
        Assert.Contains("FamilyInstance family => family.MEPModel?.ConnectorManager", reader, StringComparison.Ordinal);
        Assert.Contains("FabricationPart fabrication => fabrication.ConnectorManager", reader, StringComparison.Ordinal);
        Assert.Contains("connector.IsConnected", reader, StringComparison.Ordinal);
        Assert.Contains("ConnectorType.End", reader, StringComparison.Ordinal);
        Assert.Contains("ConnectorType.Curve", reader, StringComparison.Ordinal);
        Assert.Contains("ConnectorType.Physical", reader, StringComparison.Ordinal);
        Assert.Contains("ConnectorType.Logical", reader, StringComparison.Ordinal);
        Assert.DoesNotContain("MEPSystem", reader, StringComparison.Ordinal);
    }

    [Fact]
    public void Reader_classifies_connector_type_before_IsConnected()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "RevitMCP.Addin", "Topology", "RevitMepConnectorReader.cs"));
        var observe = SliceMethod(source, "private static ObservedConnector Observe(");

        var classIndex = observe.IndexOf("MapClass(connector.ConnectorType)", StringComparison.Ordinal);
        var gateIndex = observe.IndexOf("!PhysicalConnectionFilter.IsPhysicalClass(connectorClass)", StringComparison.Ordinal);
        var domainIndex = observe.IndexOf("MapDomain(connector.Domain)", StringComparison.Ordinal);
        var connectedIndex = observe.IndexOf("connector.IsConnected", StringComparison.Ordinal);
        var allRefsIndex = observe.IndexOf("connector.AllRefs", StringComparison.Ordinal);

        Assert.True(classIndex >= 0 && gateIndex > classIndex);
        Assert.True(domainIndex > gateIndex);
        Assert.True(connectedIndex > domainIndex);
        Assert.True(allRefsIndex > connectedIndex);

        var nonPhysicalPath = observe[gateIndex..domainIndex];
        Assert.Contains("return ", nonPhysicalPath, StringComparison.Ordinal);
        Assert.DoesNotContain("IsConnected", nonPhysicalPath, StringComparison.Ordinal);
        Assert.DoesNotContain("AllRefs", nonPhysicalPath, StringComparison.Ordinal);
        Assert.DoesNotContain(".Domain", nonPhysicalPath, StringComparison.Ordinal);

        var unsupportedDomainPath = observe[domainIndex..connectedIndex];
        Assert.Contains("return ", unsupportedDomainPath, StringComparison.Ordinal);
        Assert.DoesNotContain("IsConnected", unsupportedDomainPath, StringComparison.Ordinal);
        Assert.DoesNotContain("AllRefs", unsupportedDomainPath, StringComparison.Ordinal);

        var disconnectedPath = observe[connectedIndex..allRefsIndex];
        Assert.Contains("return ", disconnectedPath, StringComparison.Ordinal);
        Assert.DoesNotContain("AllRefs", disconnectedPath, StringComparison.Ordinal);
    }

    private static GetMepTopologyResult Traverse(
        IReadOnlyDictionary<string, ElementTopologyFacts> graph,
        IReadOnlyList<string> seeds,
        int depth = 3,
        int elements = 100,
        int edges = 200)
    {
        return MepTopologyTraversal.Traverse(
            "instance",
            "document",
            Request(seeds, depth, elements, edges),
            elementRef => graph[elementRef]);
    }

    private static void AssertParentEdges(GetMepTopologyResult result)
    {
        var depthByRef = result.Nodes.ToDictionary(node => node.ElementRef, node => node.Depth, StringComparer.Ordinal);
        foreach (var node in result.Nodes.Where(item => item.Depth > 0))
        {
            Assert.Contains(result.Edges, edge => ConnectsToParent(edge, node.ElementRef, node.Depth, depthByRef));
        }
    }

    private static bool ConnectsToParent(
        GetMepTopologyEdge edge,
        string elementRef,
        int depth,
        IReadOnlyDictionary<string, int> depthByRef)
    {
        var other = edge.ElementRefA == elementRef
            ? edge.ElementRefB
            : edge.ElementRefB == elementRef
                ? edge.ElementRefA
                : null;
        return other is not null && depthByRef[other] == depth - 1;
    }

    private static Dictionary<string, ElementTopologyFacts> GraphFrom(string seed, int neighborCount)
    {
        var neighbors = Enumerable.Range(1, neighborCount)
            .Select(index => Link($"n{index:00}", MepTopologyDomain.Hvac))
            .ToArray();
        var graph = new Dictionary<string, ElementTopologyFacts>(StringComparer.Ordinal)
        {
            [seed] = Ok(neighbors)
        };
        foreach (var neighbor in neighbors)
        {
            graph[neighbor.ElementRef] = Ok();
        }

        return graph;
    }

    private static ValidatedGetMepTopologyRequest Request(
        IReadOnlyList<string> seeds,
        int depth = 3,
        int elements = 100,
        int edges = 200)
    {
        return new ValidatedGetMepTopologyRequest("document", seeds, null, depth, elements, edges);
    }

    private static ElementTopologyFacts Ok(params PhysicalNeighbor[] neighbors)
    {
        return new ElementTopologyFacts(MepTopologySeedStatus.Ok, neighbors);
    }

    private static ElementTopologyFacts Missing()
    {
        return new ElementTopologyFacts(MepTopologySeedStatus.NotFound, []);
    }

    private static PhysicalNeighbor Link(string elementRef, MepTopologyDomain domain)
    {
        return new PhysicalNeighbor(elementRef, domain);
    }

    private static string EdgeText(GetMepTopologyEdge edge)
    {
        return $"{edge.ElementRefA}|{edge.ElementRefB}|{string.Join(",", edge.Domains)}";
    }

    private static ObservedConnector Connector(
        ObservedConnectorClass connectorClass,
        bool isConnected,
        MepTopologyDomain domain,
        params ObservedConnectorRef[] refs)
    {
        return new ObservedConnector(connectorClass, isConnected, domain, refs);
    }

    private static ObservedConnectorRef Neighbor(string elementRef, ObservedConnectorClass connectorClass)
    {
        return new ObservedConnectorRef(elementRef, connectorClass);
    }

    private static string SliceMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0);
        var next = source.IndexOf("\n    private static ", start + signature.Length, StringComparison.Ordinal);
        Assert.True(next > start);
        return source[start..next];
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "RevitMCP.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate RevitMCP.sln from the test output directory.");
    }
}
