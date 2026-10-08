using RevitMCP.Addin.Audit;
using RevitMCP.Addin.Capabilities;
using Xunit;

namespace RevitMCP.Addin.Tests;

public sealed class ApplyTransactionStatusClassifierTests
{
    [Fact]
    public void Failure_handling_is_installed_after_transaction_start()
    {
        var commit = Slice(
            File.ReadAllText(MutationPath()),
            "private static ApplyMutationResult Commit(",
            "private static ApplyMutationResult Rollback(");
        var start = commit.IndexOf("transaction.Start()", StringComparison.Ordinal);
        var handling = commit.IndexOf("SetFailureHandlingOptions(", StringComparison.Ordinal);

        Assert.True(start >= 0);
        Assert.True(handling > start);
    }

    [Fact]
    public void Cleanup_does_not_dispose_a_pending_transaction_or_replace_a_classified_result()
    {
        var release = Slice(
            File.ReadAllText(MutationPath()),
            "private static void SafeRelease(",
            "private static bool Verify(");
        var pending = release.IndexOf("TransactionStatus.Pending", StringComparison.Ordinal);
        var dispose = release.IndexOf("transaction.Dispose()", StringComparison.Ordinal);
        var disposeGuard = release.IndexOf("catch (Exception)", dispose, StringComparison.Ordinal);

        Assert.True(pending >= 0);
        Assert.True(pending < dispose);
        Assert.True(disposeGuard > dispose);
    }

    [Fact]
    public void Status_map_preserves_pending_rolled_back_and_committed()
    {
        var map = Slice(
            File.ReadAllText(MutationPath()),
            "private static ApplyObservedTransactionStatus MapStatus(",
            "private static bool TryReadStatus(");

        Assert.Contains("TransactionStatus.Pending => ApplyObservedTransactionStatus.Pending", map, StringComparison.Ordinal);
        Assert.Contains("TransactionStatus.RolledBack => ApplyObservedTransactionStatus.RolledBack", map, StringComparison.Ordinal);
        Assert.Contains("TransactionStatus.Committed => ApplyObservedTransactionStatus.Committed", map, StringComparison.Ordinal);
        Assert.Contains("_ => ApplyObservedTransactionStatus.Other", map, StringComparison.Ordinal);
    }

    [Fact]
    public void Unreadable_status_is_indeterminate()
    {
        foreach (var checkpoint in new[]
        {
            ApplyTransactionCheckpoint.Start,
            ApplyTransactionCheckpoint.Commit,
            ApplyTransactionCheckpoint.Rollback
        })
        {
            var actual = ApplyTransactionStatusClassifier.Classify(
                checkpoint,
                ApplyObservedTransactionStatus.Unreadable);
            AssertTerminal(actual, ApplyMutationKind.Indeterminate, AuditTransactionStatus.Unknown);
        }
    }

    [Fact]
    public void Commit_statuses_preserve_rolled_back_pending_and_committed_verification()
    {
        AssertTerminal(
            Classify(ApplyTransactionCheckpoint.Commit, ApplyObservedTransactionStatus.Pending),
            ApplyMutationKind.Pending,
            AuditTransactionStatus.Pending);
        AssertTerminal(
            Classify(ApplyTransactionCheckpoint.Commit, ApplyObservedTransactionStatus.RolledBack),
            ApplyMutationKind.TransactionFailed,
            AuditTransactionStatus.RolledBack);
        AssertTerminal(
            Classify(ApplyTransactionCheckpoint.Commit, ApplyObservedTransactionStatus.Other),
            ApplyMutationKind.Indeterminate,
            AuditTransactionStatus.Unknown);
        AssertTerminal(
            Classify(ApplyTransactionCheckpoint.Commit, ApplyObservedTransactionStatus.Unreadable),
            ApplyMutationKind.Indeterminate,
            AuditTransactionStatus.Unknown);

        var committed = Classify(ApplyTransactionCheckpoint.Commit, ApplyObservedTransactionStatus.Committed);
        Assert.True(committed.RequiresVerification);
        Assert.Equal(ApplyMutationKind.Applied, committed.Result.Kind);
        Assert.Equal(AuditTransactionStatus.Committed, committed.Result.TransactionStatus);
    }

    [Fact]
    public void Rollback_statuses_preserve_rolled_back_and_pending()
    {
        AssertTerminal(
            Classify(ApplyTransactionCheckpoint.Rollback, ApplyObservedTransactionStatus.Pending),
            ApplyMutationKind.Pending,
            AuditTransactionStatus.Pending);
        AssertTerminal(
            Classify(ApplyTransactionCheckpoint.Rollback, ApplyObservedTransactionStatus.RolledBack),
            ApplyMutationKind.TransactionFailed,
            AuditTransactionStatus.RolledBack);
        AssertTerminal(
            Classify(ApplyTransactionCheckpoint.Rollback, ApplyObservedTransactionStatus.Committed),
            ApplyMutationKind.Indeterminate,
            AuditTransactionStatus.Unknown);
        AssertTerminal(
            Classify(ApplyTransactionCheckpoint.Rollback, ApplyObservedTransactionStatus.Other),
            ApplyMutationKind.Indeterminate,
            AuditTransactionStatus.Unknown);
    }

    [Fact]
    public void Start_failure_keeps_a_known_non_committed_outcome()
    {
        AssertTerminal(
            Classify(ApplyTransactionCheckpoint.Start, ApplyObservedTransactionStatus.Pending),
            ApplyMutationKind.Pending,
            AuditTransactionStatus.Pending);
        AssertTerminal(
            Classify(ApplyTransactionCheckpoint.Start, ApplyObservedTransactionStatus.RolledBack),
            ApplyMutationKind.TransactionFailed,
            AuditTransactionStatus.RolledBack);
        AssertTerminal(
            Classify(ApplyTransactionCheckpoint.Start, ApplyObservedTransactionStatus.Other),
            ApplyMutationKind.TransactionFailed,
            AuditTransactionStatus.None);
        AssertTerminal(
            Classify(ApplyTransactionCheckpoint.Start, ApplyObservedTransactionStatus.Committed),
            ApplyMutationKind.Indeterminate,
            AuditTransactionStatus.Unknown);
    }

    [Fact]
    public void Returned_status_is_used_when_get_status_cannot_be_read()
    {
        var committed = ApplyTransactionStatusClassifier.Observe(
            ApplyTransactionCheckpoint.Commit,
            ApplyObservedTransactionStatus.Unreadable,
            ApplyObservedTransactionStatus.Committed);
        Assert.True(committed.RequiresVerification);

        var rolledBack = ApplyTransactionStatusClassifier.Observe(
            ApplyTransactionCheckpoint.Rollback,
            ApplyObservedTransactionStatus.Unreadable,
            ApplyObservedTransactionStatus.RolledBack);
        AssertTerminal(rolledBack, ApplyMutationKind.TransactionFailed, AuditTransactionStatus.RolledBack);

        var unknown = ApplyTransactionStatusClassifier.Observe(
            ApplyTransactionCheckpoint.Commit,
            ApplyObservedTransactionStatus.Unreadable,
            returnedStatus: null);
        AssertTerminal(unknown, ApplyMutationKind.Indeterminate, AuditTransactionStatus.Unknown);
    }

    [Fact]
    public void Current_committed_wins_over_returned_pending()
    {
        var actual = ApplyTransactionStatusClassifier.Observe(
            ApplyTransactionCheckpoint.Commit,
            ApplyObservedTransactionStatus.Committed,
            ApplyObservedTransactionStatus.Pending);
        Assert.True(actual.RequiresVerification);
        Assert.Equal(ApplyMutationKind.Applied, actual.Result.Kind);
        Assert.Equal(AuditTransactionStatus.Committed, actual.Result.TransactionStatus);
    }

    [Fact]
    public void Current_rolled_back_wins_over_returned_pending()
    {
        var actual = ApplyTransactionStatusClassifier.Observe(
            ApplyTransactionCheckpoint.Commit,
            ApplyObservedTransactionStatus.RolledBack,
            ApplyObservedTransactionStatus.Pending);
        AssertTerminal(actual, ApplyMutationKind.TransactionFailed, AuditTransactionStatus.RolledBack);
    }

    [Fact]
    public void Current_pending_wins_over_returned_committed()
    {
        var committed = ApplyTransactionStatusClassifier.Observe(
            ApplyTransactionCheckpoint.Commit,
            ApplyObservedTransactionStatus.Pending,
            ApplyObservedTransactionStatus.Committed);
        AssertTerminal(committed, ApplyMutationKind.Pending, AuditTransactionStatus.Pending);

        var rolledBack = ApplyTransactionStatusClassifier.Observe(
            ApplyTransactionCheckpoint.Rollback,
            ApplyObservedTransactionStatus.Pending,
            ApplyObservedTransactionStatus.RolledBack);
        AssertTerminal(rolledBack, ApplyMutationKind.Pending, AuditTransactionStatus.Pending);
    }

    [Fact]
    public void Returned_pending_is_used_when_current_status_is_unreadable()
    {
        var actual = ApplyTransactionStatusClassifier.Observe(
            ApplyTransactionCheckpoint.Commit,
            ApplyObservedTransactionStatus.Unreadable,
            ApplyObservedTransactionStatus.Pending);
        AssertTerminal(actual, ApplyMutationKind.Pending, AuditTransactionStatus.Pending);
    }

    [Fact]
    public void Returned_pending_stays_pending_when_current_status_is_not_final()
    {
        var actual = ApplyTransactionStatusClassifier.Observe(
            ApplyTransactionCheckpoint.Commit,
            ApplyObservedTransactionStatus.Other,
            ApplyObservedTransactionStatus.Pending);
        AssertTerminal(actual, ApplyMutationKind.Pending, AuditTransactionStatus.Pending);
    }

    private static ApplyTransactionClassification Classify(
        ApplyTransactionCheckpoint checkpoint,
        ApplyObservedTransactionStatus status)
        => ApplyTransactionStatusClassifier.Classify(checkpoint, status);

    private static void AssertTerminal(
        ApplyTransactionClassification actual,
        ApplyMutationKind kind,
        AuditTransactionStatus transactionStatus)
    {
        Assert.False(actual.RequiresVerification);
        Assert.Equal(kind, actual.Result.Kind);
        Assert.Equal(transactionStatus, actual.Result.TransactionStatus);
    }

    private static string MutationPath()
        => Path.Combine(FindRepoRoot(), "src", "RevitMCP.Addin", "Capabilities", "RevitApplyParameterMutation.cs");

    private static string Slice(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        var endIndex = source.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        Assert.True(startIndex >= 0);
        Assert.True(endIndex > startIndex);
        return source[startIndex..endIndex];
    }

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
}
