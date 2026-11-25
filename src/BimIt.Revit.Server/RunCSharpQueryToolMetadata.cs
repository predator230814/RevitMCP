namespace BimIt.Revit.Server;

internal static class RunCSharpQueryToolMetadata
{
    public const string Name = "revit_run_csharp_query";

    public const string Title = "Run bounded Revit C# query";

    public const string Description =
        "Runs one bounded read-only C# method body against the active Revit project. Prefer existing MCP primitives, or revit_lookup and revit_read, when they can answer the question. Use revit_api.symbols only when an API signature is uncertain. document_id comes from revit_get_context. The local Revit opt-in must be enabled. The source sees only the accepted host surface: doc, elementRef, and the fixed usings. This is not a general-purpose script runner and does not authorize writes.";
}
