namespace BimIt.Revit.Server;

internal static class QueryElementsToolMetadata
{
    public const string Name = "revit_query_elements";
    public const string Title = "Query Revit Elements";
    public const string Description =
        "Find elements in the active Revit document and return opaque element references. Coarse filters narrow candidates by category, family, type, level, and name. Optional predicates apply typed parameter conditions to those candidates. Document scope requires at least one coarse filter or predicate. Active view accepts an empty filter object and stays bounded by limit. Use this before requesting details about matching elements.";

    public const int DefaultLimit = 50;
}
