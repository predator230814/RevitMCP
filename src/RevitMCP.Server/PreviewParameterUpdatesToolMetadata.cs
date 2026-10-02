namespace RevitMCP.Server;

internal static class PreviewParameterUpdatesToolMetadata
{
    public const string Name = "revit_preview_parameter_updates";

    public const string Title = "Preview Revit Parameter Updates";

    public const string Description =
        "Validate and preview one bounded batch of proposed instance-parameter updates in the active project document, and create an immutable ephemeral intent when every update is eligible. Does not modify the Revit model.";
}
