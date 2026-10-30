using System.Text;
using System.Text.RegularExpressions;

namespace BimIt.Revit.Skills;

/// <summary>
/// Reads the YAML front matter at the top of SKILL.md. Only the portable Agent Skills shape is needed: top-level
/// scalar keys, quoted strings, and folded or literal block scalars. Nested maps and lists, such as
/// <c>metadata</c>, are recognised and skipped. Anything else fails closed.
/// </summary>
public static partial class SkillFrontMatter
{
    public sealed record Parsed(IReadOnlyDictionary<string, string?> Values, string Body);

    public static bool TryParse(string text, out Parsed? parsed, out string? error)
    {
        parsed = null;
        error = null;
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        if (lines.Length == 0 || lines[0].TrimEnd() != "---")
        {
            error = "SKILL.md must begin with YAML front matter (---).";
            return false;
        }

        var end = -1;
        var characters = 0;
        for (var index = 1; index < lines.Length; index++)
        {
            characters += lines[index].Length + 1;
            if (characters > SkillLimits.MaxFrontMatterCharacters)
            {
                error = "The SKILL.md front matter is too long.";
                return false;
            }

            var trimmed = lines[index].TrimEnd();
            if (trimmed is "---" or "...")
            {
                end = index;
                break;
            }
        }

        if (end < 0)
        {
            error = "The SKILL.md front matter is not closed with ---.";
            return false;
        }

        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        var line = 1;
        while (line < end)
        {
            var current = lines[line];
            if (string.IsNullOrWhiteSpace(current) || current.TrimStart().StartsWith('#'))
            {
                line++;
                continue;
            }

            if (char.IsWhiteSpace(current[0]))
            {
                error = "The SKILL.md front matter has an unexpected indented line.";
                return false;
            }

            var match = KeyLine().Match(current);
            if (!match.Success)
            {
                error = "The SKILL.md front matter has a line that is not key: value.";
                return false;
            }

            var key = match.Groups[1].Value;
            if (values.ContainsKey(key))
            {
                error = "The SKILL.md front matter repeats the key '" + key + "'.";
                return false;
            }

            var raw = match.Groups[2].Value.Trim();
            line++;
            var continuation = new List<string>();
            while (line < end && (lines[line].Length == 0 || char.IsWhiteSpace(lines[line][0])))
            {
                continuation.Add(lines[line]);
                line++;
            }

            if (!TryValue(raw, continuation, out var value, out error))
            {
                error = "The SKILL.md front matter value for '" + key + "' could not be read: " + error;
                return false;
            }

            values[key] = value;
        }

        var body = string.Join("\n", lines.Skip(end + 1));
        parsed = new Parsed(values, body);
        return true;
    }

    private static bool TryValue(string raw, List<string> continuation, out string? value, out string? error)
    {
        value = null;
        error = null;
        var content = continuation.Where(text => !string.IsNullOrWhiteSpace(text)).ToList();
        if (raw.Length == 0)
        {
            // A nested map or list. Its content is not used by BIM-IT.
            return true;
        }

        if (raw[0] is '|' or '>')
        {
            if (!BlockHeader().IsMatch(raw))
            {
                error = "unsupported block scalar header";
                return false;
            }

            var indent = content.Count == 0 ? 0 : content.Min(text => text.Length - text.TrimStart().Length);
            var trimmed = continuation
                .Select(text => text.Length >= indent ? text[indent..] : text.TrimStart())
                .ToList();
            while (trimmed.Count > 0 && trimmed[^1].Length == 0)
            {
                trimmed.RemoveAt(trimmed.Count - 1);
            }

            value = raw[0] == '|'
                ? string.Join("\n", trimmed)
                : Fold(trimmed);
            return true;
        }

        if (raw[0] == '"')
        {
            return TryDoubleQuoted(raw, content, out value, out error);
        }

        if (raw[0] == '\'')
        {
            return TrySingleQuoted(raw, content, out value, out error);
        }

        if (raw[0] is '[' or '{')
        {
            // A flow list or map. Not used by BIM-IT.
            return true;
        }

        var plain = StripComment(raw);
        if (content.Count > 0)
        {
            plain = string.Join(" ", new[] { plain }.Concat(content.Select(text => StripComment(text.Trim()))));
        }

        value = plain.Trim();
        return true;
    }

    private static string Fold(IReadOnlyList<string> lines)
    {
        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            if (line.Length == 0)
            {
                builder.Append('\n');
                continue;
            }

            if (builder.Length > 0 && builder[^1] != '\n')
            {
                builder.Append(' ');
            }

            builder.Append(line.Trim());
        }

        return builder.ToString();
    }

    private static bool TryDoubleQuoted(string raw, List<string> content, out string? value, out string? error)
    {
        value = null;
        error = null;
        var text = content.Count == 0 ? raw : raw + " " + string.Join(" ", content.Select(line => line.Trim()));
        var builder = new StringBuilder();
        for (var index = 1; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '"')
            {
                if (StripComment(text[(index + 1)..]).Trim().Length != 0)
                {
                    error = "text after the closing quote";
                    return false;
                }

                value = builder.ToString();
                return true;
            }

            if (character == '\\' && index + 1 < text.Length)
            {
                index++;
                builder.Append(text[index] switch
                {
                    'n' => '\n',
                    't' => '\t',
                    '"' => '"',
                    '\\' => '\\',
                    '/' => '/',
                    _ => text[index],
                });
                continue;
            }

            builder.Append(character);
        }

        error = "the quote is not closed";
        return false;
    }

    private static bool TrySingleQuoted(string raw, List<string> content, out string? value, out string? error)
    {
        value = null;
        error = null;
        var text = content.Count == 0 ? raw : raw + " " + string.Join(" ", content.Select(line => line.Trim()));
        var builder = new StringBuilder();
        for (var index = 1; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '\'')
            {
                if (index + 1 < text.Length && text[index + 1] == '\'')
                {
                    builder.Append('\'');
                    index++;
                    continue;
                }

                if (StripComment(text[(index + 1)..]).Trim().Length != 0)
                {
                    error = "text after the closing quote";
                    return false;
                }

                value = builder.ToString();
                return true;
            }

            builder.Append(character);
        }

        error = "the quote is not closed";
        return false;
    }

    private static string StripComment(string text)
    {
        var index = text.IndexOf(" #", StringComparison.Ordinal);
        return index < 0 ? text : text[..index];
    }

    [GeneratedRegex("^([A-Za-z0-9_-]{1,64}):(?:\\s+(.*)|)$")]
    private static partial Regex KeyLine();

    [GeneratedRegex("^[|>][+-]?$")]
    private static partial Regex BlockHeader();
}
