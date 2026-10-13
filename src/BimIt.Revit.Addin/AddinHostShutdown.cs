using BimIt.Revit.Addin.Approval;
using BimIt.Revit.Addin.Assistant;
using BimIt.Revit.Addin.Lifecycle;

namespace BimIt.Revit.Addin;

internal static class AddinHostShutdown
{
    public static void Complete(
        AddinLifecycleCoordinator lifecycle,
        AssistantShell? assistant,
        ApprovalUiRuntime? approvalUi)
    {
        ArgumentNullException.ThrowIfNull(lifecycle);
        try
        {
            lifecycle.Shutdown();
        }
        finally
        {
            try
            {
                assistant?.ShutdownForHost();
            }
            catch (Exception)
            {
                // Assistant shutdown cannot fail Revit shutdown. ShutdownForHost still kills before it returns.
            }

            approvalUi?.Shutdown();
        }
    }
}
