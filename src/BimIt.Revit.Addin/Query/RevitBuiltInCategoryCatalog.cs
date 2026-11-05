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

/// <summary>
/// The category token of CAP-0014 and CAP-0015: the exact <c>BuiltInCategory</c> member name,
/// or <c>unknown</c> for a null, unreadable, or undefined category. Never throws.
/// </summary>
internal static class RevitCategoryTokens
{
    public static string Read(Category? category)
    {
        if (category is null)
        {
            return DeclarativeQueryFilterRules.UnknownCategoryToken;
        }

        try
        {
            var builtIn = category.BuiltInCategory;
            if (builtIn == BuiltInCategory.INVALID || !Enum.IsDefined(builtIn))
            {
                return DeclarativeQueryFilterRules.UnknownCategoryToken;
            }

            return Enum.GetName(builtIn) ?? DeclarativeQueryFilterRules.UnknownCategoryToken;
        }
        catch (Exception)
        {
            return DeclarativeQueryFilterRules.UnknownCategoryToken;
        }
    }
}
