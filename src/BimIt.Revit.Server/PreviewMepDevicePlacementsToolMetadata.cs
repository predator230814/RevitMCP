namespace BimIt.Revit.Server;

internal static class PreviewMepDevicePlacementsToolMetadata
{
    public const string Name = "revit_preview_mep_device_placements";

    public const string Title = "Preview MEP Device Placements";

    public const string Description =
        "Validate one bounded batch of same-wall MEP device placements relative to an existing wall-hosted device, and create an immutable ephemeral intent when every placement is eligible. Does not modify the Revit model.";
}
