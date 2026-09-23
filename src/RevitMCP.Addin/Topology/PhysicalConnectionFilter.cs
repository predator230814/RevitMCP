using RevitMCP.Contracts;

namespace RevitMCP.Addin.Topology;

internal static class PhysicalConnectionFilter
{
    public static bool IsPhysicalClass(ObservedConnectorClass value) =>
        value is ObservedConnectorClass.End or ObservedConnectorClass.Curve or ObservedConnectorClass.Physical;

    public static bool HasEligibleConnector(
        IReadOnlyList<ObservedConnector> connectors,
        MepTopologyDomain? domainFilter)
    {
        ArgumentNullException.ThrowIfNull(connectors);
        foreach (var connector in connectors)
        {
            if (IsEligible(connector, domainFilter))
            {
                return true;
            }
        }

        return false;
    }

    public static IReadOnlyList<PhysicalNeighbor> CollectNeighbors(
        string ownerRef,
        IReadOnlyList<ObservedConnector> connectors,
        MepTopologyDomain? domainFilter)
    {
        ArgumentNullException.ThrowIfNull(ownerRef);
        ArgumentNullException.ThrowIfNull(connectors);

        var neighbors = new List<PhysicalNeighbor>();
        foreach (var connector in connectors)
        {
            if (!connector.IsConnected || !IsEligible(connector, domainFilter) || connector.Domain is not MepTopologyDomain domain)
            {
                continue;
            }

            foreach (var reference in connector.Refs)
            {
                if (!IsPhysicalClass(reference.Class))
                {
                    continue;
                }

                if (string.IsNullOrEmpty(reference.ElementRef)
                    || string.Equals(reference.ElementRef, ownerRef, StringComparison.Ordinal))
                {
                    continue;
                }

                neighbors.Add(new PhysicalNeighbor(reference.ElementRef, domain));
            }
        }

        return neighbors;
    }

    public static ElementTopologyFacts Describe(
        string ownerRef,
        IReadOnlyList<ObservedConnector> connectors,
        MepTopologyDomain? domainFilter)
    {
        if (!HasEligibleConnector(connectors, domainFilter))
        {
            return new ElementTopologyFacts(MepTopologySeedStatus.NoConnectors, []);
        }

        return new ElementTopologyFacts(
            MepTopologySeedStatus.Ok,
            CollectNeighbors(ownerRef, connectors, domainFilter));
    }

    private static bool IsEligible(ObservedConnector connector, MepTopologyDomain? domainFilter)
    {
        if (!IsPhysicalClass(connector.Class) || connector.Domain is not MepTopologyDomain domain)
        {
            return false;
        }

        return domainFilter is null || domain == domainFilter;
    }
}
