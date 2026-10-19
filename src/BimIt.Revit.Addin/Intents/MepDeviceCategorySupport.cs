namespace BimIt.Revit.Addin.Intents;

internal static class MepDeviceCategorySupport
{
    internal static readonly string[] SupportedTokens =
    [
        "OST_ElectricalFixtures",
        "OST_LightingDevices",
        "OST_LightingFixtures",
        "OST_DataDevices",
        "OST_CommunicationDevices",
        "OST_FireAlarmDevices",
        "OST_SecurityDevices",
        "OST_TelephoneDevices",
        "OST_NurseCallDevices",
        "OST_ElectricalEquipment",
        "OST_MechanicalEquipment",
        "OST_PlumbingFixtures",
        "OST_DuctTerminal"
    ];

    private static readonly HashSet<string> Supported = new(SupportedTokens, StringComparer.Ordinal);

    public static bool IsSupported(string? categoryToken)
    {
        return categoryToken is not null && Supported.Contains(categoryToken);
    }
}
