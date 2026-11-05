namespace BimIt.Revit.Addin.Query;

/// <summary>
/// Filter-value rules shared by the ADR-0023 declarative query sources (CAP-0014 and CAP-0015),
/// so their category-token and name-filter semantics cannot drift. Each source keeps its own
/// error code and message.
/// </summary>
internal static class DeclarativeQueryFilterRules
{
    public const int MaxCategoryTokens = 20;

    public const int MaxCategoryTokenLength = 80;

    public const int MaxNameLength = 256;

    /// <summary>The token of a category that is null, unreadable, or not a defined <c>BuiltInCategory</c>.</summary>
    public const string UnknownCategoryToken = "unknown";

    /// <summary>Shape of a <c>BuiltInCategory</c> member name, checked before Revit execution.</summary>
    public static bool IsCategoryTokenShape(string? token)
    {
        if (token is null || token.Length is < 1 or > MaxCategoryTokenLength)
        {
            return false;
        }

        foreach (var character in token)
        {
            if (character is not ((>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 1 to <see cref="MaxCategoryTokens"/> distinct well-shaped tokens, in the caller's order;
    /// <see langword="null"/> when the list is invalid.
    /// </summary>
    public static IReadOnlyList<string>? CategoryTokens(IReadOnlyList<string>? tokens)
    {
        if (tokens is null || tokens.Count is < 1 or > MaxCategoryTokens)
        {
            return null;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var accepted = new List<string>(tokens.Count);
        foreach (var token in tokens)
        {
            if (!IsCategoryTokenShape(token) || !seen.Add(token))
            {
                return null;
            }

            accepted.Add(token);
        }

        return accepted;
    }

    public static bool AllDefined(IReadOnlyList<string>? tokens, IBuiltInCategoryCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (tokens is null)
        {
            return true;
        }

        foreach (var token in tokens)
        {
            if (!catalog.IsDefinedMember(token))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// A substring name filter: 1 to <see cref="MaxNameLength"/> characters, not whitespace-only,
    /// never trimmed. Matching is <see cref="StringComparison.OrdinalIgnoreCase"/>.
    /// </summary>
    public static bool IsNameFilter(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length is < 1 or > MaxNameLength)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!char.IsWhiteSpace(character))
            {
                return true;
            }
        }

        return false;
    }

    public static bool NameContains(string name, string filter)
    {
        return name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
