namespace RevitMCP.Addin.Inspection;

internal static class ParameterValueTextBounder
{
    public const int MaxLength = 512;

    public static (string? Text, bool Truncated) Bound(string? value)
    {
        if (value is null)
        {
            return (null, false);
        }

        if (value.Length <= MaxLength)
        {
            return (value, false);
        }

        var length = MaxLength;
        if (char.IsHighSurrogate(value[length - 1]))
        {
            length--;
        }

        return (value[..length], true);
    }
}
