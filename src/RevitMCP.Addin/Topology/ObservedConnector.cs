using RevitMCP.Contracts;

namespace RevitMCP.Addin.Topology;

internal enum ObservedConnectorClass
{
    End,
    Curve,
    Physical,
    Logical,
    Reference,
    Family,
    Super,
    Other
}

internal readonly record struct ObservedConnectorRef(string? ElementRef, ObservedConnectorClass Class);

internal readonly record struct ObservedConnector(
    ObservedConnectorClass Class,
    bool IsConnected,
    MepTopologyDomain? Domain,
    IReadOnlyList<ObservedConnectorRef> Refs);

internal readonly record struct PhysicalNeighbor(string ElementRef, MepTopologyDomain Domain);

internal readonly record struct ElementTopologyFacts(
    MepTopologySeedStatus Status,
    IReadOnlyList<PhysicalNeighbor> Neighbors);
