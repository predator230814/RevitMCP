namespace RevitMCP.Server;

internal static class GetMepTopologyToolMetadata
{
    public const string Name = "revit_get_mep_topology";
    public const string Title = "Get Revit MEP Topology";
    public const string Description =
        "Traverse bounded physical MEP connectivity from known Revit elements and return a deterministic element-level graph.";
}
