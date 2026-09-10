using System.Globalization;
using Autodesk.Revit.DB;

namespace RevitMCP.Addin.Compatibility;

internal static class RevitElementIds
{
    public static string Format(ElementId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return id.Value.ToString(CultureInfo.InvariantCulture);
    }
}
