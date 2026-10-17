namespace BimIt.Revit.Server;

internal static class ApplyMepDevicePlacementsToolMetadata
{
    public const string Name = "revit_apply_mep_device_placements";

    public const string Title = "Apply MEP Device Placements";

    public const string Description =
        "Create one previously previewed and locally approved immutable batch of MEP devices in the exact addressed Revit process. Accepts no new coordinates and no approval decision. Returns the apply status and, when applied, the created element references.";
}
