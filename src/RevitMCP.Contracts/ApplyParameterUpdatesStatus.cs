namespace RevitMCP.Contracts;

public enum ApplyParameterUpdatesStatus
{
    Applied,
    ApprovalRequired,
    Unavailable,
    InProgress,
    Stale,
    TransactionFailed,
    CommittedUnverified,
    Indeterminate,
    AuditFailed
}
