using BimIt.Revit.Addin.Apply;
using BimIt.Revit.Addin.Approval;
using BimIt.Revit.Addin.Audit;
using BimIt.Revit.Addin.Capabilities;
using BimIt.Revit.Addin.Intents;
using BimIt.Revit.Contracts;
using Xunit;

namespace BimIt.Revit.Addin.Tests;

public sealed class ApplyMepDevicePlacementOrchestratorTests
{
    [Fact]
    public void Apply_before_approval_does_not_mutate()
    {
        using var harness = new Harness();
        var created = harness.CreateMep();
        harness.Consume = _ => new ApprovalConsumeResult(ApprovalConsumeStatus.NotApproved, null);
        var result = harness.Run(created.IntentRef!);
        Assert.Equal(ApplyMepDevicePlacementsStatus.ApprovalRequired, result.Status);
        Assert.Null(result.CreatedElementRefs);
        Assert.Equal(0, harness.Mutations);
    }

    [Fact]
    public void Approved_intent_mutates_once_and_replay_returns_the_same_refs()
    {
        using var harness = new Harness();
        var created = harness.CreateMep();
        harness.MutateResult = new ApplyMutationResult(ApplyMutationKind.Applied, AuditTransactionStatus.Committed, ["opaque-a", "opaque-b"]);
        var result = harness.Run(created.IntentRef!);
        Assert.Equal(ApplyMepDevicePlacementsStatus.Applied, result.Status);
        Assert.Equal(2, result.CreatedCount);
        Assert.Equal(["opaque-a", "opaque-b"], result.CreatedElementRefs);
        Assert.Equal(1, harness.Mutations);

        var replay = harness.Run(created.IntentRef!, mutate: () => throw new InvalidOperationException("retry must not mutate"));
        Assert.Equal(ApplyMepDevicePlacementsStatus.Applied, replay.Status);
        Assert.Equal(["opaque-a", "opaque-b"], replay.CreatedElementRefs);
        Assert.Equal(1, harness.Mutations);
    }

    [Fact]
    public void Parameter_terminal_is_unavailable_to_mep_apply_after_intent_expiry()
    {
        using var harness = new Harness();
        var parameter = harness.Store.TryCreate(harness.ParameterDraft());
        var parameterMutations = 0;
        Assert.True(harness.Store.TryGet(parameter.IntentRef!, out var entry));
        var first = ApplyParameterUpdateOrchestrator.Execute(new ApplyOperation
        {
            IntentRef = parameter.IntentRef!,
            InstanceId = "instance-a",
            RevitVersion = "2026",
            RevitBuild = "26.5.0.55",
            AddinVersion = "0.1.0",
            Intents = harness.Store,
            Attempts = harness.Attempts,
            Audit = harness.Audit,
            ResolveDocument = () => new ApplyDocumentGate(true, "doc-a"),
            Consume = _ => new ApprovalConsumeResult(
                ApprovalConsumeStatus.Consumed,
                new ApprovalSnapshot(
                    entry!.IntentRef,
                    entry.IntentFingerprint,
                    entry.InstanceId,
                    entry.DocumentId,
                    DateTimeOffset.UnixEpoch,
                    "revit-local-in-process",
                    DateTimeOffset.UnixEpoch.AddMinutes(5))),
            Mutate = _ =>
            {
                parameterMutations++;
                return new ApplyMutationResult(ApplyMutationKind.Applied, AuditTransactionStatus.Committed);
            }
        });
        Assert.Equal(ApplyParameterUpdatesStatus.Applied, first.Status);
        harness.Clock.Advance(TimeSpan.FromMinutes(10));
        Assert.False(harness.Store.TryGetControlled(parameter.IntentRef!, out _));

        var replay = harness.Run(parameter.IntentRef!, () => throw new InvalidOperationException("cross-kind replay must not mutate"));
        Assert.Equal(ApplyMepDevicePlacementsStatus.Unavailable, replay.Status);
        Assert.Null(replay.CreatedElementRefs);
        Assert.Equal(1, parameterMutations);
        Assert.Equal(0, harness.Mutations);
    }

    [Fact]
    public void Mep_replay_survives_intent_expiry_with_the_same_refs()
    {
        using var harness = new Harness();
        var created = harness.CreateMep();
        harness.MutateResult = new ApplyMutationResult(ApplyMutationKind.Applied, AuditTransactionStatus.Committed, ["opaque-a", "opaque-b"]);
        var first = harness.Run(created.IntentRef!);
        Assert.Equal(ApplyMepDevicePlacementsStatus.Applied, first.Status);
        harness.Clock.Advance(TimeSpan.FromMinutes(10));
        Assert.False(harness.Store.TryGetControlled(created.IntentRef!, out _));

        var replay = harness.Run(created.IntentRef!, () => throw new InvalidOperationException("expired replay must not mutate"));
        Assert.Equal(ApplyMepDevicePlacementsStatus.Applied, replay.Status);
        Assert.Equal(["opaque-a", "opaque-b"], replay.CreatedElementRefs);
        Assert.Equal(1, harness.Mutations);
    }

    [Fact]
    public void Wrong_kind_expired_and_document_mismatch_are_unavailable()
    {
        using var harness = new Harness();
        var parameter = harness.Store.TryCreate(harness.ParameterDraft());
        Assert.Equal(ApplyMepDevicePlacementsStatus.Unavailable, harness.Run(parameter.IntentRef!).Status);
        Assert.Equal(0, harness.Mutations);

        var created = harness.CreateMep();
        harness.Clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(ApplyMepDevicePlacementsStatus.Unavailable, harness.Run(created.IntentRef!).Status);

        harness.Clock = new ManualTimeProvider();
        created = harness.CreateMep();
        harness.DocumentId = "other-doc";
        Assert.Equal(ApplyMepDevicePlacementsStatus.Unavailable, harness.Run(created.IntentRef!).Status);
        Assert.Equal(0, harness.Mutations);
    }

    [Fact]
    public void Mutation_outcomes_map_without_a_partial_creation_result()
    {
        AssertOutcome(ApplyMutationKind.Stale, ApplyMepDevicePlacementsStatus.Stale);
        AssertOutcome(ApplyMutationKind.TransactionFailed, ApplyMepDevicePlacementsStatus.TransactionFailed);
        AssertOutcome(ApplyMutationKind.CommittedUnverified, ApplyMepDevicePlacementsStatus.CommittedUnverified);
        AssertOutcome(ApplyMutationKind.Pending, ApplyMepDevicePlacementsStatus.Indeterminate);
    }

    private static void AssertOutcome(ApplyMutationKind kind, ApplyMepDevicePlacementsStatus expected)
    {
        using var harness = new Harness();
        var created = harness.CreateMep();
        harness.MutateResult = new ApplyMutationResult(kind, kind == ApplyMutationKind.Pending ? AuditTransactionStatus.Pending : AuditTransactionStatus.RolledBack);
        var result = harness.Run(created.IntentRef!);
        Assert.Equal(expected, result.Status);
        Assert.Null(result.CreatedElementRefs);
        Assert.Null(result.CreatedCount);
        Assert.Equal(1, harness.Mutations);
    }

    [Fact]
    public void Audit_preflight_failure_does_not_consume_or_mutate()
    {
        using var harness = new Harness();
        var created = harness.CreateMep();
        harness.Audit.Stop();
        var result = harness.Run(created.IntentRef!);
        Assert.Equal(ApplyMepDevicePlacementsStatus.Unavailable, result.Status);
        Assert.Equal(0, harness.ConsumeCalls);
        Assert.Equal(0, harness.Mutations);
    }

    [Fact]
    public void Audit_start_failure_does_not_mutate()
    {
        using var harness = new Harness();
        var created = harness.CreateMep();
        harness.AuditOptions.FailFlush = true;
        var result = harness.Run(created.IntentRef!);
        Assert.Equal(ApplyMepDevicePlacementsStatus.AuditFailed, result.Status);
        Assert.Equal(0, harness.Mutations);
        Assert.Null(result.CreatedElementRefs);
    }

    private sealed class Harness : IDisposable
    {
        public Harness()
        {
            Directory = Path.Combine(Path.GetTempPath(), "revitmep-audit-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Directory);
            AuditOptions = new AuditWriterOptions { Directory = Directory };
            Audit = new ControlledWriteAuditWriter(options: AuditOptions);
            Clock = new ManualTimeProvider();
            Store = new EphemeralWriteIntentStore(Clock);
            Attempts = new ControlledApplyAttemptStore();
        }

        public string Directory { get; }

        public AuditWriterOptions AuditOptions { get; }

        public ControlledWriteAuditWriter Audit { get; }

        public ManualTimeProvider Clock { get; set; }

        public EphemeralWriteIntentStore Store { get; }

        public ControlledApplyAttemptStore Attempts { get; }

        public int ConsumeCalls { get; private set; }

        public int Mutations { get; private set; }

        public string DocumentId { get; set; } = "doc-a";

        public ApplyMutationResult MutateResult { get; set; } = new(ApplyMutationKind.Stale, AuditTransactionStatus.None);

        public Func<string, ApprovalConsumeResult>? Consume { get; set; }

        public IntentCreateResult CreateMep()
        {
            return Store.TryCreateMep("instance-a", "doc-a", new MepDevicePlacementIntent(
                "autodesk.unit.unit:millimeters-1.0.1",
                "template-a",
                "symbol-a",
                "OST_ElectricalFixtures",
                "Electrical Fixtures",
                "Duplex Receptacle",
                "Standard",
                "wall-a",
                "Basic Wall",
                "level-a",
                "L4",
                new MepPoint(2, 0, 4),
                new MepPoint(0, 0, 0),
                new MepPoint(10, 0, 0),
                false,
                false,
                true,
                true,
                [
                    new MepDevicePlacementItem(1, 1200, 0, new MepPoint(5, 0, 4)),
                    new MepDevicePlacementItem(2, 2400, 0, new MepPoint(8, 0, 4))
                ]));
        }

        public IntentDraft ParameterDraft()
        {
            return new IntentDraft
            {
                InstanceId = "instance-a",
                DocumentId = "doc-a",
                Items =
                [
                    new IntentItemDraft
                    {
                        RequestPosition = 1,
                        ElementRef = "element-a",
                        ParameterRef = "parameter-a",
                        Source = "instance",
                        IdentityKind = DescribeParameterIdentityKind.Local,
                        StableKey = "local:42",
                        Status = "ok",
                        ElementName = "Wall",
                        CategoryName = "Walls",
                        ParameterName = "Comments",
                        DataTypeKind = DescribeParameterDataTypeKind.Spec,
                        BeforeHasValue = true,
                        BeforeValue = new IntentTypedValue.StringValue("before"),
                        Proposed = new IntentTypedValue.StringValue("after")
                    }
                ]
            };
        }

        public ApplyMepDevicePlacementsResult Run(string intentRef, Func<ApplyMutationResult>? mutate = null)
        {
            Store.TryGetControlled(intentRef, out var stored);
            return ApplyMepDevicePlacementOrchestrator.Execute(new MepApplyOperation
            {
                IntentRef = intentRef,
                InstanceId = "instance-a",
                RevitVersion = "2026",
                RevitBuild = "26.5.0.55",
                AddinVersion = "0.1.0",
                Intents = Store,
                Attempts = Attempts,
                Audit = Audit,
                ResolveDocument = () => new ApplyDocumentGate(true, DocumentId),
                Consume = intent =>
                {
                    ConsumeCalls++;
                    if (Consume is not null)
                    {
                        return Consume(intent);
                    }

                    var binding = stored!.Binding;
                    return new ApprovalConsumeResult(
                        ApprovalConsumeStatus.Consumed,
                        new ApprovalSnapshot(
                            binding.IntentRef,
                            binding.IntentFingerprint,
                            binding.InstanceId,
                            binding.DocumentId,
                            DateTimeOffset.UnixEpoch,
                            "revit-local-in-process",
                            DateTimeOffset.UnixEpoch.AddMinutes(5),
                            ControlledWriteIntentKind.MepDevicePlacements));
                },
                Mutate = _ =>
                {
                    Mutations++;
                    return mutate?.Invoke() ?? MutateResult;
                }
            });
        }

        public void Dispose()
        {
            Audit.Dispose();
            try
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
