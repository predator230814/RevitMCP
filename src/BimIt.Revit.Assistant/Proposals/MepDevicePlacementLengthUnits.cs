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

    public static bool TryToMetres(string lengthUnit, double value, out double metres)
    {
        var factor = MetresPerUnit(lengthUnit);
        if (!double.IsFinite(factor) || !double.IsFinite(value))
        {
            metres = double.NaN;
            return false;
        }

        metres = value * factor;
        return double.IsFinite(metres);
    }

    public static bool TryFromMetres(string lengthUnit, double metres, out double value)
    {
        var factor = MetresPerUnit(lengthUnit);
        if (!double.IsFinite(factor) || factor == 0d || !double.IsFinite(metres))
        {
            value = double.NaN;
            return false;
        }

        value = metres / factor;
        return double.IsFinite(value);
    }

    private static double MetresPerUnit(string lengthUnit)
    {
        return lengthUnit switch
        {
            "mm" => 0.001,
            "cm" => 0.01,
            "m" => 1d,
            "in" => 0.0254,
            "ft" => 0.3048,
            _ => double.NaN,
        };
    }
}
