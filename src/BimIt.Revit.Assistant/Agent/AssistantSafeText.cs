using System.Text.RegularExpressions;

namespace BimIt.Revit.Assistant;

internal static partial class AssistantSafeText
{
    public static string Clean(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var text = value.Trim().Replace("\r", " ").Replace("\n", " ");
        text = BearerPattern().Replace(text, "$1[redacted]");
        text = TokenPattern().Replace(text, "[redacted]");
        text = PathPattern().Replace(text, "[path]");
        if (text.Length > max)
        {
            text = text[..max];
        }

        return text;
    }

    [GeneratedRegex(@"(?i)(bearer\s+)\S+", RegexOptions.CultureInvariant)]
    private static partial Regex BearerPattern();

    [GeneratedRegex(@"(?i)\b(ghp_|gho_|github_pat_|sk-|xox[baprs]-)[A-Za-z0-9_\-]+", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();

    [GeneratedRegex(@"[A-Za-z]:\\[^\s""']+|\\\\[^\s""']+", RegexOptions.CultureInvariant)]
    private static partial Regex PathPattern();
}
