using RevitMCP.Addin.Audit;

namespace RevitMCP.Addin.Capabilities;

internal enum ApplyTransactionCheckpoint
{
    Start,
    Commit,
    Rollback
}

/// <summary>
/// Closed view of a Revit transaction status. Other covers every non-terminal value,
/// including a transaction that never started.
/// </summary>
internal enum ApplyObservedTransactionStatus
{
    Unreadable,
    Pending,
    RolledBack,
    Committed,
    Other
}

internal readonly record struct ApplyTransactionClassification(
    bool RequiresVerification,
    ApplyMutationResult Result)
{
    public static ApplyTransactionClassification Terminal(
        ApplyMutationKind kind,
        AuditTransactionStatus transactionStatus)
        => new(false, new ApplyMutationResult(kind, transactionStatus));
}

internal static class ApplyTransactionStatusClassifier
{
    internal static ApplyTransactionClassification Observe(
        ApplyTransactionCheckpoint checkpoint,
        ApplyObservedTransactionStatus getStatus,
        ApplyObservedTransactionStatus? returnedStatus)
    {
        if (returnedStatus == ApplyObservedTransactionStatus.Pending
            || getStatus == ApplyObservedTransactionStatus.Pending)
        {
            return Classify(checkpoint, ApplyObservedTransactionStatus.Pending);
        }

        if (getStatus != ApplyObservedTransactionStatus.Unreadable)
        {
            return Classify(checkpoint, getStatus);
        }

        if (returnedStatus is ApplyObservedTransactionStatus returned
            && returned != ApplyObservedTransactionStatus.Unreadable)
        {
            return Classify(checkpoint, returned);
        }

        return Classify(checkpoint, ApplyObservedTransactionStatus.Unreadable);
    }

    internal static ApplyTransactionClassification Classify(
        ApplyTransactionCheckpoint checkpoint,
        ApplyObservedTransactionStatus status)
    {
        switch (status)
        {
            case ApplyObservedTransactionStatus.Pending:
                return ApplyTransactionClassification.Terminal(
                    ApplyMutationKind.Pending,
                    AuditTransactionStatus.Pending);
            case ApplyObservedTransactionStatus.RolledBack:
                return ApplyTransactionClassification.Terminal(
                    ApplyMutationKind.TransactionFailed,
                    AuditTransactionStatus.RolledBack);
            case ApplyObservedTransactionStatus.Committed
                when checkpoint == ApplyTransactionCheckpoint.Commit:
                return new ApplyTransactionClassification(
                    RequiresVerification: true,
                    new ApplyMutationResult(ApplyMutationKind.Applied, AuditTransactionStatus.Committed));
            case ApplyObservedTransactionStatus.Other
                when checkpoint == ApplyTransactionCheckpoint.Start:
                return ApplyTransactionClassification.Terminal(
                    ApplyMutationKind.TransactionFailed,
                    AuditTransactionStatus.None);
            default:
                return ApplyTransactionClassification.Terminal(
                    ApplyMutationKind.Indeterminate,
                    AuditTransactionStatus.Unknown);
        }
    }
}
