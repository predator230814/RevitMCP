using System.Text;

namespace BimIt.Revit.Addin.ReadOperations;

/// <summary>
/// CAP-0017 plural equivalence. Tokens are not stemmed and characters are not stripped.
/// </summary>
internal static class RevitApiTokens
{
    public static IReadOnlyList<string> Tokenize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var tokens = new List<string>();
        var current = new StringBuilder();
        var previous = '\0';
        var hasPrevious = false;
        foreach (var character in text)
        {
            var boundary = current.Length > 0
                && char.IsUpper(character)
                && hasPrevious
                && (char.IsLower(previous) || char.IsDigit(previous));
            if (boundary || !IsAsciiLetterOrDigit(character))
            {
                Add(tokens, current);
            }

            if (IsAsciiLetterOrDigit(character))
            {
                current.Append(char.ToLowerInvariant(character));
            }

            previous = character;
            hasPrevious = true;
        }

        Add(tokens, current);
        return tokens;
    }

    public static bool Matches(string queryToken, string apiToken)
    {
        if (queryToken.Length == 0 || apiToken.Length == 0)
        {
            return false;
        }

        if (string.Equals(queryToken, apiToken, StringComparison.Ordinal)
            || string.Equals(queryToken, apiToken + "s", StringComparison.Ordinal)
            || string.Equals(queryToken, apiToken + "es", StringComparison.Ordinal))
        {
            return true;
        }

        return apiToken[^1] == 'y'
            && string.Equals(queryToken, apiToken[..^1] + "ies", StringComparison.Ordinal);
    }

    public static bool MatchesAll(IReadOnlyList<string> queryTokens, IReadOnlyList<string> apiTokens)
    {
        if (queryTokens.Count == 0)
        {
            return false;
        }

        foreach (var queryToken in queryTokens)
        {
            var matched = false;
            foreach (var apiToken in apiTokens)
            {
                if (Matches(queryToken, apiToken))
                {
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                return false;
            }
        }

        return true;
    }

    private static void Add(List<string> tokens, StringBuilder current)
    {
        if (current.Length == 0)
        {
            return;
        }

        tokens.Add(current.ToString());
        current.Clear();
    }

    private static bool IsAsciiLetterOrDigit(char character)
    {
        return character is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9');
    }
}
