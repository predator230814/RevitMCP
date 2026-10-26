namespace BimIt.Revit.Contracts;

public static class PatternDeviceRoles
{
    public static readonly string[] All =
    [
        "receptacle",
        "switch",
        "luminaire",
        "smoke_detector",
        "thermostat",
        "data",
        "other",
    ];

    public static bool IsRole(string? role) => role is not null && All.Contains(role, StringComparer.Ordinal);

    public static bool IsPlacement(string? placement) => placement is "boundary" or "interior";

    public static IReadOnlyList<string> PreferredCategories(string role)
    {
        return role switch
        {
            "receptacle" => ["OST_ElectricalFixtures"],
            "switch" => ["OST_LightingDevices"],
            "luminaire" => ["OST_LightingFixtures"],
            "data" => ["OST_DataDevices", "OST_CommunicationDevices", "OST_TelephoneDevices"],
            "smoke_detector" => ["OST_FireAlarmDevices"],
            _ => [],
        };
    }
}
