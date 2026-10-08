namespace RevitMCP.Server;

internal static class ApplyParameterUpdatesToolMetadata
{
    public const string Name = "revit_apply_parameter_updates";

    public const string Title = "Apply Revit Parameter Updates";

    public const string Description =
        "Apply one previously previewed and locally approved immutable parameter-update batch in the exact addressed Revit process. Accepts no new values and no approval decision. Returns only the apply status.";
}
