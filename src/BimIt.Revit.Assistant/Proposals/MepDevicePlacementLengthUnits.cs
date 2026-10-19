namespace BimIt.Revit.Assistant;

/// <summary>
/// Maps the proposal's closed length-unit labels onto the Forge type ids accepted by MEP placement preview.
/// </summary>
internal static class MepDevicePlacementLengthUnits
{
    public static bool TryForgeTypeId(string lengthUnit, out string forgeTypeId)
    {
        forgeTypeId = lengthUnit switch
        {
            "mm" => "autodesk.unit.unit:millimeters-1.0.1",
            "cm" => "autodesk.unit.unit:centimeters-1.0.1",
            "m" => "autodesk.unit.unit:meters-1.0.1",
            "in" => "autodesk.unit.unit:inches-1.0.1",
            "ft" => "autodesk.unit.unit:feet-1.0.1",
            _ => "",
        };
        return forgeTypeId.Length > 0;
    }
}
