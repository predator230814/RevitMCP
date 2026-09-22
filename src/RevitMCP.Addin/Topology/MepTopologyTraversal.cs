using RevitMCP.Addin.Inspection;
using RevitMCP.Contracts;

namespace RevitMCP.Addin.Topology;

internal static class MepTopologyTraversal
{
    public static GetMepTopologyResult Traverse(
        string instanceId,
        string documentId,
        ValidatedGetMepTopologyRequest request,
        Func<string, ElementTopologyFacts> inspect)
    {
        ArgumentNullException.ThrowIfNull(instanceId);
        ArgumentNullException.ThrowIfNull(documentId);
        ArgumentNullException.ThrowIfNull(inspect);

        var cache = new Dictionary<string, ElementTopologyFacts>(StringComparer.Ordinal);
        ElementTopologyFacts Inspect(string elementRef)
        {
            if (!cache.TryGetValue(elementRef, out var facts))
            {
                facts = inspect(elementRef);
                cache[elementRef] = facts;
            }

            return facts;
        }

        var seeds = new List<GetMepTopologySeed>(request.SeedElementRefs.Count);
        var nodes = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var seed in request.SeedElementRefs)
        {
            var facts = Inspect(seed);
            seeds.Add(new GetMepTopologySeed { ElementRef = seed, Status = facts.Status });
            if (facts.Status == MepTopologySeedStatus.Ok)
            {
                nodes[seed] = 0;
            }
        }

        var edges = new Dictionary<EdgeKey, SortedSet<MepTopologyDomain>>();
        var refused = new HashSet<EdgeKey>();
        var reasons = new HashSet<MepTopologyTruncationReason>();
        var pending = new SortedSet<PendingNode>(PendingNodeComparer.Instance);
        var queued = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in nodes)
        {
            pending.Add(new PendingNode(pair.Value, pair.Key));
            queued.Add(pair.Key);
        }

        while (pending.Count > 0)
        {
            var current = pending.Min;
            pending.Remove(current);
            var facts = Inspect(current.ElementRef);
            foreach (var (neighbor, domains) in Group(facts.Neighbors))
            {
                if (string.Equals(neighbor, current.ElementRef, StringComparison.Ordinal))
                {
                    continue;
                }

                if (nodes.ContainsKey(neighbor))
                {
                    TryEmit(current.ElementRef, neighbor, domains, edges, refused, request.MaxEdges, reasons);
                    continue;
                }

                var candidateDepth = current.Depth + 1;
                if (candidateDepth > request.MaxDepth)
                {
                    reasons.Add(MepTopologyTruncationReason.Depth);
                    continue;
                }

                if (nodes.Count >= request.MaxElements)
                {
                    reasons.Add(MepTopologyTruncationReason.Elements);
                    continue;
                }

                if (!TryEmit(current.ElementRef, neighbor, domains, edges, refused, request.MaxEdges, reasons))
                {
                    continue;
                }

                nodes[neighbor] = candidateDepth;
                if (queued.Add(neighbor))
                {
                    pending.Add(new PendingNode(candidateDepth, neighbor));
                }
            }
        }

        var orderedReasons = reasons
            .OrderBy(MepTopologyOrdering.ReasonName, StringComparer.Ordinal)
            .ToArray();
        return new GetMepTopologyResult
        {
            Context = new GetMepTopologyContext
            {
                InstanceId = instanceId,
                DocumentId = documentId
            },
            Seeds = seeds,
            Nodes = nodes
                .OrderBy(pair => pair.Value)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new GetMepTopologyNode { ElementRef = pair.Key, Depth = pair.Value })
                .ToArray(),
            Edges = edges
                .OrderBy(pair => pair.Key.A, StringComparer.Ordinal)
                .ThenBy(pair => pair.Key.B, StringComparer.Ordinal)
                .Select(pair => new GetMepTopologyEdge
                {
                    ElementRefA = pair.Key.A,
                    ElementRefB = pair.Key.B,
                    Domains = pair.Value.ToArray()
                })
                .ToArray(),
            Truncated = orderedReasons.Length > 0,
            TruncationReasons = orderedReasons
        };
    }

    private static List<(string Neighbor, MepTopologyDomain[] Domains)> Group(IReadOnlyList<PhysicalNeighbor> neighbors)
    {
        var grouped = new SortedDictionary<string, SortedSet<MepTopologyDomain>>(StringComparer.Ordinal);
        foreach (var neighbor in neighbors)
        {
            if (!grouped.TryGetValue(neighbor.ElementRef, out var domains))
            {
                domains = new SortedSet<MepTopologyDomain>(MepTopologyOrdering.DomainComparer);
                grouped[neighbor.ElementRef] = domains;
            }

            domains.Add(neighbor.Domain);
        }

        return grouped.Select(pair => (pair.Key, pair.Value.ToArray())).ToList();
    }

    private static bool TryEmit(
        string left,
        string right,
        IReadOnlyList<MepTopologyDomain> domains,
        Dictionary<EdgeKey, SortedSet<MepTopologyDomain>> edges,
        HashSet<EdgeKey> refused,
        int maxEdges,
        HashSet<MepTopologyTruncationReason> reasons)
    {
        var key = EdgeKey.Canonical(left, right);
        if (refused.Contains(key))
        {
            reasons.Add(MepTopologyTruncationReason.Edges);
            return false;
        }

        if (edges.TryGetValue(key, out var existing))
        {
            foreach (var domain in domains)
            {
                existing.Add(domain);
            }

            return true;
        }

        if (edges.Count >= maxEdges)
        {
            refused.Add(key);
            reasons.Add(MepTopologyTruncationReason.Edges);
            return false;
        }

        var created = new SortedSet<MepTopologyDomain>(MepTopologyOrdering.DomainComparer);
        foreach (var domain in domains)
        {
            created.Add(domain);
        }

        edges[key] = created;
        return true;
    }

    private readonly record struct EdgeKey(string A, string B)
    {
        public static EdgeKey Canonical(string left, string right)
        {
            return string.CompareOrdinal(left, right) <= 0
                ? new EdgeKey(left, right)
                : new EdgeKey(right, left);
        }
    }

    private readonly record struct PendingNode(int Depth, string ElementRef);

    private sealed class PendingNodeComparer : IComparer<PendingNode>
    {
        public static PendingNodeComparer Instance { get; } = new();

        public int Compare(PendingNode x, PendingNode y)
        {
            var depth = x.Depth.CompareTo(y.Depth);
            if (depth != 0)
            {
                return depth;
            }

            var element = string.CompareOrdinal(x.ElementRef, y.ElementRef);
            return element == 0 ? 0 : element;
        }
    }
}
