# ADR-0011: v1 controlled-write audit

- Status: Proposed
- Date: 2026-10-07

## Context

ADR-0008 requires an audit of controlled write attempts. It requires enough metadata to correlate the intent and fingerprint, Revit context, approval method, transaction outcome, verification outcome, and timestamps. It does not require raw before or after parameter values. It leaves the audit sink undecided.

CAP-0008 and LIFECYCLE-0004 are Proposed. Both say CAP-0008 cannot be accepted for production implementation until the v1 write-audit design is accepted. Neither chooses storage, retention, or failure behavior.

This ADR chooses that v1 design. It is Proposed. It does not amend CAP-0008 or LIFECYCLE-0004. It does not authorize production apply code, a Revit `Transaction`, `Parameter.Set`, Bridge protocol v10, or a Server/MCP apply tool.

The design stays vendor-neutral, local and offline capable, independent of any MCP client, model, or provider, bounded, and fail-closed before mutation when the mandatory write-ahead record cannot be made durable. Audit cost is per approved batch. Raw BIM values stay out of the normal record. A later central or enterprise sink must be able to replace the storage without changing CAP-0008 apply semantics.

## Decision drivers

1. No Revit mutation may begin unless the mandatory write-ahead audit event for that attempt is durably persisted.
2. An audit failure after the Revit model outcome is already known must not falsify or replace that known model outcome.
3. Audit must not become a network or cloud availability dependency for v1.
4. Audit overhead is per approved batch, not per parameter.
5. No audit I/O occurs while the Revit `Transaction` is active.
6. Raw BIM values, prompts, document paths, user identity, and model prose are not required for normal audit correlation.
7. Multiple Revit processes must not contend for one audit file.
8. The v1 audit is an operational durable trail. It is not a claim of cryptographic non-repudiation against a compromised OS, user account, or Revit process.

## Options considered

### A. No persistent audit

Rejected. ADR-0008 requires an audit of write attempts. An in-memory note disappears on process exit and cannot correlate a committed change after a crash.

### B. Windows Event Log

Not selected for v1. It is an OS-specific administration and deployment surface. It is a poor fit for a portable RevitMCP-owned structured record that must stay local, bounded, and independent of one vendor's log channel.

### C. SQLite or another local database

Viable later. A database engine, schema, and migration story are unnecessary for the bounded v1 stream of two events per batch.

### D. Local Addin-owned JSON Lines

Selected for v1. One process-owned UTF-8 JSON Lines stream can be appended with the platform BCL, flushed to disk, rotated by date, and retained under a fixed size and age. It adds no package and no network call.

### E. Remote, cloud, SIEM, or OpenTelemetry sink

Useful later as an optional integration. It is not a mandatory v1 dependency. A future sink must not move Revit transaction ownership outside the Addin, and CAP-0008 semantics must not depend on one telemetry vendor.

## Decision

ADR-0011 does not supersede ADR-0001 through ADR-0010. Those decisions remain in force. This proposal authorizes no production change.

### 1. Ownership

One process-owned audit writer lives inside `RevitMCP.Addin`.

It is not owned by:

- `RevitMCP.Server`;
- the Bridge;
- the MCP client;
- an LLM, model, or provider;
- an MCP App;
- an orchestrator;
- the approval WebView page.

The writer stores no Revit API wrapper.

The writer is separate from `EphemeralWriteIntentStore`, the approval provider, and the LIFECYCLE-0004 apply-attempt store. Those components may supply bounded immutable metadata to the writer. Audit state is not added to `IntentEntry`.

### 2. Storage

The root is under `Environment.SpecialFolder.LocalApplicationData`.

Conceptual directory:

```text
RevitMCP/Audit/v1/
```

Do not hard-code a drive, a user-profile path, or a username.

Each line is one closed UTF-8 JSON object. The encoding is JSON Lines. No extra NuGet package is introduced. Use `System.Text.Json` and the platform BCL.

Each RevitMCP Addin process writes only its own stream. The process generates one high-entropy `audit_stream_ref` for its lifetime inside the Addin. Process id alone is not the identity.

Conceptual filename:

```text
write-YYYYMMDD-<audit_stream_ref>.jsonl
```

`YYYYMMDD` is the UTC date of that stream. Creation uses `FileMode.CreateNew`, or the equivalent fail-closed create that refuses an existing file.

A process may rotate to a new stream when the UTC date changes. It then stops appending to the previous file. Other RevitMCP processes never append to the same stream file. No cross-process writer lock is required.

An implementation may keep its stream open for the process lifetime and allow read sharing. Another process still must not write to it.

### 3. Two batch events

v1 has exactly two logical event types:

```text
apply_started
apply_completed
```

There is no event per parameter.

#### `apply_started`

This event is written only after all of the following are true:

- one exclusive CAP-0008 apply record exists;
- audit-sink preflight has succeeded;
- `TryConsumeApproved` has succeeded.

It is written before stale revalidation is allowed to open a Revit `Transaction`, before `Transaction.Start`, and before `Parameter.Set`.

Append one `apply_started` line and force it durable before mutation is permitted. The implementation direction is `FileStream.Flush(flushToDisk: true)`, or the equivalent strongest BCL disk-flush primitive on the supported target framework. This does not defeat physical-device failure or a compromised OS.

If `apply_started` cannot be durably persisted:

- no Revit `Transaction` starts;
- no `Parameter.Set` occurs;
- the already-consumed approval cannot be reused;
- the apply attempt becomes terminal;
- the audit sink enters the degraded state defined below.

That terminal outcome is not in the current CAP-0008 status list. A later amendment of CAP-0008 and LIFECYCLE-0004 must add:

```text
audit_failed
```

Meaning to adopt in that amendment:

```text
audit_failed
= approval was consumed and the mandatory durable write-ahead audit barrier failed
= no Revit mutation began
= terminal
= that intent never starts another mutation
```

This ADR does not make that amendment.

#### `apply_completed`

After the CAP-0008 terminal model outcome is known, append one `apply_completed` line and force it durable. The known outcomes are:

- `stale`;
- `transaction_failed`;
- `committed_unverified`;
- `indeterminate`;
- `applied`;
- a future audit-related terminal outcome, including `audit_failed`, when a later accepted amendment defines it.

No completion write occurs inside an active Revit `Transaction`.

### 4. Preflight before approval consumption

Before `TryConsumeApproved`:

- verify or open the current audit stream;
- run bounded capacity and retention housekeeping when required;
- ensure the sink is writable enough to attempt the mandatory event.

If preflight fails:

- release the CAP-0008 pre-mutation apply record;
- do not consume approval;
- start no transaction.

The later CAP-0008 amendment maps that preflight failure to the existing `unavailable` status. Preflight is not proof that the later physical write cannot fail. A failure of the post-consumption write-ahead barrier is the `audit_failed` case, not `unavailable`.

### 5. Completion-audit failure

If the model outcome is already known and `apply_completed` cannot be persisted, do not replace or falsify that known model result.

- transaction committed and verification passed remains `applied`;
- a known rollback remains `transaction_failed`;
- a committed verification mismatch remains `committed_unverified`;
- a known `Pending` or otherwise uncertain result remains `indeterminate`;
- stale revalidation remains `stale`.

The LIFECYCLE-0004 terminal outcome stays authoritative for CAP-0008 retry. A missing completion line does not become a reason to mutate again.

Mark the process-owned audit sink degraded. While it is degraded, no new apply may consume approval until preflight and recovery succeed again.

After an already-committed Revit transaction, an audit storage failure cannot honestly be reported as "the model was not changed." A durable `apply_started` event remains evidence that the attempt existed. Absence of the matching `apply_completed` event means the audit trail is incomplete. It is not proof of rollback. v1 does not claim it can reconstruct a missing completion event after a process or OS crash.

### 6. Batch cost

Audit cost is at batch level:

- at most one durable `apply_started` append before mutation;
- at most one durable `apply_completed` append after the terminal outcome;
- zero audit writes per parameter;
- zero network round trips;
- zero audit work while the Revit `Transaction` is active.

The controlled parameter batch still uses one stale-revalidation phase, one Revit `Transaction`, one Undo item on successful commit, and one post-commit verification phase. Do not introduce one transaction or one flush per parameter.

The writer may reuse one open stream for the process lifetime so each event does not pay an open and close.

### 7. Closed event schema

Each line is one closed object. No additional properties.

Common fields:

```text
schema_version                         // integer 1
event_type                             // apply_started | apply_completed
event_at_utc                           // ISO-8601 with a Z offset
audit_stream_ref                       // this process stream's opaque ref
sequence                               // monotonic integer within the stream, starting at 1
attempt_ref                            // internal high-entropy correlation value
intent_ref_hash                        // SHA-256, domain-separated, lowercase hex
intent_fingerprint                     // existing LIFECYCLE-0002 fingerprint
intent_fingerprint_schema_version      // integer, currently 1
instance_id_hash                       // SHA-256, domain-separated, lowercase hex
document_id_hash                       // SHA-256, domain-separated, lowercase hex
revit_version
revit_build
addin_version
item_count                             // integer count of items in the batch
```

`apply_started` also includes:

```text
approval_method                        // revit_local
approval_decided_at_utc
approval_effective_expiry_utc
```

`apply_completed` also includes:

```text
apply_status
transaction_status
verification_status
```

Closed values:

```text
approval_method
  revit_local

apply_status
  stale
  transaction_failed
  committed_unverified
  indeterminate
  applied
  audit_failed

transaction_status
  none
  committed
  rolled_back
  pending
  unknown

verification_status
  not_run
  passed
  failed
```

`attempt_ref` is minted by the Addin for correlation inside the audit trail. It is not an authorization token and it is never MCP output.

`sequence` increases by one for each line in that stream. A rotated stream starts its own sequence.

Do not include arbitrary exception text, failure-message text, or stack traces.

`audit_failed` is reserved in this schema for the later CAP-0008 amendment. This ADR does not add it to the current public apply result.

### 8. Hashed identifiers

Do not persist raw `intent_ref`, `instance_id`, or `document_id`.

Hash with SHA-256 and field-specific domain separation. The input is UTF-8 with no normalization and no trimming. The stored form is lowercase hexadecimal.

```text
SHA256(UTF8("revitmcp-audit-v1:intent_ref:" + rawIntentRef))
SHA256(UTF8("revitmcp-audit-v1:instance_id:" + rawInstanceId))
SHA256(UTF8("revitmcp-audit-v1:document_id:" + rawDocumentId))
```

Use the BCL SHA-256 implementation. Do not add a package. Do not use a reversible encoding. Do not treat the hash as encryption.

`intent_fingerprint` may be stored as the existing cryptographic fingerprint. LIFECYCLE-0002 already defines it as the semantic correlation digest. It is not a raw `intent_ref`.

### 9. Excluded data

The v1 write audit never persists:

- before values;
- proposed values;
- internal Revit doubles;
- element names, parameter names, or category names;
- raw `element_ref` or `parameter_ref`;
- raw `intent_ref`, `instance_id`, or `document_id`;
- `session_ref`;
- document title;
- Revit file path;
- usernames or machine names;
- prompts;
- LLM or model prose;
- MCP conversation content;
- access tokens or credentials;
- arbitrary Revit failure-message text;
- stack traces.

Failure handling is represented only by the bounded status fields above. Do not log `FailureDefinition` message text. Do not add per-parameter counts in v1.

### 10. Retention and capacity

v1 retention is 30 days maximum. The hard total size of the audit directory is 64 MiB.

The caller cannot configure either limit through MCP.

During bounded housekeeping, stream files whose UTC date is older than 30 days may be deleted. Do not delete the active stream. Do not delete an unexpired stream merely to admit a new write.

If the hard capacity is still exceeded after expired files are deleted, audit preflight fails. A new apply returns the future mapping `unavailable`. Approval stays unconsumed.

This prefers keeping recent write evidence over discarding it to make room.

Housekeeping runs at process start, stream rotation, and apply preflight. Do not rescan the directory once per parameter.

### 11. Trust

The directory is per-user local application data. v1 does not claim the trail is tamper-proof.

v1 does not defend the audit trail against a compromised OS, a compromised user account, malicious in-process Revit code, or tampered RevitMCP binaries. That is the same v1 trust boundary ADR-0010 already records.

Do not add home-grown encryption, home-grown signing, a hash chain presented as non-repudiation, or cloud attestation.

A later ADR may add an authenticated enterprise sink, tamper-resistant storage, SIEM or OpenTelemetry export, a different retention policy, or administrator controls. Core CAP-0008 semantics must not depend on one telemetry vendor.

### 12. Failure isolation

Audit failure does not break the existing read tools, CAP-0007 preview, or request-review. It only gates production apply.

If the audit directory is unavailable at Addin startup, do not fail Addin startup. Reads, preview, and request-review stay available. Apply stays unavailable.

### 13. Lifecycle

The writer may be created lazily or at startup. Either way, a creation failure stays isolated from reads.

Shutdown stops new audit and apply work, flushes buffers that were already written when that flush is still possible, and disposes the writer. Shutdown does not invent a model outcome.

Document close does not delete audit records. Intent, approval, and apply-attempt state remain ephemeral and document-scoped. The audit trail is historical. Retention, not document lifetime, removes audit files.

### 14. JSON Lines is the v1 sink, not the permanent architecture

JSON Lines is selected because the v1 stream is two local records per batch. It is not the universal audit architecture.

Later sinks may include an enterprise collector, an OpenTelemetry-compatible export, a SIEM, or an organization retention store. They stay outside Revit transaction ownership. Ordinary local controlled writes do not require a cloud service.

Do not couple core apply semantics to OpenAI, Anthropic, Autodesk cloud, Azure, or any specific telemetry vendor.

### 15. This ADR does not authorize implementation

This proposal does not authorize:

- production audit code;
- production apply code;
- `Parameter.Set`;
- a Revit `Transaction`, `SubTransaction`, or `TransactionGroup`;
- an amendment of CAP-0008 or LIFECYCLE-0004 in the same change;
- Bridge protocol v10;
- a Server or MCP apply tool;
- a package for JSON, hashing, or logging;
- encryption, signing, or a remote sink.

## Consequences

### Positive

- ADR-0008's open audit-sink question has one reviewable v1 answer.
- A write cannot start unless the write-ahead line was forced durable.
- A later disk failure cannot be used to deny a commit Revit already finalized.
- Local use does not depend on a network or a telemetry vendor.
- The record can correlate a batch without storing BIM values or raw opaque refs.
- Each Revit process owns its file, so writers do not share one append stream.

### Costs / limitations

- `audit_failed` is not yet a CAP-0008 status. Production apply still cannot be accepted until that amendment exists.
- A crash after `apply_started` and before `apply_completed` leaves an incomplete trail. v1 does not reconstruct the missing line.
- A full unexpired directory blocks new applies instead of deleting recent evidence.
- The local file is not tamper-proof.
- `Flush(flushToDisk: true)` does not promise survival of every physical-device failure.

## Follow-up

If this ADR is Accepted, the next docs checkpoint is an amendment of CAP-0008 and LIFECYCLE-0004 to the accepted audit contract, including the terminal public status `audit_failed` and the preflight mapping to `unavailable`.

Production apply implementation may begin only after all three of the following are Accepted:

1. ADR-0011;
2. the amended CAP-0008;
3. the amended LIFECYCLE-0004.

Bridge protocol v10 and the Server/MCP apply tool remain a later specification and implementation step.

## Explicitly not decided

- the enterprise sink, export format, and administrator retention controls;
- cryptographic attestation or tamper-evident storage;
- reconstruction of a missing `apply_completed` line after a crash;
- Bridge v10 and the MCP apply schema;
- production code.

## References

- ADR-0008: controlled write safety model, including decision 18
- ADR-0010: Revit-local trusted approval authority and the v1 trust boundary
- CAP-0008: `revit_apply_parameter_updates` (Proposed; unchanged by this ADR)
- LIFECYCLE-0003: Revit-local trusted approval provider lifecycle
- LIFECYCLE-0004: controlled apply-attempt lifecycle (Proposed; unchanged by this ADR)
- OWASP Logging Cheat Sheet: application logging, event attributes, sensitive-data exclusion, and log protection. <https://cheatsheetseries.owasp.org/cheatsheets/Logging_Cheat_Sheet.html>
- Microsoft .NET: `Environment.SpecialFolder.LocalApplicationData`. <https://learn.microsoft.com/dotnet/api/system.environment.specialfolder>
- Microsoft .NET: `FileStream.Flush(bool)` with `flushToDisk: true`. <https://learn.microsoft.com/dotnet/api/system.io.filestream.flush>
- Autodesk Revit 2026 API: `Transaction.Commit`, including `TransactionStatus.Pending`. <https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/32714010-7138-f64f-8fde-a310354448e3.htm>
- Autodesk Revit 2026 API: `TransactionStatus`. <https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/29b9a7a8-6754-8310-e063-622b569bb6d5.htm>
- Autodesk Revit 2026 API: `IFailuresPreprocessor.PreprocessFailures`. <https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/56e273aa-7d84-4a95-f06c-8a12e34e8be0.htm>
- Autodesk Revit 2026 API: `FailureProcessingResult.ProceedWithRollBack`, including the requirement to clear failures before a silent rollback. <https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/f147e6e6-4b2e-d61c-df9b-8b8e5ebe3fcb.htm>
- Autodesk Revit 2026 API: `FailuresAccessor.SetFailureHandlingOptions`, used with `ProceedWithRollBack` to dismiss failures and roll back without a user dialog. <https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/365c5204-4374-4ddc-6ccb-9d11d2926897.htm>
- Autodesk Revit API: `FailureHandlingOptions.SetClearAfterRollback`. <https://help.autodesk.com/cloudhelp/2024/ENU/Revit-API/files/Revit_API_Developers_Guide/Basic_Interaction_with_Revit_Elements/Transactions/Revit_API_Revit_API_Developers_Guide_Basic_Interaction_with_Revit_Elements_Transactions_Failure_Handling_Options_html.html>
