using System.Globalization;

namespace BimIt.Revit.Addin.Intents;

internal static class MepLengthUnitLabel
{
    public static string Describe(string lengthUnitId)
    {
        return lengthUnitId switch
        {
            "autodesk.unit.unit:millimeters-1.0.1" => "mm",
            "autodesk.unit.unit:centimeters-1.0.1" => "cm",
            "autodesk.unit.unit:decimeters-1.0.1" => "dm",
            "autodesk.unit.unit:meters-1.0.0" => "m",
            "autodesk.unit.unit:meters-1.0.1" => "m",
            "autodesk.unit.unit:feet-1.0.1" => "ft",
            "autodesk.unit.unit:inches-1.0.1" => "in",
            "autodesk.unit.unit:feetFractionalInches-1.0.1" => "ft",
            _ => lengthUnitId
        };
    }

    public static string Signed(double value)
    {
        var text = value.ToString("0.################", CultureInfo.InvariantCulture);
        return value < 0 ? text : "+" + text;
    }
}
