namespace RevitMCP.Addin.Execution;

/// <summary>
/// Schedules a future Revit-thread drain. Tests substitute a fake signal so the
/// queue state machine can run without Autodesk Revit.
/// </summary>
public interface IRevitEventSignal
{
    ExecutionScheduleResult Schedule();
}
