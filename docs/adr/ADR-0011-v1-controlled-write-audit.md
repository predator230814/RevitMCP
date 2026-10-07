# ADR-0011: v1 controlled-write audit

- Status: Accepted
- Date: 2026-10-07
- Product Owner acceptance: Dave, 2026-10-07

## Context

ADR-0008 requires an audit of controlled write attempts. It requires enough metadata to correlate the intent and fingerprint, Revit context, approval method, transaction outcome, verification outcome, and timestamps. It does not require raw before or after parameter values. It leaves the audit sink undecided.

CAP-0008 and LIFECYCLE-0004 were Proposed without a chosen audit sink. Both required this design before CAP-0008 could be accepted for production implementation.

This ADR chooses that v1 design. Dave, Product Owner, accepted it on 2026-10-07. Acceptance does not authorize production audit code, production apply code, a Revit `Transaction`, `Parameter.Set`, Bridge protocol v10, or a Server/MCP apply tool. CAP-0008 and LIFECYCLE-0004 remain Proposed until their conforming amendments are Accepted.

The design stays vendor-neutral, local and offline capable, independent of any MCP client, model, or provider, bounded, and fail-closed before mutation when the mandatory write-ahead record cannot be made durable. Audit cost is per approved batch. Raw BIM values stay out of the normal record. A later central or enterprise sink must be able to replace the storage without changing CAP-0008 apply semantics.

## Decision drivers

1. No Revit mutation may begin unless the mandatory write-ahead audit event for that attempt is durably persisted.
2. An audit failure after the Revit model outcome is already known must not falsify or replace that known model outcome.
3. Audit must not become a network or cloud availability dependency for v1.
4. Audit overhead is per approved batch, not per parameter.
5. No audit I/O occurs while the Revit `Transaction` is active, including while `Commit` has returned `Pending` and failure processing is not finalized.
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

ADR-0011 does not supersede ADR-0001 through ADR-0010. Those decisions remain in force. Acceptance authorizes no production implementation.

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

An implementation may keep its stream open for the process lifetime. The open stream may allow read sharing. It does not allow delete sharing. Another process still must not write to it.

Housekeeping in another Revit process must not delete a stream that its owner currently has open. If a delete fails because the file is in use, the file stays. That result means the stream is retained. It is not a reason to force-delete the file.

#### Process-local writer boundary

One process-local synchronization boundary covers, as one critical section:

- sequence allocation;
- the stream-rotation decision;
- JSON serialization;
- the append;
- `Flush(flushToDisk: true)`.

That boundary keeps sequence numbers unique inside the stream, keeps each JSON line intact, and keeps rotation from racing an append in the same process.

The same lock is not held during stale revalidation, during the Revit `Transaction`, during `Parameter.Set`, or during post-commit verification.

Each Revit process writes only its own stream file. v1 adds no cross-process audit-write lock.

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

If approval has been consumed and the attempt cannot satisfy the v1 audit contract before mutation, the future terminal outcome is `audit_failed`. That covers both of the following:

1. The mandatory `apply_started` event cannot be durably persisted.
2. Mandatory v1 audit metadata cannot be represented under the closed schema. This includes an unmapped `ApprovalSnapshot.ProviderMethod`.

For both cases:

- no Revit `Transaction` starts;
- no `Parameter.Set` occurs;
- store `audit_failed` in the LIFECYCLE-0004 apply-attempt store first;
- that intent never starts another mutation;
- no raw or unmapped provider string is written;
- any `apply_completed` record is best-effort only.

A best-effort completion line may be absent. For case 1 it may be absent because the audit sink just failed, and the sink enters the degraded state defined below. For case 2, `apply_started` is not written, because the closed schema cannot represent the metadata. Absence of the completion line does not undo the stored `audit_failed` outcome.

That terminal outcome is not in the current CAP-0008 status list. A later amendment of CAP-0008 and LIFECYCLE-0004 must add:

```text
audit_failed
```

Meaning to adopt in that amendment:

```text
audit_failed
= approval was consumed and the mandatory apply_started event could not be durably persisted
  or mandatory v1 audit metadata could not be represented under the closed schema,
  including an unmapped ApprovalSnapshot.ProviderMethod
= no Revit mutation began
= terminal
= that intent never starts another mutation
= the apply-attempt terminal state is stored before any best-effort completion line
= no raw or unmapped provider string is written
```

Pre-consumption audit and preflight failures stay outside `audit_failed`. The CAP-0008 amendment maps those to the existing `unavailable` status, and approval stays unconsumed. That amendment stays Proposed until CAP-0008 is Accepted.

#### Terminal outcome order

For every known terminal apply outcome, use this order:

1. Determine the model or apply outcome.
2. Store the immutable LIFECYCLE-0004 terminal outcome first.
3. Then attempt and flush `apply_completed`, except where this ADR says that completion write does not run.
4. Then return the CAP-0008 result.

Completion-audit durability does not precede the apply-attempt terminal state. The stored terminal outcome is what a retry reads.

If `apply_completed` persistence fails after that terminal outcome is stored:

- the already-stored terminal outcome remains authoritative;
- a retry returns that terminal outcome;
- no mutation is retried;
- the audit sink becomes degraded.

#### `apply_completed`

After the LIFECYCLE-0004 terminal outcome is stored, append one `apply_completed` line and force it durable for a finalized outcome:

- `stale`;
- `transaction_failed`;
- `committed_unverified`;
- `indeterminate`, when the transaction is not left in unresolved `Pending` failure processing;
- `applied`.

A future `audit_failed` completion, once that status exists, is best-effort only. It is not a second durability barrier.

No completion write occurs inside an active Revit `Transaction`.

#### `Pending`

Autodesk documents `TransactionStatus.Pending` as failure processing that is not yet finalized. If `Commit` returns `Pending`:

- store the LIFECYCLE-0004 terminal outcome `indeterminate` first;
- do not mutate again;
- do not perform completion-audit I/O while Revit remains in unresolved `Pending` failure processing;
- the durable `apply_started` line may remain without a matching `apply_completed` line;
- that incomplete trail is valid evidence of an unresolved attempt;
- v1 does not add a synchronous recovery mechanism or a user-dialog flow for this case.

A later design may add a safe completion hook. Until that exists, v1 does not emit `apply_completed` with `transaction_status = pending`.

Finalized `Committed`, `RolledBack`, and stale paths still attempt `apply_completed` after the terminal state is stored.

### 4. Preflight before approval consumption

Before `TryConsumeApproved`:

- verify or open the current audit stream;
- run bounded capacity and retention housekeeping when required;
- measure audit-directory usage after that housekeeping;
- require local headroom for one maximum `apply_started` line plus one maximum `apply_completed` line;
- ensure the sink is writable enough to attempt the mandatory event;
- reject `revit_version`, `revit_build`, or `addin_version` values that do not fit the bounds in decision 7. Do not truncate them.

If preflight fails:

- release the CAP-0008 pre-mutation apply record;
- do not consume approval;
- start no transaction.

The later CAP-0008 amendment maps that preflight failure to the existing `unavailable` status. Approval stays unconsumed. Preflight is not proof that the later physical write cannot fail. After approval has been consumed, a failed `apply_started` barrier or unrepresentable closed-schema metadata is `audit_failed`, not `unavailable`.

### 5. Completion-audit failure

If the model outcome is already known, the LIFECYCLE-0004 terminal outcome is already stored, and `apply_completed` cannot be persisted, do not replace or falsify that known model result.

- transaction committed and verification passed remains `applied`;
- a known rollback remains `transaction_failed`;
- a committed verification mismatch remains `committed_unverified`;
- an uncertain finalized result remains `indeterminate`;
- stale revalidation remains `stale`.

The unresolved `Pending` path does not attempt this completion write. Its stored outcome remains `indeterminate`, and its `apply_started` line may stay unmatched.

The LIFECYCLE-0004 terminal outcome stays authoritative for CAP-0008 retry. A missing completion line does not become a reason to mutate again.

Mark the process-owned audit sink degraded. While it is degraded, no new apply may consume approval until preflight and recovery succeed again.

After an already-committed Revit transaction, an audit storage failure cannot honestly be reported as "the model was not changed." A durable `apply_started` event remains evidence that the attempt existed. Absence of the matching `apply_completed` event means the audit trail is incomplete. It is not proof of rollback. v1 does not claim it can reconstruct a missing completion event after a process or OS crash.

### 6. Batch cost

Audit cost is at batch level:

- at most one durable `apply_started` append before mutation;
- at most one durable `apply_completed` append after the terminal outcome is stored, and only for a finalized outcome;
- zero completion-audit writes when `Commit` returns unresolved `Pending`;
- zero audit writes per parameter;
- zero network round trips;
- zero audit work while the Revit `Transaction` is active, and zero completion-audit work while `Pending` failure processing is unresolved.

The controlled parameter batch still uses one stale-revalidation phase, one Revit `Transaction`, one Undo item on successful commit, and one post-commit verification phase. Do not introduce one transaction or one flush per parameter.

The writer may reuse one open stream for the process lifetime so each event does not pay an open and close.

### 7. Closed event schema

Each line is one closed object. No additional properties. Every string is bounded so the maximum line size in decision 10 is enforceable. A value that does not fit its bound is not truncated and is not written.

Common fields:

```text
schema_version                         // integer 1
event_type                             // apply_started | apply_completed
event_at_utc                           // UTC, exactly yyyy-MM-ddTHH:mm:ss.fffffffZ
audit_stream_ref                       // exactly 64 lowercase hex characters
sequence                               // monotonic integer within the stream, starting at 1, at most 10 decimal digits
attempt_ref                            // exactly 64 lowercase hex characters
intent_ref_hash                        // SHA-256, domain-separated, exactly 64 lowercase hex characters
intent_fingerprint                     // LIFECYCLE-0002 SHA-256, exactly 64 lowercase hex characters
intent_fingerprint_schema_version      // integer, currently 1, at most 10 decimal digits
instance_id_hash                       // SHA-256, domain-separated, exactly 64 lowercase hex characters
document_id_hash                       // SHA-256, domain-separated, exactly 64 lowercase hex characters
revit_version                          // 1..32 UTF-8 bytes, no surrounding whitespace added
revit_build                            // 1..64 UTF-8 bytes, no surrounding whitespace added
addin_version                          // 1..32 UTF-8 bytes, no surrounding whitespace added
item_count                             // integer 1..20
```

`audit_stream_ref` and `attempt_ref` are 32 random bytes, encoded as 64 lowercase hexadecimal characters. They are minted inside the Addin.

`apply_started` also includes:

```text
approval_method                        // revit_local_in_process
approval_decided_at_utc                // same fixed UTC timestamp form as event_at_utc
approval_effective_expiry_utc          // same fixed UTC timestamp form as event_at_utc
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
  revit_local_in_process

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
  pending          // reserved; v1 does not emit this while Pending is unresolved
  unknown

verification_status
  not_run
  passed
  failed
```

`attempt_ref` is minted by the Addin for correlation inside the audit trail. It is not an authorization token and it is never MCP output.

#### Approval method

The audit enum is closed. v1 does not copy `ApprovalSnapshot.ProviderMethod` into the file, and it does not normalize an arbitrary provider string into a nearby name.

The mapping is read from the snapshot returned by a successful `TryConsumeApproved`, before `apply_started` and before any `Transaction`. The only accepted mapping is exact ordinal equality:

```text
ApprovalSnapshot.ProviderMethod == "revit-local-in-process"
-> approval_method = revit_local_in_process
```

`revit-local-in-process` is the production `ApprovalSnapshot.ProviderMethod` default. Any other provider method is unmapped.

An unmapped method uses the same future public status, `audit_failed`, defined in decision 3. No `apply_started` line is written, and the raw provider string stays out of the audit file.

`sequence` increases by one for each line in that stream. A rotated stream starts its own sequence. If the next sequence would need more than 10 decimal digits, the writer does not emit the line.

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

### 10. Retention and admission

v1 retention is 30 days maximum.

64 MiB (67,108,864 bytes) is the v1 audit-directory admission threshold. It is not an absolute global directory ceiling. Independent process-owned files have no cross-process capacity reservation, so v1 cannot guarantee that concurrent Revit processes stay at or under 64 MiB. v1 does not add a cross-process audit-write lock to create that guarantee. A future ADR would have to add cross-process capacity reservation before 64 MiB could be treated as an absolute cap.

The caller cannot configure retention or the admission threshold through MCP.

Each serialized audit event has a hard maximum UTF-8 size of 8 KiB (8,192 bytes), including the trailing newline. The field bounds in decision 7 make that maximum enforceable. A line that would exceed 8,192 bytes is not appended.

Before `TryConsumeApproved`, preflight requires local headroom for one maximum `apply_started` event plus one maximum `apply_completed` event: 16,384 bytes. Headroom is measured from the sum of file lengths in the v1 audit directory after expired-file housekeeping. If that sum plus 16,384 bytes exceeds 67,108,864 bytes, preflight fails. The later CAP-0008 mapping is `unavailable`. Approval stays unconsumed.

Concurrent Revit processes can still both pass that check and then write. The directory can overshoot 64 MiB by a small amount. The next preflight then fails closed.

During bounded housekeeping, stream files whose UTC date is older than 30 days may be deleted. Do not delete the active stream. Do not delete a stream that its owner currently has open. A delete that fails because the file is in use leaves the file in place. Do not delete an unexpired stream merely to admit a new write.

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

Acceptance does not authorize:

- production audit code;
- production apply code;
- `Parameter.Set`;
- a Revit `Transaction`, `SubTransaction`, or `TransactionGroup`;
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
- The apply-attempt terminal outcome is stored before the completion-audit write, so a later flush failure cannot rewrite the known result.

### Costs / limitations

- `audit_failed` belongs in the Proposed CAP-0008 amendment. Production apply cannot begin until that amended CAP-0008 and LIFECYCLE-0004 are Accepted.
- A crash after `apply_started` and before `apply_completed` leaves an incomplete trail. v1 does not reconstruct the missing line.
- Unresolved `Pending` may leave the same unmatched `apply_started` line. That gap is valid evidence of an unresolved attempt.
- A best-effort `apply_completed` for `audit_failed` may be absent. The stored terminal outcome still stands.
- Concurrent processes can overshoot the 64 MiB admission threshold by a small amount. The next preflight fails closed. Recent unexpired evidence is kept.
- The local file is not tamper-proof.
- `Flush(flushToDisk: true)` does not promise survival of every physical-device failure.

## Follow-up

Dave, Product Owner, accepted this ADR on 2026-10-07. The next docs checkpoint is the Proposed amendment of CAP-0008 and LIFECYCLE-0004 to this contract. That amendment includes the terminal public status `audit_failed`, the preflight mapping to `unavailable`, and the rule that the apply-attempt terminal state is stored before completion-audit durability. `audit_failed` is the only new public status that amendment adds. Those two specifications stay Proposed until they are Accepted.

Production apply implementation may begin only after all three of the following are Accepted:

1. ADR-0011;
2. the amended CAP-0008;
3. the amended LIFECYCLE-0004.

Bridge protocol v10 and the Server/MCP apply tool remain a later specification and implementation step.

## Explicitly not decided

- the enterprise sink, export format, and administrator retention controls;
- cross-process capacity reservation that would make 64 MiB an absolute directory ceiling;
- cryptographic attestation or tamper-evident storage;
- reconstruction of a missing `apply_completed` line after a crash;
- a safe completion hook for unresolved `TransactionStatus.Pending`;
- Bridge v10 and the MCP apply schema;
- production code.

## References

- ADR-0008: controlled write safety model, including decision 18
- ADR-0010: Revit-local trusted approval authority and the v1 trust boundary
- CAP-0008: `revit_apply_parameter_updates` (Proposed until its conforming amendment is Accepted)
- LIFECYCLE-0003: Revit-local trusted approval provider lifecycle
- LIFECYCLE-0004: controlled apply-attempt lifecycle (Proposed until its conforming amendment is Accepted)
- OWASP Logging Cheat Sheet: application logging, event attributes, sensitive-data exclusion, and log protection. <https://cheatsheetseries.owasp.org/cheatsheets/Logging_Cheat_Sheet.html>
- Microsoft .NET: `Environment.SpecialFolder.LocalApplicationData`. <https://learn.microsoft.com/dotnet/api/system.environment.specialfolder>
- Microsoft .NET: `FileStream.Flush(bool)` with `flushToDisk: true`. <https://learn.microsoft.com/dotnet/api/system.io.filestream.flush>
- Autodesk Revit 2026 API: `Transaction.Commit`, including `TransactionStatus.Pending`. <https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/32714010-7138-f64f-8fde-a310354448e3.htm>
- Autodesk Revit 2026 API: `TransactionStatus`. <https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/29b9a7a8-6754-8310-e063-622b569bb6d5.htm>
- Autodesk Revit 2026 API: `IFailuresPreprocessor.PreprocessFailures`. <https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/56e273aa-7d84-4a95-f06c-8a12e34e8be0.htm>
- Autodesk Revit 2026 API: `FailureProcessingResult.ProceedWithRollBack`, including the requirement to clear failures before a silent rollback. <https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/f147e6e6-4b2e-d61c-df9b-8b8e5ebe3fcb.htm>
- Autodesk Revit 2026 API: `FailuresAccessor.SetFailureHandlingOptions`, used with `ProceedWithRollBack` to dismiss failures and roll back without a user dialog. <https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/365c5204-4374-4ddc-6ccb-9d11d2926897.htm>
- Autodesk Revit API: `FailureHandlingOptions.SetClearAfterRollback`. <https://help.autodesk.com/cloudhelp/2024/ENU/Revit-API/files/Revit_API_Developers_Guide/Basic_Interaction_with_Revit_Elements/Transactions/Revit_API_Revit_API_Developers_Guide_Basic_Interaction_with_Revit_Elements_Transactions_Failure_Handling_Options_html.html>
