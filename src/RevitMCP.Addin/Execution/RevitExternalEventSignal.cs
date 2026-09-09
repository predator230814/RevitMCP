using Autodesk.Revit.UI;

namespace RevitMCP.Addin.Execution;

internal sealed class RevitExternalEventSignal : IRevitEventSignal
{
    private readonly ExternalEvent _externalEvent;

    public RevitExternalEventSignal(ExternalEvent externalEvent)
    {
        ArgumentNullException.ThrowIfNull(externalEvent);
        _externalEvent = externalEvent;
    }

    public ExecutionScheduleResult Schedule() =>
        ExternalEventRequestMapper.Map(_externalEvent.Raise());
}
