using RevitMCP.Contracts;

namespace RevitMCP.Addin.Query;

internal static class QueryElementMatcher
{
    public static bool Matches(QueryElementCandidate candidate, QueryElementFilters filters)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(filters);

        if (!MatchesAny(candidate.CategoryName, filters.CategoryNames))
        {
            return false;
        }

        if (!MatchesAny(candidate.FamilyName, filters.FamilyNames))
        {
            return false;
        }

        if (!MatchesAny(candidate.TypeName, filters.TypeNames))
        {
            return false;
        }

        if (!MatchesAny(candidate.LevelName, filters.LevelNames))
        {
            return false;
        }

        return MatchesText(candidate, filters.TextContains);
    }

    private static bool MatchesAny(string? actual, IReadOnlyList<string>? expected)
    {
        if (expected is null)
        {
            return true;
        }

        if (actual is null)
        {
            return false;
        }

        foreach (var value in expected)
        {
            if (string.Equals(actual, value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesText(QueryElementCandidate candidate, string? text)
    {
        if (text is null)
        {
            return true;
        }

        return Contains(candidate.ElementName, text)
            || Contains(candidate.FamilyName, text)
            || Contains(candidate.TypeName, text);
    }

    private static bool Contains(string? value, string text)
    {
        return value is not null
            && value.Contains(text, StringComparison.OrdinalIgnoreCase);
    }
}
