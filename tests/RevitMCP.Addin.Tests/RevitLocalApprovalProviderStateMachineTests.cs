using System.Buffers.Binary;
using System.Reflection;
using RevitMCP.Addin.Approval;
using RevitMCP.Addin.Intents;
using RevitMCP.Contracts;
using Xunit;

namespace RevitMCP.Addin.Tests;

public sealed class RevitLocalApprovalProviderStateMachineTests
{
    [Fact]
    public void Unknown_intent_cannot_start()
    {
        var harness = Harness();

        var review = harness.Provider.BeginReview("missing-intent", "doc-a");

        Assert.Equal(ApprovalReviewStatus.Unavailable, review.Status);
        Assert.Null(review.SessionRef);
    }

    [Fact]
    public void Expired_intent_cannot_start()
    {
        var harness = Harness();
        harness.Clock.Advance(TimeSpan.FromMinutes(10));

        var review = harness.Provider.BeginReview(harness.IntentRef, "doc-a");

        Assert.Equal(ApprovalReviewStatus.Unavailable, review.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Missing_active_document_cannot_start(string? activeDocumentId)
    {
        var harness = Harness();

        var review = harness.Provider.BeginReview(harness.IntentRef, activeDocumentId);

        Assert.Equal(ApprovalReviewStatus.Unavailable, review.Status);
    }

    [Fact]
    public void Wrong_active_document_cannot_start()
    {
        var harness = Harness();

        var review = harness.Provider.BeginReview(harness.IntentRef, "doc-b");

        Assert.Equal(ApprovalReviewStatus.Unavailable, review.Status);
        Assert.True(harness.Store.TryGet(harness.IntentRef, out _));
    }

    [Fact]
    public void Valid_begin_review_starts_one_session()
    {
        var harness = Harness();

        var review = harness.Provider.BeginReview(harness.IntentRef, "doc-a");

        Assert.Equal(ApprovalReviewStatus.Started, review.Status);
        Assert.Equal("session-1", review.SessionRef);
    }

    [Fact]
    public void Repeated_begin_review_returns_the_same_session()
    {
        var harness = Harness();
        var started = harness.Provider.BeginReview(harness.IntentRef, "doc-a");

        var again = harness.Provider.BeginReview(harness.IntentRef, "doc-a");

        Assert.Equal(ApprovalReviewStatus.AlreadyActive, again.Status);
        Assert.Equal(started.SessionRef, again.SessionRef);
    }

    [Fact]
    public void Another_intent_is_busy_while_a_session_is_active()
    {
        var harness = Harness();
        var other = harness.Store.TryCreate(Draft(documentId: "doc-a", elementName: "Door 1"));
        harness.Provider.BeginReview(harness.IntentRef, "doc-a");

        var busy = harness.Provider.BeginReview(other.IntentRef, "doc-a");

        Assert.Equal(ApprovalReviewStatus.Busy, busy.Status);
        Assert.Null(busy.SessionRef);
        Assert.Equal(ApprovalReviewStatus.AlreadyActive, harness.Provider.BeginReview(harness.IntentRef, "doc-a").Status);
    }

    [Fact]
    public void Active_document_change_ends_the_pending_session_without_a_decision()
    {
        var harness = Harness();
        var started = harness.Provider.BeginReview(harness.IntentRef, "doc-a");

        var observation = harness.Provider.ObserveActiveDocument("doc-b");

        Assert.Equal(ApprovalObservation.Ended, observation);
        Assert.Equal(ApprovalCommandStatus.InvalidSession, harness.Provider.ApproveCurrent(started.SessionRef, "doc-b").Status);
        Assert.Equal(ApprovalConsumeStatus.NotApproved, harness.Provider.TryConsumeApproved(harness.IntentRef).Status);
        var restarted = harness.Provider.BeginReview(harness.IntentRef, "doc-a");
        Assert.Equal(ApprovalReviewStatus.Started, restarted.Status);
        Assert.NotEqual(started.SessionRef, restarted.SessionRef);
    }

    [Fact]
    public void Same_document_observation_retains_the_session()
    {
        var harness = Harness();
        var started = harness.Provider.BeginReview(harness.IntentRef, "doc-a");

        Assert.Equal(ApprovalObservation.Retained, harness.Provider.ObserveActiveDocument("doc-a"));
        Assert.Equal(started.SessionRef, harness.Provider.BeginReview(harness.IntentRef, "doc-a").SessionRef);
    }

    [Fact]
    public void Null_active_document_observation_ends_the_pending_session_without_a_decision()
    {
        var harness = Harness();
        var started = harness.Provider.BeginReview(harness.IntentRef, "doc-a");

        Assert.Equal(ApprovalObservation.Ended, harness.Provider.ObserveActiveDocument(null));
        Assert.Equal(ApprovalCommandStatus.InvalidSession, harness.Provider.ApproveCurrent(started.SessionRef, "doc-a").Status);
        Assert.Equal(ApprovalConsumeStatus.NotApproved, harness.Provider.TryConsumeApproved(harness.IntentRef).Status);
    }

    [Fact]
    public void Wrong_or_stale_session_cannot_approve_or_reject()
    {
        var harness = Harness();
        var started = harness.Provider.BeginReview(harness.IntentRef, "doc-a");

        Assert.Equal(ApprovalCommandStatus.InvalidSession, harness.Provider.ApproveCurrent("other-session", "doc-a").Status);
        Assert.Equal(ApprovalCommandStatus.InvalidSession, harness.Provider.RejectCurrent(null, "doc-a").Status);
        Assert.Equal(started.SessionRef, harness.Provider.BeginReview(harness.IntentRef, "doc-a").SessionRef);
    }

    [Fact]
    public void Expiry_after_begin_review_prevents_a_decision()
    {
        var harness = Harness();
        var started = harness.Provider.BeginReview(harness.IntentRef, "doc-a");
        harness.Clock.Advance(TimeSpan.FromMinutes(10));

        var approve = harness.Provider.ApproveCurrent(started.SessionRef, "doc-a");
        var reject = harness.Provider.RejectCurrent(started.SessionRef, "doc-a");

        Assert.Equal(ApprovalCommandStatus.Unavailable, approve.Status);
        Assert.Equal(ApprovalCommandStatus.InvalidSession, reject.Status);
        Assert.Equal(ApprovalConsumeStatus.NotApproved, harness.Provider.TryConsumeApproved(harness.IntentRef).Status);
    }

    [Fact]
    public void Binding_mismatch_prevents_a_decision()
    {
        var clock = new ManualTimeProvider();
        var draws = new Queue<byte[]>(new[] { Bytes(1), Bytes(1) });
        var store = new EphemeralWriteIntentStore(clock, draws.Dequeue);
        var sessions = new Queue<string>(new[] { "session-1", "session-2" });
        var provider = new RevitLocalApprovalProviderStateMachine(store, clock, sessions.Dequeue);
        var first = store.TryCreate(Draft());
        var started = provider.BeginReview(first.IntentRef, "doc-a");
        store.ForgetDocument("doc-a");
        var reused = store.TryCreate(Draft(elementName: "Wall 2"));

        Assert.Equal(first.IntentRef, reused.IntentRef);
        Assert.NotEqual(first.IntentFingerprint, reused.IntentFingerprint);
        Assert.Equal(ApprovalCommandStatus.Unavailable, provider.ApproveCurrent(started.SessionRef, "doc-a").Status);
        Assert.Equal(ApprovalConsumeStatus.NotApproved, provider.TryConsumeApproved(reused.IntentRef).Status);
        var restarted = provider.BeginReview(reused.IntentRef, "doc-a");
        Assert.Equal(ApprovalReviewStatus.Started, restarted.Status);
        Assert.Equal("session-2", restarted.SessionRef);
    }

    [Fact]
    public void Valid_approve_records_one_terminal_approval()
    {
        var harness = Harness();
        harness.Clock.UtcNow = new DateTimeOffset(2026, 10, 5, 15, 0, 0, TimeSpan.Zero);
        var started = harness.Provider.BeginReview(harness.IntentRef, "doc-a");

        var approved = harness.Provider.ApproveCurrent(started.SessionRef, "doc-a");
        var again = harness.Provider.ApproveCurrent(started.SessionRef, "doc-a");
        var consumed = harness.Provider.TryConsumeApproved(harness.IntentRef);

        Assert.Equal(ApprovalCommandStatus.Recorded, approved.Status);
        Assert.Equal(ApprovalCommandStatus.InvalidSession, again.Status);
        Assert.Equal(ApprovalReviewStatus.Terminal, harness.Provider.BeginReview(harness.IntentRef, "doc-a").Status);
        Assert.Equal(ApprovalConsumeStatus.Consumed, consumed.Status);
        Assert.True(consumed.Snapshot.HasValue);
        var snapshot = consumed.Snapshot.Value;
        Assert.Equal(harness.Created.ExpiresAt, snapshot.EffectiveExpiry);
        Assert.Equal(harness.Clock.UtcNow, snapshot.DecisionTimestamp);
        Assert.Equal(RevitLocalApprovalProviderStateMachine.DefaultProviderMethod, snapshot.ProviderMethod);
    }

    [Fact]
    public void Valid_reject_records_one_terminal_rejection()
    {
        var harness = Harness();
        var started = harness.Provider.BeginReview(harness.IntentRef, "doc-a");

        Assert.Equal(ApprovalCommandStatus.Recorded, harness.Provider.RejectCurrent(started.SessionRef, "doc-a").Status);
        Assert.Equal(ApprovalCommandStatus.InvalidSession, harness.Provider.RejectCurrent(started.SessionRef, "doc-a").Status);
        Assert.Equal(ApprovalReviewStatus.Terminal, harness.Provider.BeginReview(harness.IntentRef, "doc-a").Status);
        Assert.Equal(ApprovalConsumeStatus.NotApproved, harness.Provider.TryConsumeApproved(harness.IntentRef).Status);
    }

    [Fact]
    public void Approve_then_reject_cannot_flip()
    {
        var harness = Harness();
        var started = harness.Provider.BeginReview(harness.IntentRef, "doc-a");
        harness.Provider.ApproveCurrent(started.SessionRef, "doc-a");

        Assert.Equal(ApprovalCommandStatus.InvalidSession, harness.Provider.RejectCurrent(started.SessionRef, "doc-a").Status);
        Assert.Equal(ApprovalConsumeStatus.Consumed, harness.Provider.TryConsumeApproved(harness.IntentRef).Status);
    }

    [Fact]
    public void Reject_then_approve_cannot_flip()
    {
        var harness = Harness();
        var started = harness.Provider.BeginReview(harness.IntentRef, "doc-a");
        harness.Provider.RejectCurrent(started.SessionRef, "doc-a");

        Assert.Equal(ApprovalCommandStatus.InvalidSession, harness.Provider.ApproveCurrent(started.SessionRef, "doc-a").Status);
        Assert.Equal(ApprovalReviewStatus.Terminal, harness.Provider.BeginReview(harness.IntentRef, "doc-a").Status);
        Assert.Equal(ApprovalConsumeStatus.NotApproved, harness.Provider.TryConsumeApproved(harness.IntentRef).Status);
    }

    [Fact]
    public void Dismiss_records_no_decision_and_a_later_review_gets_a_new_session()
    {
        var harness = Harness();
        var started = harness.Provider.BeginReview(harness.IntentRef, "doc-a");

        Assert.Equal(ApprovalDismissStatus.Dismissed, harness.Provider.DismissCurrent(started.SessionRef).Status);
        Assert.Equal(ApprovalConsumeStatus.NotApproved, harness.Provider.TryConsumeApproved(harness.IntentRef).Status);
        var restarted = harness.Provider.BeginReview(harness.IntentRef, "doc-a");
        Assert.Equal(ApprovalReviewStatus.Started, restarted.Status);
        Assert.NotEqual(started.SessionRef, restarted.SessionRef);
    }

    [Fact]
    public void Stale_session_after_dismissal_cannot_decide()
    {
        var harness = Harness();
        var started = harness.Provider.BeginReview(harness.IntentRef, "doc-a");
        harness.Provider.DismissCurrent(started.SessionRef);
        var restarted = harness.Provider.BeginReview(harness.IntentRef, "doc-a");

        Assert.Equal(ApprovalCommandStatus.InvalidSession, harness.Provider.ApproveCurrent(started.SessionRef, "doc-a").Status);
        Assert.Equal(restarted.SessionRef, harness.Provider.BeginReview(harness.IntentRef, "doc-a").SessionRef);
        Assert.Equal(ApprovalConsumeStatus.NotApproved, harness.Provider.TryConsumeApproved(harness.IntentRef).Status);
    }

    [Fact]
    public void Rejected_intent_cannot_reopen()
    {
        var harness = Harness();
        var started = harness.Provider.BeginReview(harness.IntentRef, "doc-a");
        harness.Provider.RejectCurrent(started.SessionRef, "doc-a");

        Assert.Equal(ApprovalReviewStatus.Terminal, harness.Provider.BeginReview(harness.IntentRef, "doc-a").Status);
    }

    [Fact]
    public void Approved_intent_cannot_reopen()
    {
        var harness = Harness();
        var started = harness.Provider.BeginReview(harness.IntentRef, "doc-a");
        harness.Provider.ApproveCurrent(started.SessionRef, "doc-a");

        Assert.Equal(ApprovalReviewStatus.Terminal, harness.Provider.BeginReview(harness.IntentRef, "doc-a").Status);
    }

    [Fact]
    public void Consumed_approval_cannot_reopen()
    {
        var harness = Harness();
        var started = harness.Provider.BeginReview(harness.IntentRef, "doc-a");
        harness.Provider.ApproveCurrent(started.SessionRef, "doc-a");
        harness.Provider.TryConsumeApproved(harness.IntentRef);

        Assert.Equal(ApprovalReviewStatus.Terminal, harness.Provider.BeginReview(harness.IntentRef, "doc-a").Status);
        Assert.Equal(ApprovalConsumeStatus.NotApproved, harness.Provider.TryConsumeApproved(harness.IntentRef).Status);
    }

    [Fact]
    public void Approved_decision_consumes_once()
    {
        var harness = Harness();
        var started = harness.Provider.BeginReview(harness.IntentRef, "doc-a");
        harness.Provider.ApproveCurrent(started.SessionRef, "doc-a");

        var consumed = harness.Provider.TryConsumeApproved(harness.IntentRef);
        var second = harness.Provider.TryConsumeApproved(harness.IntentRef);

        Assert.Equal(ApprovalConsumeStatus.Consumed, consumed.Status);
        Assert.True(consumed.Snapshot.HasValue);
        var snapshot = consumed.Snapshot.Value;
        Assert.Equal(harness.IntentRef, snapshot.IntentRef);
        Assert.Equal(harness.Created.IntentFingerprint, snapshot.IntentFingerprint);
        Assert.Equal("instance-a", snapshot.InstanceId);
        Assert.Equal("doc-a", snapshot.DocumentId);
        Assert.Equal(ApprovalConsumeStatus.NotApproved, second.Status);
        Assert.Null(second.Snapshot);
    }

    [Fact]
    public void Concurrent_consume_attempts_allow_one_success()
    {
        var harness = Harness();
        var started = harness.Provider.BeginReview(harness.IntentRef, "doc-a");
        harness.Provider.ApproveCurrent(started.SessionRef, "doc-a");
        var wins = 0;
        var barrier = new Barrier(8);

        Parallel.For(0, 8, _ =>
        {
            barrier.SignalAndWait();
            if (harness.Provider.TryConsumeApproved(harness.IntentRef).Status == ApprovalConsumeStatus.Consumed)
            {
                Interlocked.Increment(ref wins);
            }
        });

        Assert.Equal(1, wins);
    }

    [Fact]
    public void Rejected_decision_cannot_consume()
    {
        var harness = Harness();
        var started = harness.Provider.BeginReview(harness.IntentRef, "doc-a");
        harness.Provider.RejectCurrent(started.SessionRef, "doc-a");

        Assert.Equal(ApprovalConsumeStatus.NotApproved, harness.Provider.TryConsumeApproved(harness.IntentRef).Status);
    }

    [Fact]
    public void Expired_intent_makes_an_existing_approval_unusable()
    {
        var harness = Harness();
        var started = harness.Provider.BeginReview(harness.IntentRef, "doc-a");
        harness.Provider.ApproveCurrent(started.SessionRef, "doc-a");
        harness.Clock.Advance(TimeSpan.FromMinutes(10));

        Assert.Equal(ApprovalConsumeStatus.NotApproved, harness.Provider.TryConsumeApproved(harness.IntentRef).Status);
        Assert.Equal(ApprovalReviewStatus.Unavailable, harness.Provider.BeginReview(harness.IntentRef, "doc-a").Status);
    }

    [Fact]
    public void Approval_does_not_extend_intent_expiry()
    {
        var harness = Harness();
        var started = harness.Provider.BeginReview(harness.IntentRef, "doc-a");
        harness.Provider.ApproveCurrent(started.SessionRef, "doc-a");
        var consumed = harness.Provider.TryConsumeApproved(harness.IntentRef);

        Assert.True(consumed.Snapshot.HasValue);
        Assert.Equal(harness.Created.ExpiresAt, consumed.Snapshot.Value.EffectiveExpiry);
        harness.Clock.Advance(TimeSpan.FromMinutes(10));
        Assert.False(harness.Store.TryGet(harness.IntentRef, out _));
    }

    [Fact]
    public void Forget_document_clears_only_matching_provider_state()
    {
        var harness = Harness();
        var other = harness.Store.TryCreate(Draft(documentId: "doc-b", elementName: "Door 1"));
        var first = harness.Provider.BeginReview(harness.IntentRef, "doc-a");
        harness.Provider.ApproveCurrent(first.SessionRef, "doc-a");
        var second = harness.Provider.BeginReview(other.IntentRef, "doc-b");
        harness.Provider.ApproveCurrent(second.SessionRef, "doc-b");

        Assert.True(harness.Provider.ForgetDocument("doc-a") >= 1);
        Assert.True(harness.Store.TryGet(harness.IntentRef, out _));
        Assert.Equal(ApprovalReviewStatus.Started, harness.Provider.BeginReview(harness.IntentRef, "doc-a").Status);
        Assert.Equal(ApprovalConsumeStatus.Consumed, harness.Provider.TryConsumeApproved(other.IntentRef).Status);
    }

    [Fact]
    public void Delayed_decision_after_forget_document_records_nothing()
    {
        var harness = Harness();
        var started = harness.Provider.BeginReview(harness.IntentRef, "doc-a");
        harness.Provider.ForgetDocument("doc-a");

        Assert.Equal(ApprovalCommandStatus.InvalidSession, harness.Provider.ApproveCurrent(started.SessionRef, "doc-a").Status);
        Assert.Equal(ApprovalConsumeStatus.NotApproved, harness.Provider.TryConsumeApproved(harness.IntentRef).Status);
        Assert.True(harness.Store.TryGet(harness.IntentRef, out _));
    }

    [Fact]
    public void Purge_expired_removes_stale_provider_state()
    {
        var harness = Harness();
        var started = harness.Provider.BeginReview(harness.IntentRef, "doc-a");
        harness.Provider.ApproveCurrent(started.SessionRef, "doc-a");
        harness.Clock.Advance(TimeSpan.FromMinutes(10));

        Assert.Equal(ApprovalConsumeStatus.NotApproved, harness.Provider.TryConsumeApproved(harness.IntentRef).Status);
        Assert.True(harness.Provider.PurgeExpired() >= 1);
        Assert.Equal(ApprovalConsumeStatus.NotApproved, harness.Provider.TryConsumeApproved(harness.IntentRef).Status);
    }

    [Fact]
    public void Stop_clears_state_and_later_operations_fail_closed()
    {
        var harness = Harness();
        var started = harness.Provider.BeginReview(harness.IntentRef, "doc-a");
        harness.Provider.Stop();
        harness.Provider.Stop();

        Assert.Equal(ApprovalReviewStatus.Unavailable, harness.Provider.BeginReview(harness.IntentRef, "doc-a").Status);
        Assert.Equal(ApprovalCommandStatus.Unavailable, harness.Provider.ApproveCurrent(started.SessionRef, "doc-a").Status);
        Assert.Equal(ApprovalCommandStatus.Unavailable, harness.Provider.RejectCurrent(started.SessionRef, "doc-a").Status);
        Assert.Equal(ApprovalConsumeStatus.NotApproved, harness.Provider.TryConsumeApproved(harness.IntentRef).Status);
        Assert.Equal(ApprovalObservation.None, harness.Provider.ObserveActiveDocument("doc-a"));
    }

    [Fact]
    public void Snapshot_contains_binding_metadata_and_no_preview_payload()
    {
        var properties = typeof(ApprovalSnapshot).GetProperties().Select(property => property.Name).ToArray();
        Assert.Contains("IntentRef", properties);
        Assert.Contains("IntentFingerprint", properties);
        Assert.Contains("InstanceId", properties);
        Assert.Contains("DocumentId", properties);
        Assert.DoesNotContain(properties, name => name.Contains("Item", StringComparison.Ordinal));
        Assert.DoesNotContain(
            typeof(ApprovalSnapshot).GetProperties(),
            property => property.PropertyType.Name.Contains("IntentItem", StringComparison.Ordinal));

        foreach (var type in new[] { typeof(RevitLocalApprovalProviderStateMachine) }.Concat(
                     typeof(RevitLocalApprovalProviderStateMachine).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)))
        {
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var name = field.FieldType.FullName ?? string.Empty;
                Assert.DoesNotContain("Autodesk.Revit", name, StringComparison.Ordinal);
                Assert.DoesNotContain("IntentItemEntry", name, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public async Task Concurrent_approve_and_reject_keep_one_terminal_decision()
    {
        var harness = Harness();
        var started = harness.Provider.BeginReview(harness.IntentRef, "doc-a");
        var barrier = new Barrier(2);
        ApprovalCommandStatus approve = default;
        ApprovalCommandStatus reject = default;

        var approveTask = Task.Run(() =>
        {
            barrier.SignalAndWait();
            approve = harness.Provider.ApproveCurrent(started.SessionRef, "doc-a").Status;
        });
        var rejectTask = Task.Run(() =>
        {
            barrier.SignalAndWait();
            reject = harness.Provider.RejectCurrent(started.SessionRef, "doc-a").Status;
        });
        await Task.WhenAll(approveTask, rejectTask);

        Assert.Equal(1, new[] { approve, reject }.Count(status => status == ApprovalCommandStatus.Recorded));
        Assert.Equal(ApprovalReviewStatus.Terminal, harness.Provider.BeginReview(harness.IntentRef, "doc-a").Status);
        var consumed = harness.Provider.TryConsumeApproved(harness.IntentRef);
        if (approve == ApprovalCommandStatus.Recorded)
        {
            Assert.Equal(ApprovalConsumeStatus.Consumed, consumed.Status);
        }
        else
        {
            Assert.Equal(ApprovalConsumeStatus.NotApproved, consumed.Status);
        }
    }

    private static HarnessState Harness()
    {
        var clock = new ManualTimeProvider();
        var draws = new Queue<byte[]>(new[] { Bytes(1), Bytes(2), Bytes(3), Bytes(4) });
        var store = new EphemeralWriteIntentStore(clock, draws.Dequeue);
        var created = store.TryCreate(Draft());
        var sessions = new Queue<string>(new[] { "session-1", "session-2", "session-3", "session-4" });
        var provider = new RevitLocalApprovalProviderStateMachine(store, clock, sessions.Dequeue);
        return new HarnessState(store, provider, clock, created.IntentRef!, created);
    }

    private static IntentDraft Draft(string documentId = "doc-a", string elementName = "Wall 1")
    {
        return new IntentDraft
        {
            InstanceId = "instance-a",
            DocumentId = documentId,
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
                    ElementName = elementName,
                    CategoryName = "Walls",
                    ParameterName = "Comments",
                    DataTypeKind = DescribeParameterDataTypeKind.Spec,
                    BeforeHasValue = false,
                    Proposed = new IntentTypedValue.StringValue("proposed")
                }
            }
        };
    }

    private static byte[] Bytes(byte marker)
    {
        var bytes = new byte[32];
        BinaryPrimitives.WriteInt32BigEndian(bytes, marker);
        bytes[4] = marker;
        return bytes;
    }

    private sealed record HarnessState(
        EphemeralWriteIntentStore Store,
        RevitLocalApprovalProviderStateMachine Provider,
        ManualTimeProvider Clock,
        string IntentRef,
        IntentCreateResult Created);
}
