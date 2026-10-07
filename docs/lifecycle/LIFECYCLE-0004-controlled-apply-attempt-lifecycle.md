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

`IntentStoreCreateStatus.Rejected` remains a failed insertion. It is not a human rejection and not an apply status.

CAP-0008 public statuses are the only outcomes this store may later surface. This specification does not define Bridge protocol v10.

## Ownership

There is one apply-attempt store per RevitMCP Addin process lifetime.

It lives inside `RevitMCP.Addin`.

It is not owned by the Server, the Bridge, the MCP client, the model, an MCP App, or an orchestrator.

The store is empty when the process starts. Process restart never restores attempts.

Entries are scoped to the process `instance_id` and the intent's `document_id`. `document_id` is internal correlation for document-close cleanup. It is not an MCP input and not a public result field.

## What may be stored

Stored data is RevitMCP-owned bookkeeping only:

- `intent_ref`;
- `document_id`;
- attempt state: reserved, claimed, or one terminal CAP-0008 status;
- completion time and monotonic expiry metadata for a terminal outcome.

Do not store:

- approval decisions or `session_ref`;
- `intent_fingerprint`;
- before or proposed values;
- raw Revit ids or internal-unit doubles;
- live Revit API wrappers (`Document`, `Element`, `Parameter`, `Transaction`, `Connector`, or similar).

No Revit wrapper may be retained between calls. Mutation uses wrappers only inside the EXEC-0001 execution that performs that attempt.

## Lifetime

A non-terminal reservation or claim lasts only while that attempt is unfinished.

A terminal outcome is retained for **30 minutes** after completion. The caller cannot choose or extend that TTL.

Expiry enforcement uses an injectable monotonic clock captured at completion, in the same way LIFECYCLE-0002 enforces intent expiry. Comparing the wall clock with a stored timestamp must not decide validity. Moving the wall clock must not extend a terminal outcome.

Terminal outcomes may outlive the source intent. The intent TTL remains 10 minutes. Apply-attempt lookup of a terminal outcome does not require the intent to still be live.

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
1. If a terminal outcome is stored, return it.
   Do not require the source intent to still be live.
   Do not start a transaction.

2. If an attempt is already claimed and not yet terminal, return unavailable.
   Do not start a second transaction.
   Do not replace the in-progress attempt.

3. Require a live exact intent in EphemeralWriteIntentStore.
   Unknown, expired, forgotten, or otherwise absent is unavailable.
   Do not reveal which absence occurred.

4. If the active document is not the intent's bound document,
   return unavailable.
   Do not consume approval.

5. Reserve one capacity slot.
   If the reserve fails, return unavailable.

6. TryConsumeApproved(intent_ref).
   Consumption and the transition from reserved to claimed are one
   in-process atomic decision.
   If consumption fails, release the reservation, create no terminal
   outcome, and return approval_required.
   approval_required does not reveal why consumption failed.

7. Once claimed, the attempt can end only in a terminal outcome:
   stale, transaction_failed, committed_unverified, indeterminate, or applied.

8. Revalidate exactly as CAP-0008 requires.
   Failure stores stale and starts no transaction.

9. Start one CAP-0008 transaction.
   A known non-committed outcome stores transaction_failed.
   An uncertain outcome, including Pending, stores indeterminate.

10. After finalized Committed, verify as CAP-0008 requires.
    Success stores applied.
    Verification failure stores committed_unverified.

11. Store the terminal outcome before returning it.
```

A claimed attempt must not return to reserved, and it must not be deleted to allow a second claim while the document remains open. The only removals are terminal expiry, successful document close, and process shutdown.

## Concurrency

Store operations are thread-safe inside the process.

Invariants:

- at most one attempt record exists for one `intent_ref`;
- at most one of those attempts can be claimed;
- only one consumption of the matching approval can succeed;
- a terminal outcome is immutable;
- no terminal outcome starts a Revit transaction;
- document forget and shutdown make the affected records unusable.

Do not introduce distributed locking. One in-process mutual exclusion is enough.

Do not hold the store lock while executing arbitrary Revit API work. The claim is recorded before EXEC-0001 mutation work, and the terminal outcome is recorded after that work finishes.

## Timeout and cancellation

Preserve EXEC-0001.

- Cancellation or timeout before the EXEC-0001 work item begins does not claim an attempt and does not consume approval. Any reservation made for a call that never starts is released.
- Once the queued apply execution has begun, do not abort the Revit thread and do not cancel the Revit transaction from the timed-out caller.
- That execution finishes the CAP-0008 outcome and stores it, even if the caller has already stopped waiting.
- A later apply with the same `intent_ref` returns the stored terminal outcome and does not mutate again.
- A timeout response is not proof that no attempt was claimed.

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
TryGetTerminal(intent_ref)
  -> outcome | absent

TryReserve(intent_ref, document_id)
  -> reserved | exhausted | in_progress | terminal(outcome)

ReleaseReservation(intent_ref)
  -> released

Claim(intent_ref)
  -> claimed | absent

Complete(intent_ref, terminal_status)
  -> stored

ForgetDocument(document_id)
  -> removed

PurgeExpired()
  -> removed

Clear()
  -> removed
```

`TryGetTerminal` returns a stored terminal outcome even when the source intent is gone. `absent` covers unknown refs and expired or forgotten outcomes. It does not reveal which.

`Complete` accepts only the five terminal CAP-0008 statuses `applied`, `stale`, `transaction_failed`, `committed_unverified`, and `indeterminate`. It does not accept `approval_required` or `unavailable`.

## Pure tests

A future implementation must cover these cases without launching Revit:

1. A second claim for one `intent_ref` does not create a second attempt.
2. `approval_required` releases the reservation and leaves no terminal outcome.
3. A claimed attempt accepts only a terminal completion.
4. Stored `stale`, `transaction_failed`, `committed_unverified`, `indeterminate`, and `applied` are returned on retry without a new claim.
5. A terminal outcome is returned after the source intent is absent.
6. A terminal outcome is valid before 30 monotonic minutes and absent at 30 monotonic minutes.
7. Moving the UTC clock does not extend that monotonic expiry.
8. Purging an expired terminal outcome frees a capacity slot.
9. 128 live attempts are retained and another reserve is exhausted without evicting them.
10. Successful document close removes that document's attempts and does not create a document id. Cancelled close preserves them.
11. Shutdown clear removes every attempt.
12. No operation lists or searches attempts.
13. Stored records contain no values, fingerprints, session refs, raw ids, or Revit wrappers.
14. Concurrent reserves never leave more than 128 live attempts, and concurrent claims for one ref allow at most one claim.

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
