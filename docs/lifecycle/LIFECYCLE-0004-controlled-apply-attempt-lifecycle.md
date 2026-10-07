# LIFECYCLE-0004: Controlled apply-attempt lifecycle

- Status: Proposed
- Date: 2026-10-07

## Purpose

Define the Addin-owned apply-attempt store required by proposed CAP-0008 and by ADR-0008's rule that an intent may cause at most one mutation attempt.

The store remembers the outcome of a controlled apply so a retry, including a retry after the caller timed out, returns that outcome and does not open a second Revit transaction.

This specification is Proposed. It does not implement the store. It does not authorize a Revit model write, a Bridge or Server specification, or production apply code.

## Relationship

LIFECYCLE-0002 and LIFECYCLE-0003 stay unchanged.

Do not add apply state to:

- `IntentEntry`;
- `EphemeralWriteIntentStore`;
- approval-provider terminal decision objects;
- the consumed-approval tombstone.

`TryConsumeApproved` remains the LIFECYCLE-0003 operation. This specification defines when a future apply calls it. It does not change what `not_approved` reveals.

The approval store and the apply-attempt store are separate. Owning a record in one and then calling the other is ordered coordination. It is not one atomic transaction across those stores.

`IntentStoreCreateStatus.Rejected` remains a failed insertion. It is not a human rejection and not an apply status.

CAP-0008 public statuses are the only outcomes this store may later surface. This specification does not define Bridge protocol v10.

## Ownership

There is one apply-attempt store per RevitMCP Addin process lifetime.

It lives inside `RevitMCP.Addin`.

It is not owned by the Server, the Bridge, the MCP client, the model, an MCP App, or an orchestrator.

The store is empty when the process starts. Process restart never restores attempts.

Entries are scoped to the process `instance_id` and the intent's `document_id`. `document_id` is internal correlation for document-close cleanup. It is not an MCP input and not a public result field.

## What may be stored

Stored data is RevitMCP-owned bookkeeping only. An attempt record holds at least:

- `intent_ref`;
- `intent_fingerprint`;
- `instance_id`;
- `document_id`;
- attempt state: pre-mutation, irrevocable, or terminal;
- terminal status when complete;
- terminal timing and monotonic expiry metadata when complete.

Those fields are copied from the live intent when the exclusive record is established. They are internal. They are never MCP output.

Do not store:

- approval decisions or `session_ref`;
- before or proposed values;
- raw Revit ids or internal-unit doubles;
- live Revit API wrappers (`Document`, `Element`, `Parameter`, `Transaction`, `Connector`, or similar).

No Revit wrapper may be retained between calls. Mutation uses wrappers only inside the EXEC-0001 execution that performs that attempt.

## Lifetime

A non-terminal record, whether still pre-mutation or already irrevocable, lasts until it is released under the pre-mutation rule, completed, forgotten with its document, or cleared at shutdown.

A terminal outcome is retained for **30 minutes** after completion. The caller cannot choose or extend that TTL.

Expiry enforcement uses an injectable monotonic clock captured at completion, in the same way LIFECYCLE-0002 enforces intent expiry. Comparing the wall clock with a stored timestamp must not decide validity. Moving the wall clock must not extend a terminal outcome.

Terminal outcomes may outlive the source intent. The intent TTL remains 10 minutes. A matching terminal lookup does not require the source intent to still be live.

If a live intent with the same `intent_ref` has a different `intent_fingerprint`, `instance_id`, or `document_id` than the stored attempt, fail closed. Return CAP-0008 `unavailable`. Do not return the old terminal outcome. Do not start a new mutation. Do not delete the stored attempt because of that mismatch. After that live intent is no longer present, a later lookup may return the stored terminal outcome under the rule above.

Successful document close removes every attempt for that `document_id`, including unexpired terminal outcomes.

A cancelled or failed close does not remove attempts for a document that is still open.

Process shutdown clears every attempt. A restarted process has an empty store and cannot replay an old `intent_ref`.

## Capacity

v1 allows at most **128** attempts that are either unfinished or unexpired terminal outcomes, per Revit process. The limit is per process, not per document.

Before declaring capacity exhaustion, purge expired terminal outcomes.

If 128 remain, a new attempt is refused. Do not evict an unfinished attempt or an unexpired terminal outcome. No LRU or FIFO replacement is allowed.

Document close and shutdown are removal, not eviction-for-capacity.

Capacity exhaustion returns CAP-0008 `unavailable`. It does not consume approval and does not create a terminal outcome.

## Flow

Lookup and claim are exact ordinal matches on the full `intent_ref`. There is no prefix search.

```text
1. If a live intent has this intent_ref and its fingerprint, instance,
   or document binding differs from a stored attempt, return unavailable.
   Do not return the old terminal outcome.
   Do not start a mutation.

2. If a terminal outcome is stored and step 1 does not apply, return it.
   The source intent does not need to still be live.
   Do not start a transaction.

3. If an exclusive record already exists and is not terminal, return in_progress.
   Do not start a second transaction.
   Do not reveal the approval decision.
   Do not replace or release that record from this call.

4. Require a live exact intent in EphemeralWriteIntentStore.
   Unknown, expired, forgotten, or otherwise absent is unavailable.
   Do not reveal which absence occurred.

5. If the active document is not the intent's bound document,
   return unavailable.
   Do not consume approval.

6. Establish one exclusive pre-mutation record for that binding,
   or refuse when capacity is exhausted.
   Capacity exhaustion is unavailable.
   A losing caller observes in_progress.
   This record is not yet irrevocable.

7. Only the owner of that record may call TryConsumeApproved(intent_ref).
   The two stores are not one atomic transaction.
   If TryConsumeApproved returns not_approved, the owner removes that
   pre-mutation record, creates no terminal outcome, and returns
   approval_required.
   approval_required does not reveal why consumption failed.

8. Once TryConsumeApproved succeeds, the same record becomes irrevocable.
   It may only progress to a terminal outcome:
   stale, transaction_failed, committed_unverified, indeterminate, or applied.
   No normal code path may release it.

9. Revalidate exactly as CAP-0008 requires.
   Failure stores stale and starts no transaction.

10. Start one CAP-0008 transaction.
    A known non-committed outcome stores transaction_failed.
    An uncertain outcome, including Pending, stores indeterminate.

11. After finalized Committed, verify as CAP-0008 requires.
    Success stores applied.
    HasValue == false or any other verification failure stores
    committed_unverified.

12. Store the terminal outcome before returning it.
```

An irrevocable record must not return to pre-mutation, and it must not be deleted to allow a second owner while the document remains open. Releasing a record is allowed only for the owner, and only when `TryConsumeApproved` returned `not_approved`. The other removals are terminal expiry, successful document close, and process shutdown.

## Concurrency

Store operations are thread-safe inside the process.

Invariants:

- at most one attempt record exists for one `intent_ref`;
- at most one caller owns that record;
- only that owner may call `TryConsumeApproved` for it;
- after consumption succeeds, no normal path releases the record;
- a terminal outcome is immutable;
- no terminal outcome, and no `in_progress` result, starts a Revit transaction;
- document forget and shutdown make the affected records unusable.

Do not introduce distributed locking. One in-process mutual exclusion for the apply-attempt store is enough. Do not describe that lock plus the approval-provider lock as a single atomic commit.

Do not hold the store lock while executing arbitrary Revit API work. The exclusive record exists before `TryConsumeApproved`. The terminal outcome is recorded after Revit work finishes.

## Timeout and cancellation

Preserve EXEC-0001.

- Before an exclusive record exists, cancellation consumes nothing.
- While the record is still pre-mutation, cancellation releases it, does not call `TryConsumeApproved`, and stores no terminal outcome.
- After `TryConsumeApproved` succeeds, the record is irrevocable. A caller timeout does not release it.
- Once the queued apply execution has begun, do not abort the Revit thread and do not cancel the Revit transaction from the timed-out caller.
- That execution finishes the CAP-0008 outcome and stores it, even if the caller has already stopped waiting.
- A later apply returns `in_progress` until the terminal outcome is stored. It then returns that outcome when the binding matches, and it does not mutate again.
- A timeout response is not proof that no exclusive record exists.

## Document close and shutdown

Reuse the existing successful `DocumentClosing` / `DocumentClosed` correlation. `DocumentClosing` `DocumentId` is only an event-pair key. It is not a RevitMCP `document_id`.

On successful close, remove apply-attempt state for the existing `document_id` without creating a document id. Do not change the LIFECYCLE-0003 order among provider state, intent state, parameter-ref state, and document identity. Document identity is still forgotten last. After that close, no terminal outcome for that document remains.

Cancelled and failed closes preserve apply-attempt state.

Shutdown clears the store before process-owned bridge and dispatcher resources are fully released. Entering stopping rejects new claims. A later operation in that process does not restore cleared outcomes.

## No public enumeration

Do not add listing, search, prefix lookup, recent-attempts, or an audit export on this store.

Audit remains a separate design. This store is not the audit sink.

The only lookup is the exact `intent_ref` used by the apply flow above.

## Internal conceptual API

Names are conceptual. The implementation chooses the final type names. These operations are not MCP tools and not Bridge RPC methods.

```text
TryLookup(intent_ref, live_binding | absent)
  -> terminal(outcome) | in_progress | binding_mismatch | absent

TryEstablishExclusive(intent_ref, fingerprint, instance_id, document_id)
  -> owner | in_progress | exhausted

ReleasePreMutation(intent_ref)
  -> released | rejected

MarkIrrevocable(intent_ref)
  -> irrevocable | absent

Complete(intent_ref, terminal_status)
  -> stored

ForgetDocument(document_id)
  -> removed

PurgeExpired()
  -> removed

Clear()
  -> removed
```

`TryLookup` returns a stored terminal outcome when the source intent is absent, and when a live intent has the same fingerprint, instance, and document binding. `binding_mismatch` means a live intent has the same `intent_ref` and a different binding: the caller returns `unavailable`, does not read out the old outcome, and does not mutate. `absent` covers no stored attempt. It does not reveal whether an unknown ref expired.

`ReleasePreMutation` succeeds only for the owner, and only before `TryConsumeApproved` has succeeded. `rejected` means the record is already irrevocable or terminal. No normal path may release it then.

`MarkIrrevocable` is the owner's record after `TryConsumeApproved` succeeds. It does not itself call the approval provider.

`Complete` accepts only the five terminal CAP-0008 statuses `applied`, `stale`, `transaction_failed`, `committed_unverified`, and `indeterminate`. It does not accept `approval_required`, `unavailable`, or `in_progress`.

## Pure tests

A future implementation must cover these cases without launching Revit:

1. A second caller for one `intent_ref` does not create a second record and observes `in_progress` until a terminal outcome exists.
2. `not_approved` releases only the pre-mutation record, leaves no terminal outcome, and returns `approval_required`.
3. After `TryConsumeApproved` succeeds, `ReleasePreMutation` is rejected and the record accepts only a terminal completion.
4. Stored `stale`, `transaction_failed`, `committed_unverified`, `indeterminate`, and `applied` are returned on retry without a new owner when the binding matches or the source intent is absent.
5. A live intent with the same `intent_ref` and a different fingerprint, instance, or document binding does not return the old terminal outcome and does not start a mutation.
6. A terminal outcome is valid before 30 monotonic minutes and absent at 30 monotonic minutes.
7. Moving the UTC clock does not extend that monotonic expiry.
8. Purging an expired terminal outcome frees a capacity slot.
9. 128 live attempts are retained and another exclusive establish is exhausted without evicting them.
10. Successful document close removes that document's attempts and does not create a document id. Cancelled close preserves them.
11. Shutdown clear removes every attempt.
12. No operation lists or searches attempts.
13. Stored records keep fingerprint, instance, and document binding internally, and contain no BIM values, session refs, raw ids, or Revit wrappers. MCP output does not include those internal fields.
14. Concurrent establishes never leave more than 128 live attempts, and concurrent callers for one ref allow at most one owner.
15. Only the owner calls `TryConsumeApproved`. A non-owner does not.

## Explicitly excluded

- production apply code;
- changes to `IntentEntry` or `EphemeralWriteIntentStore`;
- changes to approval-provider decision objects;
- Bridge protocol v10;
- Server or MCP registration;
- `Parameter.Set`;
- `Transaction`, `SubTransaction`, and `TransactionGroup`;
- an audit sink, retention policy, or log package;
- save, sync, and worksharing checkout;
- MRTR, MCP Apps, and external approval providers;
- cross-process persistence.

## References

- ADR-0008: controlled write safety model
- ADR-0010: Revit-local trusted approval authority and provider contract
- CAP-0007: `revit_preview_parameter_updates`
- CAP-0008: `revit_apply_parameter_updates`
- LIFECYCLE-0002: ephemeral write-intent store
- LIFECYCLE-0003: Revit-local trusted approval provider lifecycle
- EXEC-0001: Revit execution queue
