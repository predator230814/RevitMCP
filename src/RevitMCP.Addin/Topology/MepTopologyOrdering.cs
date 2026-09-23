using RevitMCP.Contracts;

namespace RevitMCP.Addin.Topology;

internal static class MepTopologyOrdering
{
    public static IComparer<MepTopologyDomain> DomainComparer { get; } = Comparer<MepTopologyDomain>.Create(
        (left, right) => string.CompareOrdinal(DomainName(left), DomainName(right)));

    public static string DomainName(MepTopologyDomain domain) => domain switch
    {
        MepTopologyDomain.CableTrayConduit => "cable_tray_conduit",
        MepTopologyDomain.Electrical => "electrical",
        MepTopologyDomain.Hvac => "hvac",
        MepTopologyDomain.Piping => "piping",
        _ => domain.ToString()
    };

    public static string ReasonName(MepTopologyTruncationReason reason) => reason switch
    {
        MepTopologyTruncationReason.Depth => "depth",
        MepTopologyTruncationReason.Edges => "edges",
        MepTopologyTruncationReason.Elements => "elements",
        _ => reason.ToString()
    };
}
