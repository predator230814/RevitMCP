namespace RevitMCP.Addin.Execution;

/// <summary>
/// RevitMCP-owned mapping of <c>ExternalEvent.Raise</c> outcomes.
/// </summary>
public enum ExecutionScheduleResult
{
    Accepted,
    Pending,
    Denied,
    TimedOut
}
