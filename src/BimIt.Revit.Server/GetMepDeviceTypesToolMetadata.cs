namespace BimIt.Revit.Server;

internal static class GetMepDeviceTypesToolMetadata
{
    public const string Name = "revit_get_mep_device_types";

    public const string Title = "Get Loaded MEP Device Types";

    public const string Description =
        "Read a bounded catalog of loaded FamilySymbol types that BIM-IT can place as MEP devices. Filters use stable category tokens such as OST_ElectricalFixtures. This does not load families or modify Revit.";
}
