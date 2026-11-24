using System.Globalization;

namespace BimIt.Revit.QueryRuntimeSpike;

public sealed record QueryDiagnostic(string Severity, string Code, int Line, int Column, string Message)
{
    public string Format()
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Severity}:{Code}:{Line}:{Column}:{Message}");
    }

    public static QueryDiagnostic Error(string code, int line, int column, string message)
    {
        return new QueryDiagnostic("error", code, line, column, Flatten(message));
    }

    private static string Flatten(string message)
    {
        var flat = message.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
        return flat.Length <= 180 ? flat : flat[..180];
    }
}
