namespace BimIt.Revit.Server;

internal static class QueryParameterBindingsToolMetadata
{
    public const string Name = "revit_query_parameter_bindings";

    public const string Title = "Query Revit Parameter Bindings";

    public const string Description =
        "Read the project parameter bindings of the active host project: one row per bound definition, with identity, data type, instance or type binding, and all bound categories, plus a summary of the whole matched set. Results are paged: repeat the same query with next_cursor until complete is true. complete is the authoritative end of a traversal, and summary describes the entire matched set on every page, not the current page. document_id comes from revit_get_context. parameter_ref is the same reference that revit_describe_parameters and revit_get_parameter_values use. Category filters use stable OST_ tokens and name_contains is a case-insensitive substring. A category filter selects rows by any matching category, but every returned row still lists its complete bound-category set.";
}
