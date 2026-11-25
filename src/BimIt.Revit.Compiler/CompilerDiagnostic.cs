namespace BimIt.Revit.Compiler;

public sealed record CompilerDiagnostic(string Code, int Line, int Column, string Message)
{
    public static CompilerDiagnostic Create(string code, int line, int column, string message)
    {
        return new CompilerDiagnostic(code, line, column, Flatten(message));
    }

    public static string Flatten(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return "";
        }

        var buffer = new char[message.Length];
        var count = 0;
        var pendingSpace = false;
        foreach (var ch in message)
        {
            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = count > 0;
                continue;
            }

            if (pendingSpace && count < buffer.Length)
            {
                buffer[count++] = ' ';
                pendingSpace = false;
            }

            if (count < buffer.Length)
            {
                buffer[count++] = ch;
            }
        }

        var text = new string(buffer, 0, count);
        return text.Length <= CompilerProtocol.MaxDiagnosticMessageCharacters
            ? text
            : text[..CompilerProtocol.MaxDiagnosticMessageCharacters];
    }
}
