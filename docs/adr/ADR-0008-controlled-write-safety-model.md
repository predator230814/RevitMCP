# ADR-0008: Controlled write safety model

- Status: Accepted
- Date: 2026-09-24

## Context

RevitMCP's accepted read path is complete through CAP-0001..CAP-0006. `tools/list` exposes exactly six read-only MCP tools. Live compatibility on Revit 2025, Revit 2026.5, and Revit 2027 is recorded in `docs/CURRENT_STATE.md`. No write capability exists.

EXEC-0001 owns serialized Revit API execution and does not create a `Transaction`. Capability/application logic owns transaction policy, and that policy must be accepted before any write implementation. ADR-0006 supplies opaque `document_id` and `element_ref` for the open-document lifetime and explicitly defers persistent write-intent tokens. CAP-0004 supplies an opaque, document-scoped `parameter_ref` for a parameter definition/source binding and states that a `parameter_ref` is not an authorization token. CAP-0005 reads typed values for explicit `element_ref + parameter_ref` pairs and does not authorize writes. CAP-0004 also states that a zero read-only count does not authorize or guarantee a future write.

ADR-0007 requires future writes to have an explicit safety design covering preview, approval, stale-state revalidation, a controlled transaction, and audit. It does not define that model and does not authorize write implementation. VISION and ARCHITECTURE already require read, write, and destructive operations to stay distinguishable, and they keep transaction boundaries in the Revit execution layer.

The MCP specification `2026-07-28` introduces Multi Round-Trip Requests (MRTR). A tool can return `resultType: "input_required"` and the client retries the original call with `inputResponses`. That is a possible way for a trusted host to collect human input during a call. The current RevitMCP Server uses `ModelContextProtocol` `2.2.0`. This ADR does not migrate the protocol or the package.

This ADR records the v1 safety model only. It does not define CAP-0007, tool schemas, Bridge methods, or production code.

## Decision drivers

- A model modification requires an explicit human decision for that batch.
- Access to RevitMCP and approval to modify the model are different decisions.
- The Server cannot assume that every MCP host enforces the same confirmation behavior.
- Write semantics stay vendor-neutral and usable without one specific MCP client, MCP App, or orchestrator.
- Previewed state can become stale before apply. Apply must not silently substitute newer state.
- Revit mutation stays inside one controlled transaction owned by capability logic and dispatched through EXEC-0001.
- A retry after an uncertain response must not commit the same batch twice.
- Audit must be possible without persisting raw BIM parameter values by default.
- ADR-0001 through ADR-0007 remain in force.

## Options considered

### Option A: Client or tool-call confirmation only

The MCP host, chat UI, or tool wrapper asks the person to confirm, and RevitMCP treats a completed tool call as approved.

Advantages:

- no RevitMCP approval boundary;
- familiar in clients that already show a confirmation dialog.

Disadvantages:

- hosts differ, and some may auto-approve or skip confirmation;
- the Server cannot observe or enforce that a person approved the specific batch;
- a model can be instructed to call the tool without a person seeing the preview.

Rejected as the sole safety mechanism.

### Option B: One direct write tool with `confirm=true`

A single MCP tool both describes and applies the change. The model sets a confirmation flag or sends confirmation text.

Advantages:

- one round trip;
- simple tool surface.

Disadvantages:

- the caller that proposes the change is the same caller that asserts approval;
- confirmation text and boolean flags are model-controlled inputs;
- there is no immutable preview to revalidate;
- partial or retried calls have no separate intent identity.

Rejected.

### Option C: Preview, immutable intent, explicit approval, revalidation, and atomic apply

A read-only preview creates an opaque intent. A separate apply path runs only after a trusted approval provider attests that a person approved that exact intent. Apply revalidates the bound state, then commits at most one Revit transaction.

Advantages:

- approval is bound to one immutable batch;
- stale document or parameter state fails closed;
- transaction, verification, and retry policy have a single intent identity;
- approval providers can change without changing write semantics.

Disadvantages:

- at least two capabilities/tools;
- the Addin must retain ephemeral intent state;
- a missing approval provider means writes fail closed until one exists.

Proposed direction.

### Option D: Fully autonomous policy-based writes from the start

An administrator policy lets the agent write without a person approving each batch.

Advantages:

- fewer interruptions for repetitive edits.

Disadvantages:

- v1 would ship without a proven per-batch human gate;
- policy mistakes become model modifications;
- the model can be confused with the authority that grants the policy.

Deferred. A later ADR may design explicit administrator/user pre-authorization. ADR-0008 does not enable it.

## Decision

### 1. Every v1 write batch requires explicit human approval

Controlled writes require explicit human approval for every write batch in v1. There is no autonomous or agent-decided write mode in v1.

### 2. One approval covers one bounded immutable batch

One approval covers one batch, not each individual parameter inside that batch. The batch is bounded and immutable after the preview creates it. Any change to targets, parameter identities, proposed values, or preview content requires a new preview, a new intent, and a new approval.

### 3. Authorization and approval are separate

Authorization answers whether a caller may use RevitMCP. Approval answers whether a person has accepted one specific write batch. Access to RevitMCP does not itself approve a model modification.

This ADR does not settle the general authentication and authorization model left open in `docs/ARCHITECTURE.md`. It only decides that connection or access is not write approval.

### 4. These inputs are never approval

Never treat any of the following as approval:

- `confirm=true` or any equivalent flag sent by the model;
- arbitrary confirmation text;
- MCP tool annotations;
- model or provider identity;
- self-reported client metadata, including `clientInfo`;
- MRTR `inputResponses`, including an elicitation result with `action=accept`;
- `requestState`;
- arbitrary request fields;
- the MCP response itself.

The MCP tools specification says clients must consider tool annotations untrusted unless they come from trusted servers. Annotations remain descriptive hints. They are not a human approval of a batch.

### 5. Write workflow

```text
preview
-> immutable intent
-> explicit human approval
-> stale-state revalidation
-> one controlled Revit transaction
-> verification
-> audit
```

### 6. Preview and apply are separate capabilities

Preview and apply are separate capabilities and separate tools. This ADR does not define their complete CAP contracts, schemas, error codes, or Bridge methods.

### 7. Preview produces an opaque `intent_ref`

The preview result includes an opaque `intent_ref`. Clients pass that reference to apply. Clients must not parse it or reconstruct an intent from its text.

### 8. The Addin owns intent state

Intent state is owned by the Revit-local capability/Addin boundary:

- ephemeral;
- scoped to one Revit instance and one open-document lifetime;
- immutable after creation;
- bounded by a lifetime / expiration;
- not persisted into the Revit model;
- not parseable by clients;
- invalid after the relevant document or process lifetime ends.

This does not accept the persistent write-intent tokens deferred by ADR-0006. Close, reopen, or process exit invalidates the intent. A new `document_id` cannot reuse an older `intent_ref`.

### 9. The intent binds the batch internally

The intent internally binds:

- document identity;
- exact target identities;
- exact parameter identities and source;
- observed pre-write typed state;
- proposed typed state;
- deterministic fingerprint/version metadata needed for stale-state checking.

Preview may use Revit API objects while it executes inside a valid EXEC-0001 context.

Stored intent state must not retain live Revit API wrapper objects between calls. Persisted ephemeral intent state contains only immutable RevitMCP-owned identities, typed snapshots, fingerprints, and metadata. It does not retain live `Element`, `Parameter`, `Connector`, or similar native-wrapper objects.

Apply re-resolves the current Revit objects from those identities inside a fresh valid EXEC-0001 execution context before stale-state validation and mutation.

Autodesk documents that if the corresponding native object is destroyed, or creation of that object is undone, the managed API wrapper is no longer valid and API methods cannot be called on it. Intent state must not depend on wrapper lifetime.

Raw Revit representations used during a single EXEC-0001 execution stay inside the Addin. Storing snapshots internally does not authorize exposing `ElementId`, parameter ids, storage doubles, or other non-contract representations to clients. Agent-facing identity remains the opaque refs already accepted for reads (`document_id`, `element_ref`, `parameter_ref`) plus `intent_ref`.

`parameter_ref` remains a definition/source binding, not an authorization token, as CAP-0004 already requires.

### 10. Approval comes from a trusted approval-provider boundary

Approval is supplied through a trusted approval-provider boundary. The architecture must allow these interaction paths:

- MCP `2026-07-28` MRTR / `input_required` as a way to carry an interaction round trip;
- a future MCP App approval UI;
- a future Revit-local approval UI;
- other vendor-neutral trusted providers.

MRTR, `input_required`, and `inputResponses` are an interaction mechanism only. They are not an approval authority and they are not proof that a person approved. `inputResponses`, including an elicitation response with `action=accept`, are client-supplied input. The MRTR specification says the client gathers the requested information from the user or from other sources, then retries the original request. The tools specification does not mandate a particular UI interaction model. A human in the loop, confirmation prompts, and showing tool inputs before the call are SHOULD guidance for applications, not a protocol guarantee that a person saw or approved a batch.

Tier-1 SDK clients can satisfy `input_required` automatically through registered handlers and reissue the call. The official MCP C# SDK client resolves MRTR automatically once those handlers are registered, and it also retries `requestState`-only results without resolving an input request. A bare accepted response therefore cannot satisfy RevitMCP's v1 human-approval invariant.

`requestState` is not authority either. The MRTR specification requires servers to treat `requestState` as attacker-controlled input. If it influences authorization, resource access, or business logic, the server must protect its integrity and reject state that fails verification. If RevitMCP uses `requestState` later, that state must be integrity-protected and bound to the originating operation, the exact intent, and an expiry. That protection does not by itself prove that a person approved.

Trust in an approval provider must be established independently of self-reported `clientInfo`, model or provider identity, arbitrary request fields, and the accepted response itself. The trusted provider must produce or attest approval for this exact immutable intent. If RevitMCP cannot establish that provider trust, apply fails closed.

Human approval must be informed by the authoritative RevitMCP-generated preview/diff for the same `intent_ref` and fingerprint. A model-generated prose summary may accompany that preview. It is not the authoritative approval content. The approval attestation must be bound to that exact intent and fingerprint. The precise UI and schema remain later capability and provider work. The architecture only requires that approval be informed by, and bound to, the exact server-generated preview.

Core write semantics stay independent of any one MCP client. An external orchestrator may coordinate approval later, as ADR-0007 allows, and still must not own the Revit transaction or Revit capability semantics. RevitMCP must remain usable for its read tools without an orchestrator.

If a compatible trusted approval mechanism is unavailable, or provider trust cannot be established, write execution fails closed. No trusted provider means no apply.

### 11. MRTR is evidence, not implementation authorization

MCP `2026-07-28` MRTR is relevant architecture evidence, not an implementation authorization and not an approval authority. The current `ModelContextProtocol` `2.2.0` package and any protocol migration must be evaluated separately before implementation. This ADR does not upgrade the package and does not implement MRTR. Decision 10 still applies after any future migration: an `inputResponses` acceptance is client input, and apply still fails closed unless independent provider trust and an intent-bound human approval both exist.

### 12. Apply revalidates immediately before mutation

Immediately before mutation, apply must revalidate:

- the same intended Revit instance and document context;
- target existence;
- parameter identity and source;
- supported type;
- write eligibility;
- observed pre-write state.

Any material mismatch invalidates the intent as stale. Apply must not silently re-preview or substitute newer state. A stale intent cannot be approved again in place; the caller previews a new batch.

`Document.IsModifiable` is a dynamic execution-state signal. Autodesk documents that a model can be modified only inside an open transaction, and that the document can still be non-modifiable during regeneration, failure processing, or some events. It is not a standing write permission and it is not approval.

### 13. v1 batches are atomic

v1 write batches are atomic:

- all changes commit or none do;
- one batch uses one Revit `Transaction`;
- one successful batch produces one Revit Undo item, which is the undo item Autodesk creates for that committed transaction;
- no hidden partial-success semantics;
- EXEC-0001 remains the execution-context owner;
- capability/application logic owns transaction policy.

The dispatcher still must not start a transaction around unrelated work. Read capabilities continue to start no transaction.

EXEC-0001 already requires that a timeout after Revit execution has begun must not abort the Revit thread. A write attempt that has entered `Execute` is allowed to finish its transaction outcome. The caller may already have received a timeout. Decision 16 covers that case.

### 14. `Transaction.Commit` status is explicit

Both `Transaction.Commit()` and `Transaction.Commit(FailureHandlingOptions)` return `TransactionStatus`. Autodesk requires callers to test that status. Only a finalized `Committed` status is committed success. `RolledBack` is failure, including when failure handling rolls the transaction back. `Pending` means failure handling is not finalized and Revit is waiting; it is not final success and must not be reported as committed. Until commit is finalized, Revit also rejects further document changes, including a new transaction.

A later capability specification must define how a non-final `Pending` outcome is surfaced. It must not map `Pending` onto a normal successful write result.

### 15. Post-write verification is required

After a finalized `Committed` status, apply re-reads the affected parameter state and compares it to the intended result before returning a normal successful write result. A commit status of `Committed` with a verification mismatch is not a normal success.

### 16. An intent can attempt mutation only once

An intent may cause at most one mutation attempt capable of committing. The Addin preserves a bounded terminal outcome for that intent so a retry after an uncertain response, including a caller timeout after execution started, returns or rejects from that outcome and does not perform the same write twice.

The intent is consumed when the mutation attempt begins, before `Commit`. A second apply of the same `intent_ref` does not start another transaction. Process exit drops ephemeral intent state, so a restarted Revit instance cannot replay the old reference.

### 17. Worksharing checkout status is advisory

`WorksharingUtils.GetCheckoutStatus` may be shown in preview. Autodesk documents it as a locally cached value that can be stale relative to the central model, suitable for interactive display, and not a reliable indication that the element can be edited immediately. It is not authoritative proof that the write will succeed. Actual transaction and write failures remain authoritative. This ADR does not authorize automatic worksharing checkout.

### 18. Audit is required

Audit is required for write attempts. Record enough metadata to correlate:

- intent/fingerprint;
- Revit context;
- approval method;
- transaction outcome;
- verification outcome;
- timestamps.

The default audit design minimizes BIM value disclosure. This ADR does not require raw before/after parameter values to be persisted in audit logs.

### 19. No autonomous write mode in v1

v1 has no autonomous or agent-decided write mode.

Future policy-based pre-authorization may be designed in a later ADR, but:

- it is not enabled by ADR-0008;
- it must be explicit administrator/user policy;
- it must remain bounded by capability, target, and operation constraints;
- the model itself never grants its own authority.

### 20. This ADR does not authorize implementation

ADR-0008 does not authorize:

- any write production code;
- creation or deletion of elements;
- geometry changes;
- `ElementId` or reference-parameter writes;
- type-parameter writes;
- parameter clear/unset;
- worksharing checkout automation;
- save or synchronize;
- APS or other cloud writes;
- an orchestrator;
- MCP Apps implementation;
- Revit product UI implementation;
- an MCP SDK upgrade.

CAP-0007 is not created by this ADR and is not accepted.

### 21. Prior ADR boundaries stay in force

ADR-0008 preserves ADR-0001 through ADR-0007, including:

- out-of-process Server and in-process Addin;
- Named Pipe JSON-RPC as the local bridge;
- opaque `instance_id` discovery and explicit multi-instance routing;
- the Contracts / Bridge / Server / multi-version Addin structure;
- bounded agent context;
- opaque open-document `document_id` and `element_ref`;
- RevitMCP as a specialized local Revit capability service, with cloud Autodesk capability and orchestration kept outside the Revit transaction boundary.

## Consequences

### Positive

- Write safety has one reviewable model before any CAP or code.
- Human approval is bound to an immutable batch rather than to a model-controlled flag.
- Stale previews fail closed instead of writing unexpected current values.
- Transaction ownership stays where EXEC-0001 already placed it: capability logic, on the EXEC-0001 context.
- Approval UI and MRTR can be added later without redefining what a write means.
- Audit can correlate outcomes without requiring a BIM value log.

### Costs / limitations

- Writes cannot ship until a trusted approval provider exists; otherwise they fail closed.
- Preview and apply are two steps, and intents expire.
- `Pending` commit handling and the concrete audit sink remain for a later specification.
- The general authentication model for connecting to RevitMCP remains open.
- Policy-based unattended writes remain a separate future ADR.

## Explicitly not decided

- CAP-0007 or any later write capability contract;
- batch size limits, typed value grammar, and error-code lists;
- which trusted approval provider ships first;
- MCP `2026-07-28` migration and the `ModelContextProtocol` package upgrade;
- where audit records are stored;
- how a non-final `Pending` Revit failure is presented to a person;
- worksharing relinquish, borrow, or checkout behavior;
- save, synchronize, element creation, element deletion, and geometry edits.

## References

- ADR-0001: out-of-process MCP server and Revit capability host
- ADR-0005: agent context and token efficiency
- ADR-0006: document and element reference identity
- ADR-0007: federated MCP boundaries and optional orchestration
- EXEC-0001: Revit execution queue and transaction ownership
- CAP-0004: `revit_describe_parameters`
- CAP-0005: `revit_get_parameter_values`
- MCP specification `2026-07-28`: https://modelcontextprotocol.io/specification/2026-07-28
- MCP `2026-07-28` release, including MRTR / `input_required`: https://blog.modelcontextprotocol.io/posts/2026-07-28/
- MCP tools. The protocol does not mandate a UI interaction model. Human-in-the-loop confirmation is SHOULD guidance. Tool annotations are untrusted unless they come from trusted servers: https://modelcontextprotocol.io/specification/2026-07-28/server/tools
- MCP `2026-07-28` Multi Round-Trip Requests. `inputResponses` are client-supplied results, including `action=accept`. `requestState` is attacker-controlled input and requires integrity protection when it influences authorization or business logic: https://modelcontextprotocol.io/specification/2026-07-28/basic/patterns/mrtr
- Official MCP C# SDK MRTR guidance. Registered handlers can satisfy `input_required` and the client retries automatically, including `requestState`-only results: https://csharp.sdk.modelcontextprotocol.io/v2/concepts/mrtr/mrtr.html
- MCP C# SDK 2.0 announcement. The high-level client resolves MRTR automatically from registered handlers: https://devblogs.microsoft.com/dotnet/announcing-v20-of-the-official-mcp-csharp-sdk/
- Autodesk Revit 2026 `IsValidObject` remarks. A managed wrapper is no longer valid when the corresponding native object is destroyed or its creation is undone: https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/d1cfc136-56e5-614b-8d23-6b5ef2c7c874.htm
- Autodesk Revit 2026 `Transaction.Commit`: https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/32714010-7138-f64f-8fde-a310354448e3.htm
- Autodesk Revit 2026 `Transaction.Commit(FailureHandlingOptions)`: https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/9e9983d1-bd0d-b476-2dc4-021c56eb2bd7.htm
- Autodesk Revit 2026 `Document.IsModifiable`: https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/af884262-3ba2-b0a0-d7ef-f0a49c1bf1bc.htm
- Autodesk Revit 2026 `WorksharingUtils.GetCheckoutStatus`: https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/8359bb9a-01d2-d595-c72b-718c70841511.htm
