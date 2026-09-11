using System.Globalization;
using Autodesk.Revit.DB;

namespace RevitMCP.Addin.Inspection;

internal static class RevitParameterValueReader
{
    public static string? Read(Parameter parameter, Document document)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        ArgumentNullException.ThrowIfNull(document);

        if (!parameter.HasValue)
        {
            return null;
        }

        return parameter.StorageType switch
        {
            StorageType.String => parameter.AsString(),
            StorageType.Integer => ReadInteger(parameter),
            StorageType.Double => ReadDouble(parameter),
            StorageType.ElementId => ReadElementId(parameter, document),
            _ => null
        };
    }

    private static string? ReadInteger(Parameter parameter)
    {
        var formatted = parameter.AsValueString();
        return string.IsNullOrEmpty(formatted)
            ? parameter.AsInteger().ToString(CultureInfo.InvariantCulture)
            : formatted;
    }

    private static string? ReadDouble(Parameter parameter)
    {
        var formatted = parameter.AsValueString();
        return string.IsNullOrEmpty(formatted) ? null : formatted;
    }

    private static string? ReadElementId(Parameter parameter, Document document)
    {
        var referencedId = parameter.AsElementId();
        if (referencedId == ElementId.InvalidElementId)
        {
            return null;
        }

        var referenced = document.GetElement(referencedId);
        return ElementBasicMetadataResolver.NullIfEmpty(referenced?.Name);
    }
}
