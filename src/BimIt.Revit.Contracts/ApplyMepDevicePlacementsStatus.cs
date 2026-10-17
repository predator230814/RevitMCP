namespace BimIt.Revit.Contracts;

public enum ApplyMepDevicePlacementsStatus
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
