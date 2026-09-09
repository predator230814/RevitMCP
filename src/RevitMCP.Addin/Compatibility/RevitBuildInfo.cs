namespace RevitMCP.Addin.Compatibility;

/// <summary>
/// Build-selected Revit year for this add-in compilation. Version-specific API
/// differences belong in this compatibility area, not in MCP-facing code.
/// </summary>
public static class RevitBuildInfo
{
    public const int RevitYear =
#if REVIT2025
        2025;
#elif REVIT2026
        2026;
#elif REVIT2027
        2027;
#else
#error RevitMCP.Addin must be compiled with REVIT2025, REVIT2026, or REVIT2027.
#endif
}
