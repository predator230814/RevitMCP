using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitMCP.WebView2Spike;

[Transaction(TransactionMode.ReadOnly)]
public sealed class ShowSpikePaneCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        return SpikePaneCommands.SetVisible(commandData.Application, visible: true, ref message);
    }
}

[Transaction(TransactionMode.ReadOnly)]
public sealed class HideSpikePaneCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        return SpikePaneCommands.SetVisible(commandData.Application, visible: false, ref message);
    }
}

internal static class SpikePaneCommands
{
    public static Result SetVisible(UIApplication application, bool visible, ref string message)
    {
        DockablePane pane;
        try
        {
            pane = application.GetDockablePane(new DockablePaneId(SpikeIds.PaneGuid));
        }
        catch (Exception)
        {
            message = SpikeShowDiagnostics.Message(SpikeShowStep.NotRegistered);
            return Result.Failed;
        }

        if (!visible)
        {
            try
            {
                if (pane.IsShown())
                {
                    pane.Hide();
                }

                return Result.Succeeded;
            }
            catch (Exception)
            {
                message = "The RevitMCP UI spike pane could not be hidden.";
                return Result.Failed;
            }
        }

        SpikePaneLifetime.RequestShow();
        try
        {
            pane.Show();
        }
        catch (Exception)
        {
            message = SpikeShowDiagnostics.Message(SpikeShowStep.ShowFailed);
            return Result.Failed;
        }

        try
        {
            SpikePaneLifetime.BeginAfterShow();
            return Result.Succeeded;
        }
        catch (Exception)
        {
            message = SpikeShowDiagnostics.Message(SpikeShowStep.WebViewCreationFailed);
            return Result.Failed;
        }
    }
}
