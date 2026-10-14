using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BimIt.Revit.Addin.Assistant;

internal sealed class AssistantPaneProvider : IDockablePaneProvider
{
    private readonly AssistantShell _shell;

    public AssistantPaneProvider(AssistantShell shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _shell = shell;
    }

    public void SetupDockablePane(DockablePaneProviderData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var host = new AssistantPaneHost(_shell);
        _shell.AttachPane(host);
        data.FrameworkElement = host;
        data.VisibleByDefault = false;
        data.InitialState = new DockablePaneState
        {
            DockPosition = DockPosition.Right,
        };
    }
}

internal static class AssistantRibbonRegistration
{
    public static void Register(UIControlledApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);
        if (AssistantRibbon.StartsAssistantWhenRegistered)
        {
            throw new InvalidOperationException("Ribbon registration must not start the Assistant.");
        }

        try
        {
            application.CreateRibbonTab(AssistantRibbon.TabName);
        }
        catch (Exception)
        {
            // The tab already exists for this Revit session.
        }

        var panel = application.CreateRibbonPanel(AssistantRibbon.TabName, AssistantRibbon.PanelName);
        var button = new PushButtonData(
            AssistantRibbon.ButtonId,
            AssistantRibbon.ButtonText,
            typeof(ShowBimItAssistantCommand).Assembly.Location,
            typeof(ShowBimItAssistantCommand).FullName);
        button.ToolTip = "Open BIM-IT Assistant";
        panel.AddItem(button);
    }
}

[Transaction(TransactionMode.Manual)]
public sealed class ShowBimItAssistantCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        ArgumentNullException.ThrowIfNull(commandData);
        AssistantCommandBridge.Show(commandData.Application);
        return Result.Succeeded;
    }
}

internal static class AssistantCommandBridge
{
    private static AssistantShell? _shell;

    public static void Attach(AssistantShell shell)
    {
        _shell = shell;
    }

    public static void Show(UIApplication application)
    {
        _shell?.Show(application);
    }
}
