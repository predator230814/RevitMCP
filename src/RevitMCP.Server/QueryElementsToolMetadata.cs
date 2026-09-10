namespace RevitMCP.Server;

internal static class QueryElementsToolMetadata
{
    public const string Name = "revit_query_elements";
    public const string Title = "Query Revit Elements";
    public const string Description =
        "Find elements in the active Revit document using bounded filters and return opaque element references. Use this before requesting details about matching elements.";

    public const int DefaultLimit = 50;
}
