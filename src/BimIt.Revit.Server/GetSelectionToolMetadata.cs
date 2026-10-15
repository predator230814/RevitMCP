namespace BimIt.Revit.Server;

internal static class GetSelectionToolMetadata
{
    public const string Name = "revit_get_selection";
    public const string Title = "Get Revit Selection";
    public const string Description =
        "Return a bounded deterministic snapshot of elements currently selected in the active Revit document as opaque element references.";
}
