namespace BimIt.Revit.Server;

internal static class QueryFamilyTypesToolMetadata
{
    public const string Name = "revit_query_family_types";

    public const string Title = "Query Revit Family Types";

    public const string Description =
        "Read FamilySymbol types in the active host project, with placed-instance counts and a summary of the whole matched set. document_id comes from revit_get_context. Category filters use stable OST_ tokens. Name filters are case-insensitive substrings. Results are paged: repeat the same query with next_cursor until complete is true. complete is the authoritative end of a traversal. summary describes the entire matched set on every page, not the current page, and its family counts are relative to that set. unused_family_count counts matched families whose matched types are all unplaced, so for an unfiltered used/unused family classification use usage=all with no name filters, optionally narrowed by category_tokens. placed_instance_count counts instances in the active host project only; linked-document instances are excluded.";
}
