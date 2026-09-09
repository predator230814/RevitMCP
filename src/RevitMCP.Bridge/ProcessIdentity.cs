namespace RevitMCP.Bridge;

internal static class ProcessIdentity
{
    public static bool StartTimesMatch(DateTimeOffset expectedUtc, DateTimeOffset actualUtc)
    {
        return Math.Abs((expectedUtc.ToUniversalTime() - actualUtc.ToUniversalTime()).TotalSeconds) < 1.0;
    }
}
