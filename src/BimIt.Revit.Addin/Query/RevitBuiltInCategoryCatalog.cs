using Autodesk.Revit.DB;

namespace BimIt.Revit.Addin.Query;

internal sealed class RevitBuiltInCategoryCatalog : IBuiltInCategoryCatalog
{
    public bool IsDefinedMember(string token)
    {
        if (!Enum.TryParse<BuiltInCategory>(token, ignoreCase: false, out var value))
        {
            return false;
        }

        if (value == BuiltInCategory.INVALID || !Enum.IsDefined(value))
        {
            return false;
        }

        return string.Equals(Enum.GetName(value), token, StringComparison.Ordinal);
    }
}
