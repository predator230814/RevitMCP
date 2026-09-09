using Autodesk.Revit.UI;

namespace RevitMCP.Addin.Execution;

internal static class ExternalEventRequestMapper
{
    public static ExecutionScheduleResult Map(ExternalEventRequest request) =>
        request switch
        {
            ExternalEventRequest.Accepted => ExecutionScheduleResult.Accepted,
            ExternalEventRequest.Pending => ExecutionScheduleResult.Pending,
            ExternalEventRequest.Denied => ExecutionScheduleResult.Denied,
            ExternalEventRequest.TimedOut => ExecutionScheduleResult.TimedOut,
            _ => ExecutionScheduleResult.Denied
        };
}
