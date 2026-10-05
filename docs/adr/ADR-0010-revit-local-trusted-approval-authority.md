# ADR-0010: Revit-local trusted approval authority and provider contract

- Status: Proposed
- Date: 2026-10-05

## Context

ADR-0008 requires a trusted approval-provider boundary before any v1 write. Approval is a decision that a person accepted one exact immutable batch. Access to RevitMCP, a completed tool call, and a rendered screen are different facts.

ADR-0009 keeps one Revit-local capability core and separates human-interaction UI from approval authority. An MCP App may later present an approval experience. That presentation is not the authority. Persistent writes stay on the ADR-0008 path. Orchestration stays external and optional under ADR-0007.

CAP-0007 creates an immutable Addin-owned ephemeral intent. The current implementation matches LIFECYCLE-0002:

- `EphemeralWriteIntentStore` is the one in-process store;
- `IntentDraft` is the creation snapshot;
- `IntentCanonicalEncoder` produces the schema-version 1 canonical bytes and SHA-256 `intent_fingerprint`;
- `IntentEntry` stores `intent_ref`, fingerprint, schema version, creation and expiry timestamps, `instance_id`, `document_id`, and the ordered preview items.

`IntentEntry` has no approval decision, transaction state, or consumed state. `IntentStoreCreateStatus.Rejected` means the snapshot was not stored. It is not a human rejection.

LIFECYCLE-0002 explicitly keeps approval state out of that immutable intent store. It forbids approval-state, apply-state, and transaction-state operations on the store.

CAP-0008/apply does not exist. No production Revit write exists. No trusted approval provider exists.

PR #60, merged to main, proved an isolated feasibility spike for this topology on Revit 2025.5 (`25.5.0.57`), Revit 2026.5 (`26.5.0.55`), and Revit 2027:

```text
Revit DockablePane
-> WPF host
-> local WebView2 content
```

The recorded evidence is in `spikes/RevitMCP.WebView2Spike/README.md`. Revit 2025.5 rendered local content, completed hostReady and ping/pong, hid and showed the pane, and shut down and relaunched cleanly, while the loaded managed WebView2 assemblies remained Autodesk Revit 2025 `1.0.2045.28` and the Evergreen runtime stayed separate (`154.0.4258.53`). Revit 2026.5 and Revit 2027 passed local content, messaging, and lifecycle with Autodesk-hosted managed WebView2 `1.0.2478.35`. That spike is feasibility evidence. It does not accept a production UI architecture, and this ADR does not copy spike code into production.

This ADR decides the trusted approval boundary before CAP-0008 is designed. It does not implement that boundary.

## Decision drivers

- Preserve ADR-0008: the model, the MCP client, and the Server cannot approve a write.
- Preserve LIFECYCLE-0002: the stored intent stays immutable.
- Bind every approval to the exact Addin-owned intent, not to caller-supplied preview text or a caller-supplied fingerprint.
- Fail closed when trust, identity, freshness, or the human decision is missing.
- Show the person the RevitMCP-generated preview for that stored intent.
- Keep a future Revit-local presentation surface inside the Addin process without making the surface the authority.
- Keep human waiting outside any Revit transaction and outside an active EXEC-0001 mutation.
- Leave room for a later companion or enterprise provider without selecting its attestation technology.
- Leave MRTR and MCP Apps as interaction mechanisms.
- Authorize no production code from this proposal.

## Options considered

### Option A: Revit-local, in-process approval authority in RevitMCP.Addin

The trusted approval authority for v1 lives inside the RevitMCP.Addin process. It resolves the stored intent, presents the authoritative preview through a Revit-local surface, and records the person's decision in provider-owned ephemeral state.

Advantages:

- the authority shares the process that owns the intent store and the future mutation;
- a client or model cannot mint the decision by sending another field;
- same-process trust does not need a cryptographic approval token;
- the merged spike shows that a local DockablePane presentation is feasible.

Disadvantages:

- v1 writes require a person at the Revit session;
- headless or unattended apply stays impossible in v1;
- the provider needs its own lifecycle beside the immutable intent store.

Proposed direction for v1.

### Option B: Separate local companion-process approval provider

A process beside Revit, still on the same machine, owns the approval decision and attests it back to the Addin.

Advantages:

- the approval UI can restart or update without unloading the Addin;
- a later desktop companion can reuse the same trust boundary.

Disadvantages:

- approval crosses a process boundary, so v1 would need integrity protection and replay protection before any write;
- the companion can drift from the Addin-owned intent lifetime;
- it adds a second local component before the in-process boundary is proven.

Remains a future provider possibility. Not selected for v1.

### Option C: Remote or enterprise approval provider

An authenticated service outside the Revit machine approves the batch.

Advantages:

- supports a later named human identity, enterprise policy, and remote reviewers;
- matches deployment cases where the person is not at the Revit workstation.

Disadvantages:

- requires authenticated provider identity, integrity-protected attestation, expiry, and replay protection;
- network absence would block writes;
- choosing a token, certificate, or signature format now would freeze an unsettled security design.

Remains a future provider possibility. Not selected for v1.

### Option D: MCP client, MRTR, or MCP App as the approval authority

The client, an MRTR `inputResponses` acceptance, `requestState`, `clientInfo`, or an MCP App would be treated as proof that a person approved the batch.

Advantages:

- no Revit-local approval surface to build;
- hosts that already show a confirmation dialog would appear sufficient.

Disadvantages:

- ADR-0008 already rejects those inputs as approval;
- hosts differ, and a client can satisfy MRTR without a person seeing the RevitMCP preview;
- an MCP App can render or invoke UI without RevitMCP observing the decision;
- the caller that proposes the change would remain able to assert approval.

Rejected as an approval authority.

## Decision

ADR-0010 does not supersede ADR-0001 through ADR-0009. Those decisions remain in force. This record is Proposed. Nothing in it is accepted implementation authority.

### 1. v1 approval authority is Revit-local and in-process

The trusted approval authority for v1 lives inside the RevitMCP.Addin process boundary.

These are not approval authorities:

- the MCP client;
- RevitMCP.Server;
- the model;
- an MCP App;
- an orchestrator;
- an MRTR response;
- `clientInfo`;
- arbitrary request fields.

This preserves ADR-0008. If that in-process provider is unavailable, apply fails closed.

In-process ownership is a trust decision. It is not isolation from other code already loaded into Revit. The v1 threat scope is:

- v1 protects the write decision against untrusted MCP, model, client, Server, MCP App, orchestrator, and request inputs;
- the trusted computing base includes the Revit process, the RevitMCP.Addin binaries, and the bundled local approval UI assets used by the provider;
- v1 does not claim protection against a compromised Revit process, malicious or injected in-process code, tampered RevitMCP binaries or UI assets, or a compromised OS or user session;
- stronger isolation against those threats is future external-provider and platform-security work.

### 2. Intent state remains immutable

LIFECYCLE-0002 semantics stay unchanged.

`EphemeralWriteIntentStore` remains the immutable intent store. This ADR does not add any of the following to `IntentEntry`:

- `Approved`;
- `Rejected`;
- transaction state;
- consumed state;
- any other mutable approval or apply field.

Approval state is separate provider-owned ephemeral state. The store's existing create status `Rejected` remains a failed insertion. It is not the human decision `rejected`.

### 3. Approval is bound to the exact stored intent

An approval decision is internally bound to at least:

- `intent_ref`;
- `intent_fingerprint`;
- `instance_id`;
- `document_id`;
- decision timestamp;
- provider/method identity;
- effective expiry.

The provider resolves the authoritative intent from the Addin-owned store. Caller-supplied preview content is not authority. A caller-supplied fingerprint is not authority. The provider compares the stored fingerprint with the decision it recorded.

The effective approval lifetime never exceeds the underlying intent lifetime. Successful document close, intent expiry, provider shutdown, or Revit process shutdown invalidates the approval.

### 4. Provider decision model

The provider decision model conceptually allows:

- pending;
- approved;
- rejected.

These words are architectural states. This ADR does not require an implementation enum.

A rejection is terminal for that intent in v1. A later operation the person wants is a new preview and a new intent.

Approval becomes unusable once a mutation attempt for that intent begins. That matches ADR-0008's rule that an intent may cause at most one mutation attempt. The exact apply-state machine remains CAP-0008 work. It is not stored on `IntentEntry`.

### 5. Fail closed

Apply and approval fail closed in these cases:

- no trusted provider available: no apply;
- unknown intent: no approval;
- expired intent: no approval;
- fingerprint mismatch: no approval;
- wrong instance or document: no approval;
- rejected intent: no apply.

This ADR does not invent CAP-0008 public error codes for those cases.

### 6. Authoritative approval content

The human sees the RevitMCP-generated preview/diff derived from the exact stored intent. That stored approval-preview payload is the content ADR-0008 and CAP-0007 already bind to the fingerprint.

Model-generated prose may accompany that preview. It is non-authoritative. The UI does not reconstruct the write batch from model text.

### 7. Revit-local UI boundary

The merged WebView2 spike is evidence that the preferred v1 Revit approval presentation may use:

```text
Revit DockablePane
-> WPF host
-> local WebView2 content
```

That is the preferred v1 presentation direction. It is not an MCP protocol requirement. It is not the approval authority. The native .NET Addin is the authority. WebView2 remains a bounded presentation and input surface.

Any future implementation of that surface includes:

- bundled local UI content only for the trusted approval surface;
- lazy WebView2 creation;
- a managed WebView2 SDK compatible with the assemblies loaded by each supported Revit host;
- the Evergreen browser runtime treated separately from that managed SDK;
- exact local-origin validation;
- a content security policy;
- typed bounded messages;
- no generic native bridge;
- no arbitrary Revit API access from JavaScript;
- no remote webpage as the trusted approval UI.

This ADR does not copy spike code into production and does not accept WebView2 as production UI architecture.

### 8. Approval UI session binding

The conceptual session pattern is:

```text
native provider
-> resolves the exact intent from the Addin store
-> creates an active local approval session
-> generates a read-only render model from the stored intent
-> displays that session
-> receives Approve or Reject for the current session
-> revalidates the exact intent, fingerprint, and session
-> records the decision internally
```

The WebView2 message itself is not an authority-bearing token. A future bounded message concept such as `approveCurrent` or `rejectCurrent` names the decision for the current session. The page does not send an arbitrary `intent_ref`, fingerprint, or write instruction that the provider treats as authority.

The bundled local approval UI is part of the trusted local interaction path. It must emit Approve or Reject only from an explicit user action for the currently rendered native-bound approval session. Page-load or automatic approval, model-triggered approval, and a generic programmatic approval command are forbidden. Content security policy, origin checks, and session validation constrain the channel. They do not by themselves prove a physical human gesture. The native .NET provider remains the authority.

This ADR does not define a final JSON schema.

### 9. No human wait inside a Revit transaction

The architecture prohibits:

- opening a Revit `Transaction` while waiting for approval;
- keeping an EXEC-0001 mutation execution active while waiting for a human;
- blocking the Revit API thread for an approval UI decision.

Approval occurs before stale-state revalidation and mutation execution. The exact MCP retry and tool UX remains CAP-0008 work.

### 10. v1 human identity

v1 does not require a durable named human identity.

The v1 security guarantee is an explicit decision through the trusted Revit-local interactive provider in the active Revit process.

Audit may record:

- provider type and method;
- decision;
- timestamp;
- intent and fingerprint correlation.

v1 does not persist a username or account identity by default. A future authenticated enterprise provider may add a verified human subject. That subject is not part of v1.

### 11. Future external providers

The architecture allows later:

- a trusted local companion provider;
- an authenticated enterprise or remote approval provider;
- an MCP App acting as a frontend to a separately trusted provider.

When approval crosses a process, machine, or network trust boundary, that provider requires:

- authenticated provider identity;
- integrity-protected attestation;
- exact intent and fingerprint binding;
- expiry;
- replay protection.

This ADR does not select an attestation format, JWT, certificate system, or cryptographic scheme. Same-process v1 does not require a cryptographic signature.

### 12. MCP MRTR remains interaction only

MCP specification `2026-07-28` Multi Round-Trip Requests are an interaction mechanism.

- `inputResponses` are client-provided responses;
- `requestState` passes through the client;
- the MCP specification requires a server to treat `requestState` that affects authorization or business logic as attacker-controlled input and to protect its integrity;
- MRTR does not by itself establish RevitMCP trusted human approval.

This ADR does not authorize MRTR implementation or an MCP SDK upgrade. ADR-0008 decision 11 remains in force.

### 13. MCP Apps remain presentation and interaction only

The official MCP Apps specification, stable `2026-01-26`, is a supporting reference.

App-only visibility may keep UI actions out of model context. It does not prove trusted human approval.

A future MCP App may present an approval experience or front a trusted provider. It is not automatically the authority. ADR-0009 decision 6 remains in force.

### 14. Existing service boundaries stay

Approval does not create a second Revit MCP server.

```text
MCP client
  -> RevitMCP.Server
  -> Bridge
  -> RevitMCP.Addin
```

Inside the Addin:

```text
immutable intent store
trusted approval provider
future controlled apply
EXEC-0001
Revit API
```

Orchestration stays external and optional under ADR-0007. An orchestrator may coordinate workflow. It does not own the approval authority, the Revit transaction, or Revit capability semantics.

### 15. This ADR does not authorize implementation

ADR-0010 does not authorize:

- approval-provider production code;
- production WebView2 UI;
- MCP Apps;
- MRTR;
- an MCP SDK upgrade;
- CAP-0008;
- `revit_apply_parameter_updates`;
- Bridge protocol v9;
- a Server write tool;
- `Parameter.Set`;
- a Revit `Transaction`;
- save or sync;
- production writes;
- external provider attestation implementation;
- authentication architecture.

## Consequences

### Positive

- v1 has a strong and simple trust boundary against untrusted MCP-side inputs: the approval decision is owned by the Revit-local Addin boundary that owns the intent.
- The client and the model cannot self-approve.
- Immutable intents remain unchanged.
- A local approval UI can still be a modern DockablePane surface.
- A companion or enterprise provider can be added later without redefining the write path.
- Same-process v1 does not require cryptographic approval tokens.
- The person is not held inside a Revit mutation context while deciding.

### Costs / limitations

- v1 writes require Revit-local interactive approval.
- Headless and unattended v1 writes remain impossible.
- The approval provider needs lifecycle state separate from `EphemeralWriteIntentStore`.
- An external enterprise provider requires a later trust and attestation design.
- WebView2 compatibility remains a Revit-version integration concern for any future presentation implementation.
- v1 does not claim protection against a compromised Revit process, injected in-process code, tampered binaries or UI assets, or a compromised OS or user session. Stronger isolation is later external-provider and platform-security work.

## Explicitly not decided

- exact provider interfaces and classes;
- the approval decision-store lifecycle specification;
- the exact WebView2 production implementation;
- the exact approval render model;
- the exact audit sink and schema;
- named user identity;
- the external attestation format;
- the CAP-0008 contract;
- apply error codes;
- apply retry UX;
- MRTR integration;
- an MCP App approval frontend;
- the remote provider;
- authentication and authorization;
- Bridge protocol v9 and the Server write surface.

## References

- ADR-0007: federated MCP boundaries and optional orchestration
- ADR-0008: controlled write safety model
- ADR-0009: agent and human interaction and trusted approval boundary
- LIFECYCLE-0002: ephemeral write-intent store
- CAP-0007: `revit_preview_parameter_updates`
- Merged WebView2 spike evidence, PR #60, `spikes/RevitMCP.WebView2Spike/README.md`. Feasibility only. This ADR does not accept that spike as production UI.
- MCP specification `2026-07-28` Multi Round-Trip Requests. `inputResponses` are client-provided. `requestState` that affects authorization or business logic is attacker-controlled and requires integrity protection: https://modelcontextprotocol.io/specification/2026-07-28/basic/patterns/mrtr
- MCP tools specification `2026-07-28`. The protocol does not mandate a UI interaction model: https://modelcontextprotocol.io/specification/2026-07-28/server/tools
- Official MCP Apps specification, stable `2026-01-26`. Supporting reference only. This ADR does not implement MCP Apps: https://github.com/modelcontextprotocol/ext-apps/blob/main/specification/2026-01-26/apps.mdx

External implementations, including Autodesk's official Revit MCP and other MCP hosts, are reference material only. They are not authorities for this decision.
