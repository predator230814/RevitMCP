using BimIt.Revit.Addin.Apply;
using BimIt.Revit.Addin.Approval;
using BimIt.Revit.Addin.Audit;
using BimIt.Revit.Addin.Intents;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.Capabilities;

internal sealed class MepApplyOperation
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

    public required Func<MepDevicePlacementIntent, ApplyMutationResult> Mutate { get; init; }

    public Func<string>? MintAttemptRef { get; init; }

    public Action? BeforeEstablish { get; init; }
}

internal static class ApplyMepDevicePlacementOrchestrator
{
    public static ApplyMepDevicePlacementsResult Execute(MepApplyOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        operation.Intents.TryGetControlled(operation.IntentRef, out var controlled);
        if (controlled is null)
        {
            return MapLookup(operation.Attempts.TryLookup(operation.IntentRef, null))
                ?? Result(ApplyMepDevicePlacementsStatus.Unavailable);
        }

        if (controlled.Binding.Kind != ControlledWriteIntentKind.MepDevicePlacements
            || controlled.MepDevicePlacements is not { } intent)
        {
            return Result(ApplyMepDevicePlacementsStatus.Unavailable);
        }
        var binding = new ApplyBinding(
            controlled.Binding.IntentFingerprint,
            controlled.Binding.InstanceId,
            controlled.Binding.DocumentId,
            ControlledWriteIntentKind.MepDevicePlacements);
        var replay = MapLookup(operation.Attempts.TryLookup(operation.IntentRef, binding));
        if (replay is not null)
        {
            return replay;
        }

        if (!string.Equals(controlled.Binding.InstanceId, operation.InstanceId, StringComparison.Ordinal))
        {
            return Result(ApplyMepDevicePlacementsStatus.Unavailable);
        }

        var gate = operation.ResolveDocument();
        if (!gate.Available || !string.Equals(gate.DocumentId, controlled.Binding.DocumentId, StringComparison.Ordinal))
        {
            return Result(ApplyMepDevicePlacementsStatus.Unavailable);
        }

        if (!TryOwn(operation, binding, out var lease))
        {
            return MapLookup(operation.Attempts.TryLookup(operation.IntentRef, binding))
                ?? Result(ApplyMepDevicePlacementsStatus.Unavailable);
        }

        var attemptRef = operation.MintAttemptRef?.Invoke() ?? MintAttemptRef();
        var metadata = new AuditCommonMetadata
        {
            AttemptRef = attemptRef,
            IntentRef = controlled.Binding.IntentRef,
            IntentFingerprint = controlled.Binding.IntentFingerprint,
            IntentFingerprintSchemaVersion = controlled.Binding.FingerprintSchemaVersion,
            InstanceId = controlled.Binding.InstanceId,
            DocumentId = controlled.Binding.DocumentId,
            RevitVersion = operation.RevitVersion,
            RevitBuild = operation.RevitBuild,
            AddinVersion = operation.AddinVersion,
            ItemCount = intent.Items.Count
        };
        if (!operation.Audit.Preflight(metadata).Succeeded)
        {
            operation.Attempts.ReleasePreMutation(lease);
            return Result(ApplyMepDevicePlacementsStatus.Unavailable);
        }

        var consumed = operation.Consume(controlled.Binding.IntentRef);
        if (consumed.Status != ApprovalConsumeStatus.Consumed
            || consumed.Snapshot is not { } snapshot
            || snapshot.IntentKind != ControlledWriteIntentKind.MepDevicePlacements)
        {
            operation.Attempts.ReleasePreMutation(lease);
            return Result(ApplyMepDevicePlacementsStatus.ApprovalRequired);
        }

        operation.Attempts.MarkIrrevocable(lease);
        if (!SnapshotMatches(snapshot, controlled.Binding)
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
                emitCompletion: true,
                createdRefs: null);
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
                emitCompletion: true,
                createdRefs: null);
        }

        var mutation = operation.Mutate(intent);
        return mutation.Kind switch
        {
            ApplyMutationKind.Stale => Finish(operation, lease, metadata, snapshot, ApplyTerminalStatus.Stale, AuditApplyStatus.Stale, AuditTransactionStatus.None, AuditVerificationStatus.NotRun, true, null),
            ApplyMutationKind.TransactionFailed => Finish(operation, lease, metadata, snapshot, ApplyTerminalStatus.TransactionFailed, AuditApplyStatus.TransactionFailed, mutation.TransactionStatus, AuditVerificationStatus.NotRun, true, null),
            ApplyMutationKind.Applied => Finish(operation, lease, metadata, snapshot, ApplyTerminalStatus.Applied, AuditApplyStatus.Applied, AuditTransactionStatus.Committed, AuditVerificationStatus.Passed, true, mutation.CreatedElementRefs),
            ApplyMutationKind.CommittedUnverified => Finish(operation, lease, metadata, snapshot, ApplyTerminalStatus.CommittedUnverified, AuditApplyStatus.CommittedUnverified, AuditTransactionStatus.Committed, AuditVerificationStatus.Failed, true, null),
            ApplyMutationKind.Pending => Finish(operation, lease, metadata, snapshot, ApplyTerminalStatus.Indeterminate, AuditApplyStatus.Indeterminate, AuditTransactionStatus.Pending, AuditVerificationStatus.NotRun, false, null),
            _ => Finish(operation, lease, metadata, snapshot, ApplyTerminalStatus.Indeterminate, AuditApplyStatus.Indeterminate, AuditTransactionStatus.Unknown, AuditVerificationStatus.NotRun, true, null)
        };
    }

    private static bool TryOwn(MepApplyOperation operation, ApplyBinding binding, out ApplyAttemptLease lease)
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

    private static ApplyMepDevicePlacementsResult? MapLookup(ApplyLookupResult lookup)
    {
        return lookup.Status switch
        {
            ApplyLookupStatus.Terminal when lookup.Terminal == ApplyTerminalStatus.Applied && lookup.CreatedElementRefs is { Count: > 0 } refs
                => Applied(refs),
            ApplyLookupStatus.Terminal when lookup.Terminal is { } terminal => Result(ToPublic(terminal)),
            ApplyLookupStatus.InProgress => Result(ApplyMepDevicePlacementsStatus.InProgress),
            ApplyLookupStatus.BindingMismatch => Result(ApplyMepDevicePlacementsStatus.Unavailable),
            _ => null
        };
    }

    private static ApplyMepDevicePlacementsResult Finish(
        MepApplyOperation operation,
        ApplyAttemptLease lease,
        AuditCommonMetadata metadata,
        ApprovalSnapshot snapshot,
        ApplyTerminalStatus terminal,
        AuditApplyStatus applyStatus,
        AuditTransactionStatus transactionStatus,
        AuditVerificationStatus verificationStatus,
        bool emitCompletion,
        IReadOnlyList<string>? createdRefs)
    {
        if (terminal == ApplyTerminalStatus.Applied && (createdRefs is null || createdRefs.Count != metadata.ItemCount))
        {
            terminal = ApplyTerminalStatus.CommittedUnverified;
            applyStatus = AuditApplyStatus.CommittedUnverified;
            verificationStatus = AuditVerificationStatus.Failed;
            createdRefs = null;
        }

        operation.Attempts.Complete(lease, terminal, createdRefs);
        if (emitCompletion && transactionStatus != AuditTransactionStatus.Pending)
        {
            operation.Audit.TryAppendApplyCompleted(CompletedDraft(metadata, snapshot, applyStatus, transactionStatus, verificationStatus));
        }

        return terminal == ApplyTerminalStatus.Applied && createdRefs is not null
            ? Applied(createdRefs)
            : Result(ToPublic(terminal));
    }

    private static bool SnapshotMatches(ApprovalSnapshot snapshot, ControlledWriteIntentBinding binding)
    {
        return snapshot.IntentKind == ControlledWriteIntentKind.MepDevicePlacements
            && string.Equals(snapshot.IntentRef, binding.IntentRef, StringComparison.Ordinal)
            && string.Equals(snapshot.IntentFingerprint, binding.IntentFingerprint, StringComparison.Ordinal)
            && string.Equals(snapshot.InstanceId, binding.InstanceId, StringComparison.Ordinal)
            && string.Equals(snapshot.DocumentId, binding.DocumentId, StringComparison.Ordinal);
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

    private static ApplyMepDevicePlacementsStatus ToPublic(ApplyTerminalStatus status)
    {
        return status switch
        {
            ApplyTerminalStatus.Applied => ApplyMepDevicePlacementsStatus.Applied,
            ApplyTerminalStatus.Stale => ApplyMepDevicePlacementsStatus.Stale,
            ApplyTerminalStatus.TransactionFailed => ApplyMepDevicePlacementsStatus.TransactionFailed,
            ApplyTerminalStatus.CommittedUnverified => ApplyMepDevicePlacementsStatus.CommittedUnverified,
            ApplyTerminalStatus.Indeterminate => ApplyMepDevicePlacementsStatus.Indeterminate,
            ApplyTerminalStatus.AuditFailed => ApplyMepDevicePlacementsStatus.AuditFailed,
            _ => ApplyMepDevicePlacementsStatus.Indeterminate
        };
    }

    private static ApplyMepDevicePlacementsResult Applied(IReadOnlyList<string> createdRefs)
    {
        return new ApplyMepDevicePlacementsResult
        {
            Status = ApplyMepDevicePlacementsStatus.Applied,
            CreatedCount = createdRefs.Count,
            CreatedElementRefs = createdRefs
        };
    }

    private static ApplyMepDevicePlacementsResult Result(ApplyMepDevicePlacementsStatus status)
    {
        return new ApplyMepDevicePlacementsResult { Status = status };
    }
}
