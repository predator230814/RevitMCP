using RevitMCP.Addin.Apply;
using RevitMCP.Addin.Audit;
using RevitMCP.Addin.Identity;
using RevitMCP.Addin.Lifecycle;
using Xunit;

namespace RevitMCP.Addin.Tests;

public sealed class ApplyAttemptStoreTests
{
    [Fact]
    public void Second_caller_observes_in_progress_until_the_terminal_outcome_exists()
    {
        var store = new ControlledApplyAttemptStore();
        var binding = Binding();

        Assert.Equal(ApplyEstablishStatus.Owner, store.TryEstablishExclusive("intent-a", binding, out var owner));
        Assert.Equal(ApplyEstablishStatus.InProgress, store.TryEstablishExclusive("intent-a", binding, out var loser));
        Assert.False(loser.IsAssigned);
        Assert.Equal(ApplyLookupStatus.InProgress, store.TryLookup("intent-a", binding).Status);
        Assert.True(store.TryInspect("intent-a", out var first));
        Assert.Equal(ApplyAttemptState.PreMutation, first.State);

        Assert.Equal(ApplyMarkStatus.Irrevocable, store.MarkIrrevocable(owner));
        Assert.Equal(ApplyCompleteStatus.Stored, store.Complete(owner, ApplyTerminalStatus.Applied));

        var retry = store.TryLookup("intent-a", binding);
        Assert.Equal(ApplyLookupStatus.Terminal, retry.Status);
        Assert.Equal(ApplyTerminalStatus.Applied, retry.Terminal);
        Assert.Equal(ApplyEstablishStatus.InProgress, store.TryEstablishExclusive("intent-a", binding, out _));
        Assert.True(store.TryInspect("intent-a", out var still));
        Assert.Equal(first.IntentFingerprint, still.IntentFingerprint);
    }

    [Fact]
    public void Pre_mutation_release_removes_the_record_and_stores_no_terminal()
    {
        var store = new ControlledApplyAttemptStore();
        Assert.Equal(ApplyEstablishStatus.Owner, store.TryEstablishExclusive("intent-a", Binding(), out var owner));

        Assert.Equal(ApplyReleaseStatus.Released, store.ReleasePreMutation(owner));

        Assert.Equal(ApplyLookupStatus.Absent, store.TryLookup("intent-a", Binding()).Status);
        Assert.False(store.TryInspect("intent-a", out _));
        Assert.Equal(ApplyEstablishStatus.Owner, store.TryEstablishExclusive("intent-a", Binding(), out _));
    }

    [Fact]
    public void Release_is_rejected_after_irrevocable_including_audit_failed()
    {
        var store = new ControlledApplyAttemptStore();
        Assert.Equal(ApplyEstablishStatus.Owner, store.TryEstablishExclusive("intent-a", Binding(), out var owner));
        Assert.Equal(ApplyCompleteStatus.Rejected, store.Complete(owner, ApplyTerminalStatus.AuditFailed));
        Assert.Equal(ApplyMarkStatus.Irrevocable, store.MarkIrrevocable(owner));
        Assert.Equal(ApplyReleaseStatus.Rejected, store.ReleasePreMutation(owner));
        Assert.Equal(ApplyCompleteStatus.Stored, store.Complete(owner, ApplyTerminalStatus.AuditFailed));
        Assert.Equal(ApplyReleaseStatus.Rejected, store.ReleasePreMutation(owner));
        Assert.Equal(ApplyCompleteStatus.Rejected, store.Complete(owner, ApplyTerminalStatus.Applied));
        Assert.Equal(ApplyTerminalStatus.AuditFailed, store.TryLookup("intent-a", null).Terminal);
    }

    [Fact]
    public void Matching_or_absent_source_retries_the_same_terminal_without_a_new_owner()
    {
        foreach (var status in Enum.GetValues<ApplyTerminalStatus>())
        {
            var store = new ControlledApplyAttemptStore();
            var binding = Binding();
            Complete(store, "intent-a", binding, status);

            Assert.Equal(status, store.TryLookup("intent-a", binding).Terminal);
            Assert.Equal(status, store.TryLookup("intent-a", null).Terminal);
            Assert.Equal(ApplyEstablishStatus.InProgress, store.TryEstablishExclusive("intent-a", binding, out _));
        }

        Assert.Equal(
            new[]
            {
                nameof(ApplyTerminalStatus.Applied),
                nameof(ApplyTerminalStatus.Stale),
                nameof(ApplyTerminalStatus.TransactionFailed),
                nameof(ApplyTerminalStatus.CommittedUnverified),
                nameof(ApplyTerminalStatus.Indeterminate),
                nameof(ApplyTerminalStatus.AuditFailed)
            },
            Enum.GetNames<ApplyTerminalStatus>());
    }

    [Fact]
    public void Live_binding_mismatch_hides_the_retained_terminal()
    {
        var store = new ControlledApplyAttemptStore();
        var binding = Binding();
        Complete(store, "intent-a", binding, ApplyTerminalStatus.Applied);

        AssertMismatch(store, Binding(fingerprint: Hex('b')));
        AssertMismatch(store, Binding(instanceId: "instance-b"));
        AssertMismatch(store, Binding(documentId: "doc-b"));
        Assert.Equal(ApplyTerminalStatus.Applied, store.TryLookup("intent-a", binding).Terminal);
        Assert.Equal(ApplyEstablishStatus.InProgress, store.TryEstablishExclusive("intent-a", Binding(fingerprint: Hex('b')), out _));
    }

    [Fact]
    public void Terminal_expires_at_thirty_monotonic_minutes()
    {
        var clock = new ManualTimeProvider();
        var store = new ControlledApplyAttemptStore(clock);
        Complete(store, "intent-a", Binding(), ApplyTerminalStatus.Stale);

        clock.Advance(ControlledApplyAttemptStore.TerminalLifetime - TimeSpan.FromTicks(1));
        Assert.Equal(ApplyTerminalStatus.Stale, store.TryLookup("intent-a", null).Terminal);

        clock.Advance(TimeSpan.FromTicks(1));
        Assert.Equal(ApplyLookupStatus.Absent, store.TryLookup("intent-a", null).Status);
        Assert.False(store.TryInspect("intent-a", out _));
    }

    [Fact]
    public void Wall_clock_shift_does_not_extend_terminal_lifetime()
    {
        var clock = new ManualTimeProvider();
        var store = new ControlledApplyAttemptStore(clock);
        Complete(store, "intent-a", Binding(), ApplyTerminalStatus.Applied);

        clock.UtcNow = clock.UtcNow.AddDays(2);
        Assert.Equal(ApplyTerminalStatus.Applied, store.TryLookup("intent-a", Binding()).Terminal);

        clock.Advance(ControlledApplyAttemptStore.TerminalLifetime);
        Assert.Equal(ApplyLookupStatus.Absent, store.TryLookup("intent-a", null).Status);
    }

    [Fact]
    public void Capacity_retains_128_live_attempts_and_purge_frees_an_expired_slot()
    {
        var clock = new ManualTimeProvider();
        var store = new ControlledApplyAttemptStore(clock);
        for (var index = 0; index < ControlledApplyAttemptStore.LiveCapacity; index++)
        {
            Assert.Equal(ApplyEstablishStatus.Owner, store.TryEstablishExclusive("intent-" + index, Binding(), out var lease));
            Assert.Equal(ApplyMarkStatus.Irrevocable, store.MarkIrrevocable(lease));
            Assert.Equal(ApplyCompleteStatus.Stored, store.Complete(lease, ApplyTerminalStatus.Applied));
        }

        Assert.Equal(ApplyEstablishStatus.Exhausted, store.TryEstablishExclusive("intent-extra", Binding(), out _));
        Assert.Equal(ApplyTerminalStatus.Applied, store.TryLookup("intent-0", Binding()).Terminal);

        Assert.Equal(0, store.PurgeExpired());
        clock.Advance(ControlledApplyAttemptStore.TerminalLifetime);
        Assert.Equal(ControlledApplyAttemptStore.LiveCapacity, store.PurgeExpired());
        Assert.Equal(ApplyEstablishStatus.Owner, store.TryEstablishExclusive("intent-extra", Binding(), out _));
        Assert.Equal(ApplyLookupStatus.InProgress, store.TryLookup("intent-extra", Binding()).Status);
    }

    [Fact]
    public void Concurrent_establishes_keep_one_owner_and_the_capacity_ceiling()
    {
        var store = new ControlledApplyAttemptStore();
        var owners = 0;
        Parallel.For(0, 32, _ =>
        {
            var status = Establish(store, "intent-shared", Binding());
            if (status == ApplyEstablishStatus.Owner)
            {
                Interlocked.Increment(ref owners);
            }
            else
            {
                Assert.Equal(ApplyEstablishStatus.InProgress, status);
            }
        });
        Assert.Equal(1, owners);

        var admitted = 0;
        var exhausted = 0;
        Parallel.For(0, 200, index =>
        {
            var status = Establish(store, "intent-cap-" + index, Binding(documentId: "doc-" + index));
            if (status == ApplyEstablishStatus.Owner)
            {
                Interlocked.Increment(ref admitted);
            }
            else
            {
                Assert.Equal(ApplyEstablishStatus.Exhausted, status);
                Interlocked.Increment(ref exhausted);
            }
        });

        Assert.Equal(ControlledApplyAttemptStore.LiveCapacity - 1, admitted);
        Assert.Equal(200 - (ControlledApplyAttemptStore.LiveCapacity - 1), exhausted);
        Assert.Equal(ApplyLookupStatus.InProgress, store.TryLookup("intent-shared", Binding()).Status);
    }

    [Fact]
    public void Successful_document_close_removes_matching_attempts_and_cancelled_close_preserves_them()
    {
        var store = new ControlledApplyAttemptStore();
        var kept = Binding(documentId: "doc-b");
        Assert.Equal(ApplyEstablishStatus.Owner, store.TryEstablishExclusive("intent-a", Binding(), out _));
        Assert.Equal(ApplyEstablishStatus.Owner, store.TryEstablishExclusive("intent-b", kept, out _));
        var seen = new List<string>();
        var cleanup = new DocumentCloseIdentityCleanup<object>(_ =>
            RevitExecutionDispatcherLifetime.ApplySuccessfulDocumentClose(
                true,
                "doc-a",
                _ => 0,
                documentId =>
                {
                    seen.Add(documentId);
                    return store.ForgetDocument(documentId);
                },
                _ => 0,
                () => false,
                () => false));
        var document = new object();

        cleanup.OnClosing(1, document);
        cleanup.OnClosed(1, DocumentCloseOutcome.Cancelled);
        cleanup.OnClosing(2, document);
        cleanup.OnClosed(2, DocumentCloseOutcome.Failed);
        Assert.Empty(seen);
        Assert.Equal(ApplyLookupStatus.InProgress, store.TryLookup("intent-a", Binding()).Status);

        cleanup.OnClosing(3, document);
        cleanup.OnClosed(3, DocumentCloseOutcome.Succeeded);
        Assert.Equal(new[] { "doc-a" }, seen);
        Assert.Equal(ApplyLookupStatus.Absent, store.TryLookup("intent-a", Binding()).Status);
        Assert.Equal(ApplyLookupStatus.InProgress, store.TryLookup("intent-b", kept).Status);
    }

    [Fact]
    public void Clear_rejects_future_claims_and_removes_state()
    {
        var store = new ControlledApplyAttemptStore();
        Complete(store, "intent-a", Binding(), ApplyTerminalStatus.Indeterminate);

        Assert.Equal(1, store.Clear());
        Assert.False(store.TryInspect("intent-a", out _));
        Assert.Equal(ApplyLookupStatus.Absent, store.TryLookup("intent-a", null).Status);
        Assert.Equal(ApplyEstablishStatus.Rejected, store.TryEstablishExclusive("intent-b", Binding(), out _));
        Assert.Equal(0, store.Clear());
    }

    [Fact]
    public void Store_has_no_enumeration_api_and_keeps_no_bim_or_revit_wrappers()
    {
        var names = typeof(ControlledApplyAttemptStore)
            .GetMethods()
            .Select(method => method.Name);
        Assert.DoesNotContain(names, name =>
            name.Contains("List", StringComparison.Ordinal)
            || name.Contains("Search", StringComparison.Ordinal)
            || name.Contains("Recent", StringComparison.Ordinal)
            || name.Contains("Enumerate", StringComparison.Ordinal));

        var source = File.ReadAllText(RepoFile(Path.Combine("src", "RevitMCP.Addin", "Apply", "ControlledApplyAttemptStore.cs")));
        Assert.DoesNotContain("Autodesk.Revit", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Parameter.Set", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new Transaction", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TransactionGroup", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SubTransaction", source, StringComparison.Ordinal);
        Assert.DoesNotContain("element_ref", source, StringComparison.Ordinal);
        Assert.DoesNotContain("parameter_ref", source, StringComparison.Ordinal);
        Assert.DoesNotContain("session_ref", source, StringComparison.Ordinal);
        Assert.DoesNotContain("audit_stream_ref", source, StringComparison.Ordinal);

        var store = new ControlledApplyAttemptStore();
        Assert.Equal(ApplyEstablishStatus.Owner, store.TryEstablishExclusive("intent-a", Binding(), out _));
        Assert.True(store.TryInspect("intent-a", out var inspection));
        Assert.Equal(Hex('a'), inspection.IntentFingerprint);
        Assert.Equal("instance-a", inspection.InstanceId);
        Assert.Equal("doc-a", inspection.DocumentId);
        Assert.Equal(
            new[] { "IntentRef", "IntentFingerprint", "InstanceId", "DocumentId", "State", "Terminal" },
            typeof(ApplyAttemptInspection).GetProperties().Select(property => property.Name).ToArray());
    }

    [Fact]
    public void Non_owner_cannot_mark_or_complete()
    {
        var store = new ControlledApplyAttemptStore();
        Assert.Equal(ApplyEstablishStatus.Owner, store.TryEstablishExclusive("intent-a", Binding(), out var owner));
        var outsider = new ApplyAttemptLease("intent-a", owner.Token + 1);

        Assert.Equal(ApplyReleaseStatus.Rejected, store.ReleasePreMutation(outsider));
        Assert.Equal(ApplyMarkStatus.Absent, store.MarkIrrevocable(outsider));
        Assert.Equal(ApplyCompleteStatus.Rejected, store.Complete(outsider, ApplyTerminalStatus.Applied));
        Assert.Equal(ApplyAttemptState.PreMutation, Inspect(store, "intent-a").State);
        Assert.DoesNotContain(
            typeof(ControlledApplyAttemptStore).GetMethods().Select(method => method.Name),
            name => name.Contains("Consume", StringComparison.Ordinal));
    }

    [Fact]
    public void Failed_preflight_can_release_the_pre_mutation_record_without_a_terminal()
    {
        var root = TempDirectory();
        try
        {
            var blocked = Path.Combine(root, "not-a-directory");
            File.WriteAllText(blocked, "x");
            var writer = new ControlledWriteAuditWriter(options: new AuditWriterOptions { Directory = blocked });
            var store = new ControlledApplyAttemptStore();
            Assert.Equal(ApplyEstablishStatus.Owner, store.TryEstablishExclusive("intent-a", Binding(), out var owner));

            var preflight = writer.Preflight("2026", "2026.5", "0.1");

            Assert.False(preflight.Succeeded);
            Assert.Equal(ApplyReleaseStatus.Released, store.ReleasePreMutation(owner));
            Assert.Equal(ApplyLookupStatus.Absent, store.TryLookup("intent-a", null).Status);
            writer.Dispose();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Terminal_outcome_stays_stored_when_completion_audit_fails()
    {
        var root = TempDirectory();
        try
        {
            var options = new AuditWriterOptions { Directory = root, FailFlush = true };
            var writer = new ControlledWriteAuditWriter(options: options);
            var store = new ControlledApplyAttemptStore();
            Complete(store, "intent-a", Binding(), ApplyTerminalStatus.Applied);
            options.FailFlush = true;

            var completed = writer.TryAppendApplyCompleted(CompletedDraft());

            Assert.Equal(AuditAppendStatus.Failed, completed);
            Assert.True(writer.IsDegraded);
            Assert.Equal(ApplyTerminalStatus.Applied, store.TryLookup("intent-a", null).Terminal);
            Assert.Equal(ApplyEstablishStatus.InProgress, store.TryEstablishExclusive("intent-a", Binding(), out var outsider));
            Assert.False(outsider.IsAssigned);
            Assert.Equal(ApplyReleaseStatus.Rejected, store.ReleasePreMutation(new ApplyAttemptLease("intent-a", 1)));
            writer.Dispose();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Indeterminate_completion_does_not_require_a_completion_audit_line()
    {
        var store = new ControlledApplyAttemptStore();
        Complete(store, "intent-a", Binding(), ApplyTerminalStatus.Indeterminate);

        Assert.Equal(ApplyTerminalStatus.Indeterminate, store.TryLookup("intent-a", Binding()).Terminal);
        Assert.Equal(ApplyEstablishStatus.InProgress, store.TryEstablishExclusive("intent-a", Binding(), out _));
        Assert.Empty(typeof(ControlledApplyAttemptStore).GetFields());
    }

    private static ApplyEstablishStatus Establish(ControlledApplyAttemptStore store, string intentRef, ApplyBinding binding)
    {
        return store.TryEstablishExclusive(intentRef, binding, out _);
    }

    private static void Complete(ControlledApplyAttemptStore store, string intentRef, ApplyBinding binding, ApplyTerminalStatus status)
    {
        Assert.Equal(ApplyEstablishStatus.Owner, store.TryEstablishExclusive(intentRef, binding, out var lease));
        Assert.Equal(ApplyMarkStatus.Irrevocable, store.MarkIrrevocable(lease));
        Assert.Equal(ApplyCompleteStatus.Stored, store.Complete(lease, status));
    }

    private static void AssertMismatch(ControlledApplyAttemptStore store, ApplyBinding binding)
    {
        var mismatch = store.TryLookup("intent-a", binding);
        Assert.Equal(ApplyLookupStatus.BindingMismatch, mismatch.Status);
        Assert.Null(mismatch.Terminal);
    }

    private static ApplyAttemptInspection Inspect(ControlledApplyAttemptStore store, string intentRef)
    {
        Assert.True(store.TryInspect(intentRef, out var inspection));
        return inspection;
    }

    private static ApplyBinding Binding(
        string documentId = "doc-a",
        string? fingerprint = null,
        string instanceId = "instance-a")
    {
        return new ApplyBinding(fingerprint ?? Hex('a'), instanceId, documentId);
    }

    private static string Hex(char digit) => new(digit, 64);

    private static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "revitmcp-apply-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "RevitMCP.sln")))
            {
                return Path.Combine(dir.FullName, relative);
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }

    private static AuditEventDraft CompletedDraft()
    {
        return new AuditEventDraft
        {
            EventAtUtc = new DateTimeOffset(2026, 10, 7, 15, 4, 5, TimeSpan.Zero).AddTicks(1234567),
            AttemptRef = Hex('c'),
            IntentRef = "raw-intent-secret",
            IntentFingerprint = Hex('d'),
            IntentFingerprintSchemaVersion = 1,
            InstanceId = "raw-instance-secret",
            DocumentId = "raw-document-secret",
            RevitVersion = "2026",
            RevitBuild = "2026.5",
            AddinVersion = "0.1.0",
            ItemCount = 1,
            ApplyStatus = AuditApplyStatus.Applied,
            TransactionStatus = AuditTransactionStatus.Committed,
            VerificationStatus = AuditVerificationStatus.Passed
        };
    }
}
