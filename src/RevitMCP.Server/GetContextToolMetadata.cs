namespace RevitMCP.Server;

internal static class GetContextToolMetadata
{
    public const string Name = "revit_get_context";
    public const string Title = "Get Revit Context";
    public const string Description =
        "Return the current application, active document, active view, and selection summary for a Revit instance. Use this when the target Revit context is unknown or may have changed.";
}
