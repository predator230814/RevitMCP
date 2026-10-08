using RevitMCP.Addin.Apply;
using RevitMCP.Addin.Approval;
using RevitMCP.Addin.Audit;
using RevitMCP.Addin.Intents;
using RevitMCP.Contracts;

namespace RevitMCP.Addin.Capabilities;

internal enum ApplyMutationKind
{
    Stale,
    TransactionFailed,
    Applied,
    CommittedUnverified,
    Indeterminate,
    Pending
}

internal readonly record struct ApplyMutationResult(ApplyMutationKind Kind, AuditTransactionStatus TransactionStatus);

internal readonly record struct ApplyDocumentGate(bool Available, string? DocumentId);

internal sealed class ApplyOperation
{
    public required string IntentRef { get; init; }

    public required string InstanceId { get; init; }

    public required string RevitVersion { get; init; }

    public required string RevitBuild { get; init; }

    public required string AddinVersion { get; init; }

    public required EphemeralWriteIntentStore Intents { get; init; }

    public required ControlledApplyAttemptStore Attempts { get; init; }

    public required ControlledWriteAuditWriter Audit { get; init; }

    public required Func<string, ApprovalConsumeResult> Consume { get; init; }

    public required Func<ApplyDocumentGate> ResolveDocument { get; init; }

    public required Func<IntentEntry, ApplyMutationResult> Mutate { get; init; }

    public Func<string>? MintAttemptRef { get; init; }

    public Action? BeforeEstablish { get; init; }
}

internal static class ApplyParameterUpdateOrchestrator
{
    public static ApplyParameterUpdatesResult Execute(ApplyOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        operation.Intents.TryGet(operation.IntentRef, out var intent);
        var binding = intent is null
            ? (ApplyBinding?)null
            : new ApplyBinding(intent.IntentFingerprint, intent.InstanceId, intent.DocumentId);
        var lookup = operation.Attempts.TryLookup(operation.IntentRef, binding);
        var replay = MapLookup(lookup);
        if (replay is not null)
        {
            return replay;
        }

        if (intent is null
            || binding is null
            || !string.Equals(intent.InstanceId, operation.InstanceId, StringComparison.Ordinal))
        {
            return Result(ApplyParameterUpdatesStatus.Unavailable);
        }

        var gate = operation.ResolveDocument();
        if (!gate.Available || !string.Equals(gate.DocumentId, intent.DocumentId, StringComparison.Ordinal))
        {
            return Result(ApplyParameterUpdatesStatus.Unavailable);
        }

        if (!TryOwn(operation, binding.Value, out var lease))
        {
            return MapLookup(operation.Attempts.TryLookup(operation.IntentRef, binding))
                ?? Result(ApplyParameterUpdatesStatus.Unavailable);
        }

        var attemptRef = operation.MintAttemptRef?.Invoke() ?? MintAttemptRef();
        var metadata = new AuditCommonMetadata
        {
            AttemptRef = attemptRef,
            IntentRef = intent.IntentRef,
            IntentFingerprint = intent.IntentFingerprint,
            IntentFingerprintSchemaVersion = intent.FingerprintSchemaVersion,
            InstanceId = intent.InstanceId,
            DocumentId = intent.DocumentId,
            RevitVersion = operation.RevitVersion,
            RevitBuild = operation.RevitBuild,
            AddinVersion = operation.AddinVersion,
            ItemCount = intent.Items.Count
        };
        if (!operation.Audit.Preflight(metadata).Succeeded)
        {
            operation.Attempts.ReleasePreMutation(lease);
            return Result(ApplyParameterUpdatesStatus.Unavailable);
        }

        var consumed = operation.Consume(intent.IntentRef);
        if (consumed.Status != ApprovalConsumeStatus.Consumed || consumed.Snapshot is not { } snapshot)
        {
            operation.Attempts.ReleasePreMutation(lease);
            return Result(ApplyParameterUpdatesStatus.ApprovalRequired);
        }

        operation.Attempts.MarkIrrevocable(lease);
        if (!SnapshotMatches(snapshot, intent)
            || !ControlledWriteAuditWriter.TryMapApprovalMethod(snapshot.ProviderMethod, out _))
        {
            return Finish(
                operation,
                lease,
                metadata,
                snapshot,
                ApplyTerminalStatus.AuditFailed,
                AuditApplyStatus.AuditFailed,
                AuditTransactionStatus.None,
                AuditVerificationStatus.NotRun,
                emitCompletion: true);
        }

        var started = operation.Audit.TryAppendApplyStarted(StartedDraft(metadata, snapshot));
        if (started != AuditAppendStatus.Durable)
        {
            return Finish(
                operation,
                lease,
                metadata,
                snapshot,
                ApplyTerminalStatus.AuditFailed,
                AuditApplyStatus.AuditFailed,
                AuditTransactionStatus.None,
                AuditVerificationStatus.NotRun,
                emitCompletion: true);
        }

        var mutation = operation.Mutate(intent);
        return mutation.Kind switch
        {
            ApplyMutationKind.Stale => Finish(
                operation,
                lease,
                metadata,
                snapshot,
                ApplyTerminalStatus.Stale,
                AuditApplyStatus.Stale,
                AuditTransactionStatus.None,
                AuditVerificationStatus.NotRun,
                emitCompletion: true),
            ApplyMutationKind.TransactionFailed => Finish(
                operation,
                lease,
                metadata,
                snapshot,
                ApplyTerminalStatus.TransactionFailed,
                AuditApplyStatus.TransactionFailed,
                mutation.TransactionStatus,
                AuditVerificationStatus.NotRun,
                emitCompletion: true),
            ApplyMutationKind.Applied => Finish(
                operation,
                lease,
                metadata,
                snapshot,
                ApplyTerminalStatus.Applied,
                AuditApplyStatus.Applied,
                AuditTransactionStatus.Committed,
                AuditVerificationStatus.Passed,
                emitCompletion: true),
            ApplyMutationKind.CommittedUnverified => Finish(
                operation,
                lease,
                metadata,
                snapshot,
                ApplyTerminalStatus.CommittedUnverified,
                AuditApplyStatus.CommittedUnverified,
                AuditTransactionStatus.Committed,
                AuditVerificationStatus.Failed,
                emitCompletion: true),
            ApplyMutationKind.Pending => Finish(
                operation,
                lease,
                metadata,
                snapshot,
                ApplyTerminalStatus.Indeterminate,
                AuditApplyStatus.Indeterminate,
                AuditTransactionStatus.Pending,
                AuditVerificationStatus.NotRun,
                emitCompletion: false),
            _ => Finish(
                operation,
                lease,
                metadata,
                snapshot,
                ApplyTerminalStatus.Indeterminate,
                AuditApplyStatus.Indeterminate,
                AuditTransactionStatus.Unknown,
                AuditVerificationStatus.NotRun,
                emitCompletion: true)
        };
    }

    private static bool TryOwn(ApplyOperation operation, ApplyBinding binding, out ApplyAttemptLease lease)
    {
        lease = ApplyAttemptLease.None;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (attempt == 0)
            {
                operation.BeforeEstablish?.Invoke();
            }
            var established = operation.Attempts.TryEstablishExclusive(operation.IntentRef, binding, out lease);
            if (established == ApplyEstablishStatus.Owner)
            {
                return true;
            }

            var lookup = operation.Attempts.TryLookup(operation.IntentRef, binding);
            if (lookup.Status != ApplyLookupStatus.Absent)
            {
                return false;
            }

            if (established is ApplyEstablishStatus.Exhausted or ApplyEstablishStatus.Rejected)
            {
                return false;
            }
        }

        return false;
    }

    private static ApplyParameterUpdatesResult? MapLookup(ApplyLookupResult lookup)
    {
        return lookup.Status switch
        {
            ApplyLookupStatus.Terminal when lookup.Terminal is { } terminal => Result(ToPublic(terminal)),
            ApplyLookupStatus.InProgress => Result(ApplyParameterUpdatesStatus.InProgress),
            ApplyLookupStatus.BindingMismatch => Result(ApplyParameterUpdatesStatus.Unavailable),
            _ => null
        };
    }

    private static ApplyParameterUpdatesResult Finish(
        ApplyOperation operation,
        ApplyAttemptLease lease,
        AuditCommonMetadata metadata,
        ApprovalSnapshot snapshot,
        ApplyTerminalStatus terminal,
        AuditApplyStatus applyStatus,
        AuditTransactionStatus transactionStatus,
        AuditVerificationStatus verificationStatus,
        bool emitCompletion)
    {
        operation.Attempts.Complete(lease, terminal);
        if (emitCompletion && transactionStatus != AuditTransactionStatus.Pending)
        {
            operation.Audit.TryAppendApplyCompleted(CompletedDraft(
                metadata,
                snapshot,
                applyStatus,
                transactionStatus,
                verificationStatus));
        }

        return Result(ToPublic(terminal));
    }

    private static bool SnapshotMatches(ApprovalSnapshot snapshot, IntentEntry intent)
    {
        return string.Equals(snapshot.IntentRef, intent.IntentRef, StringComparison.Ordinal)
            && string.Equals(snapshot.IntentFingerprint, intent.IntentFingerprint, StringComparison.Ordinal)
            && string.Equals(snapshot.InstanceId, intent.InstanceId, StringComparison.Ordinal)
            && string.Equals(snapshot.DocumentId, intent.DocumentId, StringComparison.Ordinal);
    }

    private static AuditEventDraft StartedDraft(AuditCommonMetadata metadata, ApprovalSnapshot snapshot)
    {
        return Draft(metadata, snapshot, null, null, null);
    }

    private static AuditEventDraft CompletedDraft(
        AuditCommonMetadata metadata,
        ApprovalSnapshot snapshot,
        AuditApplyStatus applyStatus,
        AuditTransactionStatus transactionStatus,
        AuditVerificationStatus verificationStatus)
    {
        return Draft(metadata, snapshot, applyStatus, transactionStatus, verificationStatus);
    }

    private static AuditEventDraft Draft(
        AuditCommonMetadata metadata,
        ApprovalSnapshot snapshot,
        AuditApplyStatus? applyStatus,
        AuditTransactionStatus? transactionStatus,
        AuditVerificationStatus? verificationStatus)
    {
        return new AuditEventDraft
        {
            EventAtUtc = DateTimeOffset.UtcNow,
            AttemptRef = metadata.AttemptRef,
            IntentRef = metadata.IntentRef,
            IntentFingerprint = metadata.IntentFingerprint,
            IntentFingerprintSchemaVersion = metadata.IntentFingerprintSchemaVersion,
            InstanceId = metadata.InstanceId,
            DocumentId = metadata.DocumentId,
            RevitVersion = metadata.RevitVersion,
            RevitBuild = metadata.RevitBuild,
            AddinVersion = metadata.AddinVersion,
            ItemCount = metadata.ItemCount,
            ApprovalMethod = snapshot.ProviderMethod,
            ApprovalDecidedAtUtc = snapshot.DecisionTimestamp,
            ApprovalEffectiveExpiryUtc = snapshot.EffectiveExpiry,
            ApplyStatus = applyStatus,
            TransactionStatus = transactionStatus,
            VerificationStatus = verificationStatus
        };
    }

    private static string MintAttemptRef()
    {
        Span<byte> bytes = stackalloc byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static ApplyParameterUpdatesStatus ToPublic(ApplyTerminalStatus status)
    {
        return status switch
        {
            ApplyTerminalStatus.Applied => ApplyParameterUpdatesStatus.Applied,
            ApplyTerminalStatus.Stale => ApplyParameterUpdatesStatus.Stale,
            ApplyTerminalStatus.TransactionFailed => ApplyParameterUpdatesStatus.TransactionFailed,
            ApplyTerminalStatus.CommittedUnverified => ApplyParameterUpdatesStatus.CommittedUnverified,
            ApplyTerminalStatus.Indeterminate => ApplyParameterUpdatesStatus.Indeterminate,
            ApplyTerminalStatus.AuditFailed => ApplyParameterUpdatesStatus.AuditFailed,
            _ => ApplyParameterUpdatesStatus.Indeterminate
        };
    }

    private static ApplyParameterUpdatesResult Result(ApplyParameterUpdatesStatus status)
    {
        return new ApplyParameterUpdatesResult { Status = status };
    }
}
