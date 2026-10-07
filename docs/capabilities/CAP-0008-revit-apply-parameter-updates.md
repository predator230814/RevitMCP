# CAP-0008: `revit_apply_parameter_updates`

- Status: Proposed
- Operation class: Write
- Date: 2026-10-07

## Purpose

`revit_apply_parameter_updates` is the proposed apply half of the ADR-0008 write workflow:

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

This specification is Proposed. It authorizes no production code, no Bridge method, no Server tool, and no Revit model write.

Apply is not preview and not request-review. Preview creates the intent. Request-review presents it. Apply is the only proposed path that may mutate parameter values.

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
stale
transaction_failed
committed_unverified
indeterminate
```

No other property is present. The result must not expose:

- an approval decision, including approved, rejected, dismissed, or never reviewed;
- `session_ref`;
- `intent_fingerprint` or any fingerprint;
- raw before or proposed values;
- raw Revit ids, `ElementId`, `UniqueId`, or internal-unit doubles;
- a transaction id, undo id, or verification payload.

Routing failures that happen before the addressed Addin accepts the call, including an unknown `instance_id`, stay outside this object. They follow ADR-0003 exact-instance routing. They are not rewritten as one of the statuses above, and they must not select a different Revit process.

## Status semantics

### `applied`

The Revit transaction reached finalized `TransactionStatus.Committed`, and post-write verification of every target succeeded.

This is the only success status.

### `approval_required`

No consumable trusted local approval exists for the live intent.

This status does not reveal whether the person rejected, dismissed, never reviewed, or whether a previous approval was already consumed. It starts no apply attempt and consumes nothing.

### `unavailable`

The exact intent cannot currently be applied, and no apply attempt has begun.

This covers unknown, expired, forgotten, or wrong-current-document intent state, and the equivalent bounded states in LIFECYCLE-0004, including attempt-capacity exhaustion and a single attempt that is already in progress with no stored terminal outcome yet.

`unavailable` does not reveal which of those occurred. It does not consume approval and does not create a terminal attempt.

### `stale`

An approved attempt was claimed, then failed exact stale-state revalidation.

No Revit transaction was started. The outcome is terminal for that intent. A later apply of the same `intent_ref` returns this stored outcome and must not mutate.

A current value that already equals the proposed value, because something else changed the model, is `stale`. It is not `applied`.

### `transaction_failed`

A mutation attempt began, and no successful commit occurred. The non-committed outcome is known, including finalized `TransactionStatus.RolledBack`.

The outcome is terminal. A later apply must not start another transaction.

### `committed_unverified`

Revit returned finalized `TransactionStatus.Committed`, then exact post-write verification failed or threw.

The model may have changed. The outcome is terminal. A later apply must not start another transaction and must not report `applied`.

### `indeterminate`

The final transaction outcome cannot be established safely. This includes unexpected `TransactionStatus.Pending` and any uncertain commit outcome.

`Pending` is not success. The outcome is terminal. A later apply must not start another transaction to resolve it.

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

## Stale revalidation

Reuse CAP-0007 semantics exactly. Do not invent a second comparison, tolerance, or eligibility rule.

Revalidation runs only after LIFECYCLE-0004 has claimed the attempt. It re-resolves current Revit objects in a fresh EXEC-0001 context. It does not trust wrappers stored earlier. The apply-attempt store still holds no Revit wrappers.

Immediately before any `Transaction` or `Parameter.Set`, re-resolve and re-check all of the following:

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

Any failed revalidation, including a thrown read, is `stale` for the whole batch. Roll nothing, because no transaction has started. Store the terminal outcome before returning.

## Transaction

One Revit `Transaction` covers the entire ordered batch.

- no `SubTransaction`;
- no `TransactionGroup`;
- all items commit or none do;
- one successful commit produces one Undo item;
- the transaction name is exactly `RevitMCP Apply Parameter Updates`.

EXEC-0001 remains the execution-context owner. The dispatcher does not start this transaction. Apply logic owns transaction policy and starts it only after stale revalidation succeeds.

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
- `Pending`: store `indeterminate`. Never treat `Pending` as success and never start another transaction while it is unresolved.
- Any other or unreadable status: store `indeterminate`.

## Post-commit verification

After finalized `Committed` only, re-resolve and re-read every target in a fresh resolution. Do not trust the pre-commit wrappers.

String: `Parameter.AsString()` is an ordinal exact match of the proposed string.

Integer: `Parameter.AsInteger()` is an exact match of the proposed Int32.

Quantity: `Parameter.AsDouble()` exactly equals the internal double passed to `Set` during this same execution. No tolerance. Do not publish that double.

Any verification exception or mismatch stores `committed_unverified`. The model may have changed. Do not roll back a transaction Revit has already committed. Do not retry the mutation.

Only a completed verification of every target stores `applied`.

## Retry and double commit

LIFECYCLE-0004 owns the apply-attempt store. This capability does not add apply state to `IntentEntry`, `EphemeralWriteIntentStore`, or approval-provider decision objects.

An intent causes at most one mutation attempt capable of committing.

A retry returns the stored terminal outcome when one exists, including after the source intent TTL has elapsed, and does not open a transaction.

A timeout or cancellation after the queued apply execution has begun does not abort the Revit thread. That execution finishes its controlled outcome and stores it. The caller may already have stopped waiting.

## Audit

ADR-0008 audit is mandatory for a production apply. A future accepted audit design must be able to correlate intent and fingerprint, Revit context, approval method, transaction outcome, verification outcome, and timestamps, while minimizing BIM value disclosure. ADR-0008 does not require raw before or after values in that record.

CAP-0008 does not choose the audit sink, storage, retention, or failure policy.

CAP-0008 cannot move from Proposed to Accepted for production implementation until the v1 write-audit design is accepted.

## What this capability does not do

- no Bridge protocol v10 and no Bridge method definition;
- no Server or MCP registration;
- no production `Parameter.Set`;
- no production `Transaction`, `SubTransaction`, or `TransactionGroup`;
- no audit sink;
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

A future implementation, after this specification is Accepted and after the v1 write-audit design is Accepted, must show:

1. MCP input is only exact `instance_id` and `intent_ref`, and another Revit instance is never selected.
2. The Addin request is only `intent_ref`.
3. The public result object contains only `status` and one of the seven values.
4. No consumable approval returns `approval_required` and leaves the approval unconsumed.
5. Unknown, expired, forgotten, or wrong-document intent state before a claim returns `unavailable`.
6. A claimed attempt that fails CAP-0007 revalidation returns `stale`, starts no transaction, and stays terminal.
7. A current value that equals the proposal without equaling the stored before-state returns `stale`, not `applied`.
8. One transaction named `RevitMCP Apply Parameter Updates` commits the whole batch or none of it, with one Undo item only on success.
9. Any warning or error during that transaction rolls back.
10. `RolledBack` is `transaction_failed`. `Pending` and any uncertain commit outcome are `indeterminate`. Neither status retries the mutation.
11. Finalized `Committed` plus exact verification is `applied`. A verification mismatch is `committed_unverified`.
12. A second apply returns the stored terminal outcome and does not call `Parameter.Set` again.
13. The result exposes no approval decision, `session_ref`, fingerprint, raw values, or raw Revit ids.

## Explicitly deferred

- Bridge protocol v10 and the RPC name;
- Server/MCP registration and transport error codes;
- the audit sink, retention, and failure policy;
- production apply code;
- save, sync, and worksharing checkout;
- type parameters, reference values, clear/unset, family writes, and non-parameter writes;
- autonomous approval, MRTR, MCP Apps, and external approval providers.

## References

- ADR-0008: controlled write safety model
- ADR-0009: agent and human interaction and trusted approval boundary
- ADR-0010: Revit-local trusted approval authority and provider contract
- CAP-0007: `revit_preview_parameter_updates`
- LIFECYCLE-0002: ephemeral write-intent store
- LIFECYCLE-0003: Revit-local trusted approval provider lifecycle
- LIFECYCLE-0004: controlled apply-attempt lifecycle
- EXEC-0001: Revit execution queue
