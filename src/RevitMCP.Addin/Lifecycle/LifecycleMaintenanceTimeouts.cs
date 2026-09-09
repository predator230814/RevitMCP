namespace RevitMCP.Addin.Lifecycle;

/// <summary>
/// Bounded local maintenance waits at the synchronous Revit lifecycle boundary.
/// These are not MCP, Bridge protocol, or capability timeouts.
/// </summary>
internal static class LifecycleMaintenanceTimeouts
{
    public static readonly TimeSpan Startup = TimeSpan.FromSeconds(5);

    public static readonly TimeSpan Shutdown = TimeSpan.FromSeconds(5);
}
