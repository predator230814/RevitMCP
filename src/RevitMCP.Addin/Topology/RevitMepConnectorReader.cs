using Autodesk.Revit.DB;
using RevitMCP.Addin.Topology;
using RevitMCP.Contracts;
using RevitDomain = Autodesk.Revit.DB.Domain;

namespace RevitMCP.Addin.Topology;

internal static class RevitMepConnectorReader
{
    public static ElementTopologyFacts Read(Document document, string elementRef, MepTopologyDomain? domain)
    {
        ArgumentNullException.ThrowIfNull(document);
        var element = document.GetElement(elementRef);
        if (element is null || element is ElementType)
        {
            return new ElementTopologyFacts(MepTopologySeedStatus.NotFound, []);
        }

        var manager = TryGetManager(element);
        if (manager?.Connectors is not ConnectorSet connectors)
        {
            return new ElementTopologyFacts(MepTopologySeedStatus.NoConnectors, []);
        }

        var observations = new List<ObservedConnector>();
        foreach (var connector in Enumerate(connectors))
        {
            observations.Add(Observe(document, connector));
        }

        return PhysicalConnectionFilter.Describe(elementRef, observations, domain);
    }

    private static ConnectorManager? TryGetManager(Element element)
    {
        return element switch
        {
            MEPCurve curve => curve.ConnectorManager,
            FamilyInstance family => family.MEPModel?.ConnectorManager,
            FabricationPart fabrication => fabrication.ConnectorManager,
            _ => null
        };
    }

    private static ObservedConnector Observe(Document document, Connector connector)
    {
        var refs = new List<ObservedConnectorRef>();
        if (connector.AllRefs is ConnectorSet allRefs)
        {
            foreach (var referenced in Enumerate(allRefs))
            {
                refs.Add(new ObservedConnectorRef(ResolveOwnerRef(document, referenced), MapClass(referenced.ConnectorType)));
            }
        }

        return new ObservedConnector(
            MapClass(connector.ConnectorType),
            connector.IsConnected,
            MapDomain(connector.Domain),
            refs);
    }

    private static string? ResolveOwnerRef(Document document, Connector referenced)
    {
        if (referenced.Owner is not Element owner || !ReferenceEquals(owner.Document, document) || owner is ElementType)
        {
            return null;
        }

        return string.IsNullOrEmpty(owner.UniqueId) ? null : owner.UniqueId;
    }

    private static MepTopologyDomain? MapDomain(RevitDomain domain)
    {
        if (domain == RevitDomain.DomainHvac)
        {
            return MepTopologyDomain.Hvac;
        }

        if (domain == RevitDomain.DomainPiping)
        {
            return MepTopologyDomain.Piping;
        }

        if (domain == RevitDomain.DomainElectrical)
        {
            return MepTopologyDomain.Electrical;
        }

        if (domain == RevitDomain.DomainCableTrayConduit)
        {
            return MepTopologyDomain.CableTrayConduit;
        }

        return null;
    }

    private static ObservedConnectorClass MapClass(ConnectorType type)
    {
        if (type == ConnectorType.End)
        {
            return ObservedConnectorClass.End;
        }

        if (type == ConnectorType.Curve)
        {
            return ObservedConnectorClass.Curve;
        }

        if (type == ConnectorType.Physical)
        {
            return ObservedConnectorClass.Physical;
        }

        if (type == ConnectorType.Logical)
        {
            return ObservedConnectorClass.Logical;
        }

        if (type == ConnectorType.Reference)
        {
            return ObservedConnectorClass.Reference;
        }

        if (type == ConnectorType.Family)
        {
            return ObservedConnectorClass.Family;
        }

        if (type == ConnectorType.Super)
        {
            return ObservedConnectorClass.Super;
        }

        return ObservedConnectorClass.Other;
    }

    private static IEnumerable<Connector> Enumerate(ConnectorSet connectors)
    {
        var iterator = connectors.ForwardIterator();
        while (iterator.MoveNext())
        {
            if (iterator.Current is Connector connector)
            {
                yield return connector;
            }
        }
    }
}
