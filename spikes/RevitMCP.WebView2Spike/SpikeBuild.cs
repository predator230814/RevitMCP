namespace RevitMCP.WebView2Spike;

internal static class SpikeBuild
{
#if REVIT2025
    public const string RevitYear = "2025";
#elif REVIT2026
    public const string RevitYear = "2026";
#elif REVIT2027
    public const string RevitYear = "2027";
#else
    public const string RevitYear = "unknown";
#endif
}
