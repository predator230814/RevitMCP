# CAP-0008: `revit_apply_parameter_updates`

- Status: Accepted
- Amended: 2026-10-07 — conform to Accepted ADR-0011
- Operation class: Write
- Date: 2026-10-07
- Product Owner acceptance: Dave, 2026-10-07

## Purpose

`revit_apply_parameter_updates` is the apply half of the ADR-0008 write workflow:

```text
preview
-> immutable intent
-> trusted human approval
-> stale-state revalidation
-> one controlled Revit transaction
-> verification
-> audit
```

It asks one exact Revit process to apply one existing CAP-0007 immutable intent, and only after that process's trusted local approval provider has an unconsumed approval for that intent.

This specification is Accepted. Dave, Product Owner, accepted it on 2026-10-07. It conforms to the Accepted ADR-0011 write-audit contract. Acceptance does not mean an implementation exists. No production code, audit writer, Bridge method, Server tool, or Revit model write exists under this acceptance.

Apply is not preview and not request-review. Preview creates the intent. Request-review presents it. Apply is the only path in this workflow that may mutate parameter values.

## MCP tool

### Name

`revit_apply_parameter_updates`

### Title

`Apply Revit Parameter Updates`

### Description

Apply one previously previewed and locally approved batch of instance-parameter updates in the exact addressed Revit process. Returns only a closed status. Does not accept new values, a confirmation flag, or a human decision.

### Annotations

- `readOnlyHint: false`
- `destructiveHint: true`
- `idempotentHint: false`
- `openWorldHint: false`

`readOnlyHint` is false because a successful apply mutates the Revit model.

`destructiveHint` is true because the MCP annotation meaning of `false` is that the tool performs only additive updates. Replacing a parameter value is not additive. The hint does not classify the operation as element deletion, and it does not bypass ADR-0008. The operation class remains Write. ADR-0009 keeps destructive behavior distinguishable and write-class; this annotation is that distinction on the MCP surface.

`idempotentHint` is false because the call is not advertised as safe to repeat as a fresh mutation. LIFECYCLE-0004 separately forbids a second Revit transaction for the same intent. The hint is not approval and not that lifecycle rule.

`openWorldHint` is false because the tool does not reach outside the addressed Revit document and the Addin-owned intent, approval, and apply-attempt state.

Annotations are descriptive hints. ADR-0008 forbids treating them as approval.

## MCP input

The MCP-facing input is a closed object with exactly two required properties:

```text
instance_id
intent_ref
```

Both are the exact opaque values returned by `revit_preview_parameter_updates` for the batch to apply.

`instance_id` is required. There is no implicit instance, no "current" instance, and no fallback to another Revit process.

`intent_ref` is the LIFECYCLE-0002 opaque token. Clients must not parse it.

The input must not contain any of the following:

- `document_id`;
- `intent_fingerprint` or any fingerprint;
- `session_ref`;
- proposed or before values;
- a `confirm` flag or any equivalent;
- approval text;
- model, provider, or client identity, including `clientInfo`;
- a human decision, including Approve, Reject, or an MRTR `inputResponses` acceptance.

Unexpected properties are rejected before an apply attempt. That rejection is not an apply status. This specification does not name a Server error code and does not define Server registration.

## Public result

When the addressed Addin handles the apply, the public result is a closed object containing only:

```text
status
```

`status` is exactly one of:

```text
applied
approval_required
unavailable
in_progress
stale
transaction_failed
committed_unverified
indeterminate
audit_failed
```

`audit_failed` is the only new public status in this amendment.

No other property is present. The result must not expose:

- an approval decision, including approved, rejected, dismissed, or never reviewed;
- `session_ref`;
- `intent_fingerprint` or any fingerprint;
- raw before or proposed values;
- raw Revit ids, `ElementId`, `UniqueId`, or internal-unit doubles;
- a transaction id, undo id, or verification payload;
- audit fields, including hashes, `approval_method`, `audit_stream_ref`, `attempt_ref`, or any audit event payload.

Routing failures that happen before the addressed Addin accepts the call, including an unknown `instance_id`, stay outside this object. They follow ADR-0003 exact-instance routing. They are not rewritten as one of the statuses above, and they must not select a different Revit process.

## Status semantics

### `applied`

The Revit transaction reached finalized `TransactionStatus.Committed`, and post-write verification of every target succeeded.

This is the only success status.

### `approval_required`

No consumable trusted local approval exists for the live intent.

This status does not reveal whether the person rejected, dismissed, or never reviewed. The pre-mutation apply record is released. No terminal outcome is created, and approval is not consumed.

### `unavailable`

The exact intent cannot currently be applied, and no apply attempt owns it.

This covers:

- unknown, expired, or forgotten intent;
- wrong active document;
- attempt-capacity exhaustion;
- ADR-0011 audit preflight failure before `TryConsumeApproved`, including a degraded audit sink;
- equivalent bounded states in LIFECYCLE-0004 where no attempt owns the intent.

A live intent with the same `intent_ref` but a different fingerprint, instance, or document binding than a stored attempt is also `unavailable`. Do not return the old terminal outcome. Do not start a mutation.

`unavailable` does not reveal which of those occurred. It does not consume approval and does not create a terminal attempt. Do not use `audit_failed` before approval consumption.

### `in_progress`

An exclusive apply record for this exact intent already exists, and no terminal outcome is available yet.

This call starts no second transaction. It does not reveal whether the human approved, rejected, dismissed, or never reviewed. It does not replace or release the existing record.

### `audit_failed`

Trusted approval was successfully consumed, and the mandatory v1 write-ahead audit contract could not be established before mutation.

No Revit mutation began. The outcome is terminal. That intent never starts another mutation. Approval cannot be reused.

This covers at least:

1. the mandatory `apply_started` event could not be durably persisted;
2. mandatory audit metadata cannot be represented under ADR-0011's closed v1 schema after approval consumption, including an unmapped `ApprovalSnapshot.ProviderMethod`.

For both:

- no Revit `Transaction` starts;
- no `Parameter.Set` occurs;
- store the terminal apply-attempt outcome `audit_failed` first;
- any `apply_completed` line is best-effort only;
- raw and unmapped provider strings are never persisted.

A later apply with the same binding returns this stored outcome and must not mutate.

### `stale`

An irrevocable attempt failed exact stale-state revalidation after `apply_started` was durably written.

No Revit transaction was started. The outcome is terminal for that intent. A later apply with the same binding returns this stored outcome and must not mutate.

The durable `apply_started` line is intentional. Approval was consumed and the controlled attempt began, even though stale revalidation required no Revit `Transaction`.

A current value that already equals the proposed value, because something else changed the model, is `stale`. It is not `applied`.

### `transaction_failed`

A mutation attempt began, and no successful commit occurred. The non-committed outcome is known, including finalized `TransactionStatus.RolledBack`.

The outcome is terminal. A later apply must not start another transaction.

### `committed_unverified`

Revit returned finalized `TransactionStatus.Committed`, then exact post-write verification failed or threw.

The model may have changed. The outcome is terminal. A later apply must not start another transaction and must not report `applied`.

### `indeterminate`

The final transaction outcome cannot be established safely. This includes unresolved `TransactionStatus.Pending` and any uncertain commit outcome.

`Pending` is not success. The outcome is terminal. A later apply must not start another transaction to resolve it.

If `Commit` returns unresolved `Pending`:

- store terminal `indeterminate` first;
- do not mutate again;
- do not perform `apply_completed` audit I/O while `Pending` failure processing remains unresolved;
- the durable `apply_started` line may remain unmatched;
- a retry returns `indeterminate`;
- v1 adds no synchronous `Pending` recovery.

## Instance routing

The MCP host routes to the Revit process named by `instance_id` and to no other.

Missing, stale, or ineligible instance selection is a routing failure. It is not an invitation to retry against a different registered Revit process.

The transport-neutral Addin request does not carry `instance_id`. The pipe already addresses that process.

## Transport-neutral request

Conceptually, the Addin request contains only:

```text
intent_ref
```

It does not contain `document_id`, fingerprint, `session_ref`, values, a confirm flag, approval text, model or client identity, or a human decision.

The Addin resolves the authoritative intent, document, targets, and values from `EphemeralWriteIntentStore`. Caller-supplied preview content is not authority.

This specification does not define a Bridge method name, a protocol version, or a Server schema. Those remain future specifications. Bridge protocol v10 is not created here.

## Apply ordering

The accepted order is:

```text
1. Terminal and binding retry lookup.
2. Require a live exact intent.
3. Active-document and binding checks.
4. Establish one exclusive pre-mutation apply record.
5. Run ADR-0011 audit preflight.
6. If preflight fails, release that record and return unavailable.
7. TryConsumeApproved(intent_ref).
8. If not_approved, release that record and return approval_required.
9. Approval consumed makes the record irrevocable.
10. Validate and map mandatory audit metadata.
11. If that metadata is unrepresentable, store terminal audit_failed
    and start no transaction.
12. Durably append apply_started and Flush(flushToDisk: true).
13. If that write-ahead barrier fails, store terminal audit_failed
    and start no transaction.
14. Exact stale-state revalidation.
15. If stale, store terminal stale.
16. One controlled Revit Transaction.
17. Determine the transaction outcome.
18. Post-commit verification when applicable.
19. Store the immutable LIFECYCLE-0004 terminal outcome first.
20. Then attempt apply_completed only when ADR-0011 permits it.
21. Return the public terminal outcome.
```

Steps 12 through 21 run only after approval consumption. Steps 14 through 21 run only after a durable `apply_started`. Steps 16 through 18 run only when revalidation is not `stale`. Step 15 is the terminal store for `stale`, and step 20 still attempts `apply_completed` for that stored outcome. Steps 11 and 13 store `audit_failed` and then use only the best-effort completion ADR-0011 allows. Step 19 is the terminal store for a transaction or verification outcome, including unresolved `Pending`, which does not attempt `apply_completed`.

Preflight runs after the exclusive pre-mutation record exists and before `TryConsumeApproved`. It includes the Accepted ADR-0011 checks: audit writer and sink availability, current stream availability, bounded housekeeping, retention and admission, the required 16 KiB local headroom, and representable pre-consumption metadata.

Preflight failure releases the pre-mutation record, does not consume approval, starts no transaction, and returns `unavailable`.

The accepted v1 approval-method mapping is exact ordinal equality:

```text
ApprovalSnapshot.ProviderMethod == "revit-local-in-process"
-> audit approval_method = revit_local_in_process
```

There is no arbitrary normalization. An unmapped provider method after successful consumption is `audit_failed`. No mutation begins. The raw provider string is not written.

Once ADR-0011 marks the process-owned audit sink degraded, a new apply fails preflight before approval consumption and returns `unavailable`. Approval stays unconsumed. Reads, CAP-0007 preview, and request-review stay available. Recovery stays inside the ADR-0011 audit writer. This capability adds no MCP recovery command.

## Stale revalidation

Reuse CAP-0007 semantics exactly. Do not invent a second comparison, tolerance, or eligibility rule.

Revalidation runs only after `apply_started` has been durably persisted. It re-resolves current Revit objects in a fresh EXEC-0001 context. It does not trust wrappers stored earlier. The apply-attempt store still holds no Revit wrappers.

Immediately before any `Transaction` or `Parameter.Set`, and only after that durable `apply_started` line, re-resolve and re-check all of the following:

- the active document is the intent's bound project document, using a non-creating document-identity lookup;
- CAP-0007 document gates for that document: project document, not a family document, and `Document.IsReadOnly == false`;
- each element, under the existing opaque `element_ref` rules, and not an `ElementType` input;
- each `parameter_ref` in this open-document lifetime;
- source is `instance`;
- parameter identity, using the CAP-0004 identity already stored on the intent, with no name fallback and no `LookupParameter`;
- the occurrence is present on the element's visible instance parameters;
- the amended CAP-0007 write-eligibility rule: `Parameter.IsReadOnly == false`, and not (`Parameter.IsShared == true` and `Parameter.UserModifiable == false`);
- storage, spec, and value semantics still match the stored proposed kind, including `SpecTypeId.Int.Integer` for integers and the measurable-spec unit rules for quantities;
- human-facing element, category, and parameter names, truncation flags, and `data_type` still match the stored approval-preview payload, as CAP-0007 requires for a later apply;
- the exact before-state.

`Document.IsModifiable` is not a permission signal.

### Before-state

Compare the re-read current state with the intent's stored before-state. No tolerance in v1.

String: ordinal exact equality of the untrimmed `Parameter.AsString()` and the stored before string. A stored before with no value matches only a current parameter that still has no value. A null `AsString()` for a valued string does not match a stored string.

Integer: exact Int32 equality of `Parameter.AsInteger()` and the stored before integer.

Quantity: convert the current internal value with `UnitUtils.ConvertFromInternalUnits` to the stored `unit_type_id`, and compare that number exactly with the stored before quantity. Do not compare raw internal doubles with the stored display number. Do not apply a tolerance.

If the current state equals the proposed state and does not equal the stored before-state, the intent is `stale`. Do not report `applied` and do not skip the write by treating the batch as already done.

Any failed revalidation, including a thrown read, is `stale` for the whole batch. Roll nothing, because no transaction has started. Store terminal `stale` first. Then attempt `apply_completed`. Then return `stale`.

## Transaction

One Revit `Transaction` covers the entire ordered batch.

- no `SubTransaction`;
- no `TransactionGroup`;
- all items commit or none do;
- one successful commit produces one Undo item;
- the transaction name is exactly `RevitMCP Apply Parameter Updates`.

EXEC-0001 remains the execution-context owner. The dispatcher does not start this transaction. Apply logic owns transaction policy and starts it only after the durable `apply_started` barrier and after stale revalidation succeeds.

`Parameter.Set` runs in stored request order:

- string: `Set` the exact proposed string, including the empty string, which is not a clear;
- integer: `Set` the exact proposed Int32;
- quantity: convert the stored proposed number with `UnitUtils.ConvertToInternalUnits` and the stored `unit_type_id`, then `Set` that internal double. Do not use `SetValueString`, `AsValueString`, or display-string parsing.

Every `Parameter.Set` result must be true. A false result or an exception before commit rolls the transaction back when that rollback can be known, stores `transaction_failed` when the non-committed outcome is known, and stores `indeterminate` when the outcome cannot be established. Do not commit a partial batch.

## Failure handling

v1 is conservative.

Any Revit failure message generated during the controlled transaction, whether Revit classifies it as a warning or an error, causes rollback.

The proposed direction is an `IFailuresPreprocessor` that requests `FailureProcessingResult.ProceedWithRollBack`, with failures cleared after rollback so write execution does not leave transaction resolution to an uncontrolled Revit user dialog.

Do not whitelist warnings in v1. Do not continue a transaction that produced a warning.

Check `TransactionStatus` explicitly.

- Finalized `Committed`: continue to post-commit verification. Do not return `applied` yet.
- Finalized `RolledBack`: store `transaction_failed`.
- `Pending`: store `indeterminate`. Never treat `Pending` as success and never start another transaction while it is unresolved. Do not perform `apply_completed` audit I/O while that failure processing remains unresolved.
- Any other or unreadable status: store `indeterminate`.

## Post-commit verification

After finalized `Committed` only, re-resolve and re-read every target in a fresh resolution. Do not trust the pre-commit wrappers.

For every supported proposed value, verification first requires `Parameter.HasValue == true`. A parameter that has no value never verifies as `applied`. CAP-0008 does not support clear or unset.

Then:

- string: `Parameter.AsString()` is an ordinal exact match of the proposed string;
- integer: `Parameter.AsInteger()` exactly equals the proposed Int32;
- quantity: `Parameter.AsDouble()` exactly equals the internal double passed to `Set` during this same execution. No tolerance. Do not publish that double.

Any verification exception, `HasValue == false`, or mismatch stores `committed_unverified` first. The model may have changed. Do not roll back a transaction Revit has already committed. Do not retry the mutation. Then attempt `apply_completed`, and return `committed_unverified` even if that completion write fails.

Only a completed verification of every target stores `applied`. Store `applied` first, then attempt `apply_completed`, then return `applied`.

## Retry and double commit

LIFECYCLE-0004 owns the apply-attempt store. This capability does not add apply state to `IntentEntry`, `EphemeralWriteIntentStore`, or approval-provider decision objects.

An intent causes at most one mutation attempt capable of committing.

LIFECYCLE-0004 establishes one exclusive apply record, then ADR-0011 preflight runs, and only then may the owner call `TryConsumeApproved`. The approval store and the apply-attempt store are not one atomic transaction. If preflight fails, that pre-mutation record is released and the result is `unavailable`. If consumption returns not approved, that pre-mutation record is released and the result is `approval_required`. After consumption succeeds, the record is irrevocable and can end only as a terminal outcome.

A retry returns the stored terminal outcome when one exists and the binding still matches, including after the source intent TTL has elapsed, and does not open a transaction. A second call that finds the exclusive record before a terminal outcome returns `in_progress`.

If a live intent has the same `intent_ref` and a different fingerprint, instance, or document binding, the result is `unavailable`. Do not return the old terminal outcome and do not start a mutation.

A timeout or cancellation after the queued apply execution has begun does not abort the Revit thread. That execution finishes its controlled outcome and stores it. The caller may already have stopped waiting.

## Audit

ADR-0008 audit is mandatory for a production apply. Accepted ADR-0011 is the v1 audit contract: ownership, storage, durability, data minimization, retention, admission, and failure behavior. This capability uses that contract. It does not restate the closed event schema.

Public MCP results do not expose audit fields, raw BIM values, hashes, approval method, audit stream refs, or attempt refs.

For every normal finalized terminal outcome, store the immutable LIFECYCLE-0004 terminal outcome first, then attempt and `Flush(flushToDisk: true)` `apply_completed`, then return the CAP result.

If `apply_completed` fails:

- do not change the already-known CAP or model outcome;
- do not change the terminal apply-attempt outcome;
- do not retry the mutation;
- mark the audit sink degraded;
- return the known outcome.

`applied` stays `applied`. `transaction_failed` stays `transaction_failed`. `stale` stays `stale`. `committed_unverified` stays `committed_unverified`. A finalized `indeterminate` stays `indeterminate`.

`audit_failed` stores its terminal outcome first. Its `apply_completed` line is best-effort only. Unresolved `Pending` stores `indeterminate` and performs no completion-audit I/O while failure processing remains unresolved.

This specification is Accepted. Acceptance records the contract. It does not mean a production apply or audit writer exists.

## What this capability does not do

- no Bridge protocol v10 and no Bridge method definition;
- no Server or MCP registration;
- no production `Parameter.Set`;
- no production `Transaction`, `SubTransaction`, or `TransactionGroup`;
- no production audit-writer implementation;
- no save or synchronize;
- no worksharing checkout, borrow, or relinquish;
- no type-parameter writes;
- no ElementId or reference-parameter writes;
- no clearing or unsetting;
- no family-document writes;
- no element creation, deletion, or geometry writes;
- no autonomous or agent-decided approval;
- no MRTR implementation and no use of `inputResponses` as approval;
- no MCP App;
- no external approval provider;
- no arbitrary Python or other generated code execution.

## Acceptance criteria

A future implementation of this Accepted specification must show:

1. MCP input is only exact `instance_id` and `intent_ref`, and another Revit instance is never selected.
2. The Addin request is only `intent_ref`.
3. The public result object contains only `status` and one of the nine values.
4. No consumable approval releases the pre-mutation record, returns `approval_required`, consumes nothing, and stores no terminal outcome.
5. Unknown, expired, forgotten, or wrong-document intent state, and capacity exhaustion, return `unavailable` when no attempt owns the intent.
6. Audit preflight failure returns `unavailable`, releases the pre-mutation record, and does not consume approval.
7. A second call that finds an exclusive record and no terminal outcome returns `in_progress`, starts no transaction, and does not reveal the approval decision.
8. A live intent whose fingerprint, instance, or document binding differs from the stored attempt returns `unavailable`, does not return the old terminal outcome, and does not mutate.
9. A durable `apply_started` line is required before any `Transaction`.
10. Metadata that cannot be represented after approval consumption, and a failed write-ahead barrier, return `audit_failed`, start no `Transaction`, and call no `Parameter.Set`.
11. A retry of terminal `audit_failed` returns that outcome and does not mutate.
12. An irrevocable attempt that fails CAP-0007 revalidation returns `stale`, starts no transaction, and stays terminal. `apply_started` was already durable.
13. A current value that equals the proposal without equaling the stored before-state returns `stale`, not `applied`.
14. One transaction named `RevitMCP Apply Parameter Updates` commits the whole batch or none of it, with one Undo item only on success.
15. Any warning or error during that transaction rolls back.
16. `RolledBack` is `transaction_failed`. Unresolved `Pending` stores `indeterminate`, emits no completion audit, and does not start a second transaction. Any other uncertain commit outcome is `indeterminate`. Neither status retries the mutation.
17. Finalized `Committed` verifies `HasValue == true` and then the exact proposed string, Int32, or internal double. Success is `applied`. `HasValue == false` or any mismatch is `committed_unverified`.
18. The terminal apply-attempt outcome is stored before `apply_completed`.
19. A failed `apply_completed` preserves the known CAP outcome, including `applied`, `transaction_failed`, `stale`, `committed_unverified`, and finalized `indeterminate`.
20. A degraded audit sink blocks a new apply at preflight, before approval consumption, and returns `unavailable`.
21. A second apply returns the stored terminal outcome when the binding matches and does not call `Parameter.Set` again.
22. The result exposes no approval decision, `session_ref`, fingerprint, raw values, raw Revit ids, or public audit metadata.

## Explicitly deferred

- Bridge protocol v10 and the RPC name;
- Server/MCP registration and transport error codes;
- production audit-writer code;
- production apply code;
- save, sync, and worksharing checkout;
- type parameters, reference values, clear/unset, family writes, and non-parameter writes;
- autonomous approval, MRTR, MCP Apps, and external approval providers.

## References

- ADR-0008: controlled write safety model
- ADR-0009: agent and human interaction and trusted approval boundary
- ADR-0010: Revit-local trusted approval authority and provider contract
- ADR-0011: v1 controlled-write audit (Accepted)
- CAP-0007: `revit_preview_parameter_updates`
- LIFECYCLE-0002: ephemeral write-intent store
- LIFECYCLE-0003: Revit-local trusted approval provider lifecycle
- LIFECYCLE-0004: controlled apply-attempt lifecycle
- EXEC-0001: Revit execution queue
