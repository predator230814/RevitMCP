namespace RevitMCP.Addin.Lifecycle;

internal enum AddinLifecycleState
{
    Created,
    BootstrapPending,
    Starting,
    Ready,
    Faulted,
    Stopping,
    Stopped
}
