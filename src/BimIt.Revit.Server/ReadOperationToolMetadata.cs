namespace BimIt.Revit.Server;

internal static class ReadOperationToolMetadata
{
    public const string Name = "revit_read";

    public const string Title = "Read a registered Revit operation";

    public const string Description =
        "Runs one registered read operation on the active host project. operation and contract_version come from revit_lookup; there is no default version. document_id comes from revit_get_context. inputs must match the operation schema from lookup. page_size and cursor are envelope fields for operations whose pagination is cursor, and they are rejected for pagination none. Follow next_cursor with the same operation, version, and inputs until complete is true.";
}
