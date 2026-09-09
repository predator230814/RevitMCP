using Autodesk.Revit.UI;

namespace RevitMCP.Addin.Execution;

internal sealed class RevitExternalEventHandler : IExternalEventHandler
{
    public const string HandlerName = "RevitMCP Execution Dispatcher";

    private readonly RevitExecutionQueue<UIApplication> _queue;

    public RevitExternalEventHandler(RevitExecutionQueue<UIApplication> queue)
    {
        ArgumentNullException.ThrowIfNull(queue);
        _queue = queue;
    }

    public void Execute(UIApplication app)
    {
        _queue.ExecutePending(app);
    }

    public string GetName() => HandlerName;
}
