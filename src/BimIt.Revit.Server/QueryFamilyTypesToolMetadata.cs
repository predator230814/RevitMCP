namespace BimIt.Revit.Server;

internal static class QueryFamilyTypesToolMetadata
{
    public const string Name = "revit_query_family_types";

    public const string Title = "Query Revit Family Types";

    public const string Description =
        "Read FamilySymbol types in the active host project, with placed-instance counts and a summary of the whole matched set. Results are paged: repeat the same query with next_cursor until complete is true. document_id comes from revit_get_context. Category filters use stable OST_ tokens. Name filters are case-insensitive substrings.";
}
