using RevitMCP.Addin.Apply;
using RevitMCP.Addin.Approval;
using RevitMCP.Addin.Audit;
using RevitMCP.Addin.Capabilities;
using RevitMCP.Addin.Inspection;
using RevitMCP.Addin.Intents;
using RevitMCP.Contracts;
using Xunit;

namespace RevitMCP.Addin.Tests;

public sealed class ApplyParameterUpdatesOrchestratorTests
{
    [Fact]
    public void Terminal_replay_does_not_mutate_again()
    {
        using var harness = new Harness();
        var created = harness.CreateIntent();
        var binding = harness.Binding(created.IntentRef!);
        Assert.Equal(ApplyEstablishStatus.Owner, harness.Attempts.TryEstablishExclusive(created.IntentRef!, binding, out var lease));
        Assert.Equal(ApplyMarkStatus.Irrevocable, harness.Attempts.MarkIrrevocable(lease));
        Assert.Equal(ApplyCompleteStatus.Stored, harness.Attempts.Complete(lease, ApplyTerminalStatus.Applied));

        var result = harness.Run(created.IntentRef!, mutate: () => throw new InvalidOperationException("must not mutate"));
        Assert.Equal(ApplyParameterUpdatesStatus.Applied, result.Status);
        Assert.Equal(0, harness.ConsumeCalls);
    }

    [Fact]
    public void Establish_loss_to_a_terminal_replays_that_terminal()
    {
        using var harness = new Harness();
        var created = harness.CreateIntent();
        harness.BeforeEstablish = () =>
        {
            var binding = harness.Binding(created.IntentRef!);
            Assert.Equal(ApplyEstablishStatus.Owner, harness.Attempts.TryEstablishExclusive(created.IntentRef!, binding, out var lease));
            Assert.Equal(ApplyMarkStatus.Irrevocable, harness.Attempts.MarkIrrevocable(lease));
            Assert.Equal(ApplyCompleteStatus.Stored, harness.Attempts.Complete(lease, ApplyTerminalStatus.Stale));
        };

        var result = harness.Run(created.IntentRef!, mutate: () => throw new InvalidOperationException("must not mutate"));
        Assert.Equal(ApplyParameterUpdatesStatus.Stale, result.Status);
        Assert.Equal(0, harness.ConsumeCalls);
    }

    [Fact]
    public void Missing_approval_releases_the_pre_mutation_record()
    {
        using var harness = new Harness();
        var created = harness.CreateIntent();
        harness.Consume = _ => new ApprovalConsumeResult(ApprovalConsumeStatus.NotApproved, null);
        var result = harness.Run(created.IntentRef!);
        Assert.Equal(ApplyParameterUpdatesStatus.ApprovalRequired, result.Status);
        Assert.Equal(ApplyLookupStatus.Absent, harness.Attempts.TryLookup(created.IntentRef!, harness.Binding(created.IntentRef!)).Status);
        Assert.Equal(0, harness.Mutations);
    }

    [Fact]
    public void Preflight_failure_happens_before_consume_and_releases()
    {
        using var harness = new Harness();
        var created = harness.CreateIntent();
        harness.Audit.Stop();
        var result = harness.Run(created.IntentRef!);
        Assert.Equal(ApplyParameterUpdatesStatus.Unavailable, result.Status);
        Assert.Equal(0, harness.ConsumeCalls);
        Assert.Equal(ApplyLookupStatus.Absent, harness.Attempts.TryLookup(created.IntentRef!, null).Status);
    }

    [Fact]
    public void Consumed_approval_is_irrevocable_and_completion_failure_keeps_applied()
    {
        using var harness = new Harness();
        var created = harness.CreateIntent();
        harness.MutateResult = new ApplyMutationResult(ApplyMutationKind.Applied, AuditTransactionStatus.Committed);
        harness.BeforeMutation = () => harness.AuditOptions.FailFlush = true;
        var result = harness.Run(created.IntentRef!);
        Assert.Equal(ApplyParameterUpdatesStatus.Applied, result.Status);
        var lookup = harness.Attempts.TryLookup(created.IntentRef!, harness.Binding(created.IntentRef!));
        Assert.Equal(ApplyLookupStatus.Terminal, lookup.Status);
        Assert.Equal(ApplyTerminalStatus.Applied, lookup.Terminal);
        Assert.Equal(ApplyReleaseStatus.Rejected, harness.Attempts.ReleasePreMutation(new ApplyAttemptLease(created.IntentRef!, 1)));

        var second = harness.Run(created.IntentRef!, mutate: () => throw new InvalidOperationException("retry must not mutate"));
        Assert.Equal(ApplyParameterUpdatesStatus.Applied, second.Status);
    }

    [Fact]
    public void Unmapped_provider_is_audit_failed_without_mutation()
    {
        using var harness = new Harness();
        var created = harness.CreateIntent();
        harness.Store.TryGet(created.IntentRef!, out var entry);
        harness.Consume = _ => new ApprovalConsumeResult(
            ApprovalConsumeStatus.Consumed,
            new ApprovalSnapshot(entry!.IntentRef, entry.IntentFingerprint, entry.InstanceId, entry.DocumentId, DateTimeOffset.UnixEpoch, "other-provider", DateTimeOffset.UnixEpoch.AddMinutes(5)));
        var result = harness.Run(created.IntentRef!);
        Assert.Equal(ApplyParameterUpdatesStatus.AuditFailed, result.Status);
        Assert.Equal(0, harness.Mutations);
        Assert.Equal(ApplyTerminalStatus.AuditFailed, harness.Attempts.TryLookup(created.IntentRef!, harness.Binding(created.IntentRef!)).Terminal);
    }

    [Fact]
    public void Pending_stores_indeterminate_and_skips_completion()
    {
        using var harness = new Harness();
        var created = harness.CreateIntent();
        harness.MutateResult = new ApplyMutationResult(ApplyMutationKind.Pending, AuditTransactionStatus.Pending);
        var result = harness.Run(created.IntentRef!);
        Assert.Equal(ApplyParameterUpdatesStatus.Indeterminate, result.Status);
        var path = Directory.GetFiles(harness.Directory, "*.jsonl").Single();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd();
        Assert.Contains("apply_started", text, StringComparison.Ordinal);
        Assert.DoesNotContain("apply_completed", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Before_state_comparison_is_exact()
    {
        var item = Item(new IntentTypedValue.StringValue("A"), new IntentTypedValue.StringValue("B"));
        Assert.True(ApplyBeforeStateComparer.Matches(item, true, new IntentTypedValue.StringValue("A")));
        Assert.False(ApplyBeforeStateComparer.Matches(item, true, new IntentTypedValue.StringValue("A ")));
        Assert.False(ApplyBeforeStateComparer.Matches(item, false, null));

        var quantity = Item(
            new IntentTypedValue.QuantityValue(1.25, "unit"),
            new IntentTypedValue.QuantityValue(2.5, "unit"));
        Assert.True(ApplyBeforeStateComparer.Matches(quantity, true, new IntentTypedValue.QuantityValue(1.25, "unit")));
        Assert.False(ApplyBeforeStateComparer.Matches(quantity, true, new IntentTypedValue.QuantityValue(1.2500001, "unit")));
        Assert.False(ApplyBeforeStateComparer.Matches(quantity, true, new IntentTypedValue.QuantityValue(1.25, "other")));
    }

    [Fact]
    public void Provider_mapping_and_mutation_source_match_the_contract()
    {
        Assert.True(ControlledWriteAuditWriter.TryMapApprovalMethod("revit-local-in-process", out var mapped));
        Assert.Equal("revit_local_in_process", mapped);
        Assert.False(ControlledWriteAuditWriter.TryMapApprovalMethod("Revit-Local-In-Process", out _));

        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "RevitMCP.Addin", "Capabilities", "RevitApplyParameterMutation.cs"));
        Assert.Contains("RevitMCP Apply Parameter Updates", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TransactionGroup", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SubTransaction", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LookupParameter", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetValueString", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AsValueString", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Checkout", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SynchronizeWithCentral", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Save(", source, StringComparison.Ordinal);
        Assert.Equal(ApplyParameterUpdatesStatus.Applied, new ApplyParameterUpdatesResult { Status = ApplyParameterUpdatesStatus.Applied }.Status);
    }

    private static IntentItemEntry Item(IntentTypedValue before, IntentTypedValue proposed)
        => new(
            1,
            "element",
            "parameter",
            "instance",
            DescribeParameterIdentityKind.Local,
            null,
            null,
            "local",
            "ok",
            "Wall",
            false,
            "Walls",
            false,
            "Comments",
            false,
            DescribeParameterDataTypeKind.Spec,
            null,
            true,
            before,
            proposed);

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "RevitMCP.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate RevitMCP.sln.");
    }

    private sealed class Harness : IDisposable
    {
        public Harness()
        {
            Directory = Path.Combine(Path.GetTempPath(), "RevitMCP.Tests", Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Directory);
            AuditOptions = new AuditWriterOptions { Directory = Directory };
            Audit = new ControlledWriteAuditWriter(options: AuditOptions);
            Store = new EphemeralWriteIntentStore();
            Attempts = new ControlledApplyAttemptStore();
        }

        public string Directory { get; }

        public AuditWriterOptions AuditOptions { get; }

        public ControlledWriteAuditWriter Audit { get; }

        public EphemeralWriteIntentStore Store { get; }

        public ControlledApplyAttemptStore Attempts { get; }

        public int ConsumeCalls { get; private set; }

        public int Mutations { get; private set; }

        public ApplyMutationResult MutateResult { get; set; } = new(ApplyMutationKind.Stale, AuditTransactionStatus.None);

        public Action? BeforeEstablish { get; set; }

        public Action? BeforeMutation { get; set; }

        public Func<string, ApprovalConsumeResult>? Consume { get; set; }

        public IntentCreateResult CreateIntent()
        {
            return Store.TryCreate(new IntentDraft
            {
                InstanceId = "instance-a",
                DocumentId = "doc-a",
                Items = new List<IntentItemDraft>
                {
                    new()
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
                }
            });
        }

        public ApplyBinding Binding(string intentRef)
        {
            Assert.True(Store.TryGet(intentRef, out var entry));
            return new ApplyBinding(entry!.IntentFingerprint, entry.InstanceId, entry.DocumentId);
        }

        public ApplyParameterUpdatesResult Run(string intentRef, Func<ApplyMutationResult>? mutate = null)
        {
            Assert.True(Store.TryGet(intentRef, out var entry));
            return ApplyParameterUpdateOrchestrator.Execute(new ApplyOperation
            {
                IntentRef = intentRef,
                InstanceId = "instance-a",
                RevitVersion = "2026",
                RevitBuild = "26.5.0.55",
                AddinVersion = "0.1.0",
                Intents = Store,
                Attempts = Attempts,
                Audit = Audit,
                BeforeEstablish = BeforeEstablish,
                ResolveDocument = () => new ApplyDocumentGate(true, "doc-a"),
                Consume = intent =>
                {
                    ConsumeCalls++;
                    if (Consume is not null)
                    {
                        return Consume(intent);
                    }

                    return new ApprovalConsumeResult(
                        ApprovalConsumeStatus.Consumed,
                        new ApprovalSnapshot(
                            entry!.IntentRef,
                            entry.IntentFingerprint,
                            entry.InstanceId,
                            entry.DocumentId,
                            DateTimeOffset.UnixEpoch,
                            "revit-local-in-process",
                            DateTimeOffset.UnixEpoch.AddMinutes(5)));
                },
                Mutate = _ =>
                {
                    BeforeMutation?.Invoke();
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
