using Autodesk.Revit.DB;

namespace BimIt.Revit.Addin.CSharpQuery;

internal static class CSharpQueryElementRef
{
    public const string NullElement = "element-ref-null";

    public static string Reference(Element? element)
    {
        if (element is null || string.IsNullOrEmpty(element.UniqueId))
        {
            return NullElement;
        }

        return element.UniqueId;
    }
}
