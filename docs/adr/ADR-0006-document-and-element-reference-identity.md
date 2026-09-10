# ADR-0006: Document and element reference identity

- Status: Accepted
- Date: 2026-09-10

## Context

CAP-0001 established `instance_id` as the process-lifetime identity of a RevitMCP-enabled Revit process. That is sufficient for context discovery, but it is not sufficient for capabilities that return element references intended to be used in later calls.

An Autodesk Revit `ElementId` is scoped to a document/project and Autodesk's Revit API reference warns that ids may change during a Revit session. Autodesk exposes `Element.UniqueId` specifically as a stable identifier that can be stored externally and used to retrieve the same element later if it still exists.

RevitMCP therefore needs two different identity concepts before CAP-0002 can safely support `query -> inspect -> future write` workflows:

- an identity for the currently open Revit `Document`;
- an opaque element reference that is safer to retain across repeated capability calls than a numeric `ElementId`.

The solution must not depend on local paths, cloud identifiers, usernames, document titles, or one deployment/provider model.

## Decision drivers

- Prevent an element reference from being accidentally reused against a different active document.
- Keep identifiers transport-neutral and provider-neutral.
- Work for unsaved local projects, workshared projects, cloud models, and family documents without requiring persistent external metadata.
- Avoid exposing file paths or cloud/project identifiers merely for routing.
- Support safe chaining into CAP-0003 and future controlled write capabilities.
- Preserve ADR-0005 context efficiency without sacrificing identity correctness.
- Avoid changing the already accepted CAP-0001 result contract.

## Options considered

### Option A: Use Revit `ElementId` alone

Advantages:

- compact;
- already used by CAP-0001 for the current active view;
- simple Revit API lookup.

Disadvantages:

- scoped to a document;
- can be unsafe to retain across repeated external-command calls;
- does not protect against active-document changes;
- weak foundation for future write operations.

Rejected for cross-call element references.

### Option B: Use persistent document paths/cloud GUIDs plus `ElementId`

Advantages:

- can identify some documents across sessions;
- familiar to external integrations.

Disadvantages:

- unsaved documents have no path;
- cloud/workshared identity introduces provider- and deployment-specific semantics;
- paths and cloud metadata increase information exposure;
- still inherits `ElementId` cross-call limitations;
- more complexity than CAP-0002 requires.

Rejected as the base capability identity model.

### Option C: RevitMCP-owned ephemeral `document_id` plus opaque `element_ref`

Advantages:

- works for every currently open document including unsaved documents;
- prevents accidental cross-document reuse;
- no file-system or cloud identity exposure;
- element references can use Revit's stable `Element.UniqueId` semantics internally;
- remains compact enough when query results are bounded;
- keeps future persistent document identity as a separate concern.

Disadvantages:

- `document_id` is not stable after close/reopen;
- element references are longer than numeric `ElementId` values;
- the Addin must own a small document-identity service.

Accepted.

## Decision

### 1. `document_id` is opaque and scoped to an open document lifetime

RevitMCP will assign an opaque string `document_id` to a live Revit `Document` when a capability first needs document identity.

Required semantics:

- unique within the Revit process lifetime;
- stable while that specific `Document` remains open in that process;
- not persisted as document metadata;
- not derived from title, file path, central path, cloud project identity, username, or process id;
- a close followed by reopen is allowed and expected to receive a new `document_id`;
- clients must treat the value as opaque.

The initial implementation may generate the value from a random GUID because that is simple and collision-resistant. This is an implementation detail only. Agent-facing schemas must not declare UUID/GUID format or require clients to parse it.

The Addin owns the mapping because the Addin owns live Revit `Document` objects and valid Revit execution context. No Revit API object may escape through `RevitMCP.Contracts`.

### 2. CAP-0002 targets only the active document

CAP-0002 does not introduce arbitrary addressing of inactive/open documents.

If `document_id` is omitted, CAP-0002 uses the active document and returns its assigned `document_id`.

If `document_id` is provided, it acts as a context guard. It must match the currently active document's RevitMCP identity. A mismatch returns `DOCUMENT_CONTEXT_CHANGED`; RevitMCP must not silently run the query against another document.

This keeps the first document-aware capability simple while protecting multi-call workflows from UI context changes.

### 3. Cross-call element references use opaque `element_ref`

Capabilities intended to return element handles for later calls will use `element_ref: string`, not numeric `element_id`.

Required semantics:

- opaque to clients;
- meaningful only with the matching `document_id`;
- stable enough to retrieve the same Revit element in later capability calls while that element still exists;
- current implementation is based on Autodesk Revit `Element.UniqueId`;
- clients must not infer or depend on the internal format.

Using `Element.UniqueId` internally is deliberate because Autodesk documents it as stable and retrievable through the document, while `ElementId` has weaker retention semantics.

RevitMCP may introduce another internal representation in the future without changing the agent contract if the opaque `element_ref` semantics remain satisfied.

### 4. CAP-0001 remains unchanged

CAP-0001 `active_view.element_id` remains an `ElementId` serialized as a string.

That field describes the current active-view context at the time of the call; it was not designed as a durable cross-call element handle. Changing CAP-0001 would add payload and compatibility cost without improving its accepted purpose.

### 5. Identity correctness outranks byte minimization

`Element.UniqueId`-based references are longer than numeric `ElementId` strings. This is accepted under ADR-0005 because identity correctness is required for deterministic recovery and future safe writes.

Query results must instead control context cost through filters, explicit limits, minimal result fields, and deterministic truncation.

## Consequences

### Positive

- CAP-0002 can return reusable element references without ambiguity across active-document changes.
- CAP-0003 can require `document_id + element_ref[]` and fail safely when context has changed.
- Future write capabilities have a stronger identity foundation.
- Unsaved/local/workshared/cloud documents share one agent-facing identity model.
- File paths and cloud project identifiers remain unnecessary for routine agent workflows.

### Costs / limitations

- The Addin needs process-lifetime document identity bookkeeping.
- `document_id` does not identify the same model across close/reopen or separate Revit processes.
- Persistent cross-session model identity remains intentionally deferred.
- `element_ref` values are more verbose than numeric element ids.

## Explicitly deferred

- persistent document identity across Revit sessions;
- inactive-document targeting;
- external database identity;
- cloud/project GUID exposure;
- link-document addressing;
- subelement references;
- persistent write-intent tokens or locks.

## References

- ADR-0003: Revit instance registration, discovery, and addressing
- ADR-0005: Agent context and token efficiency
- CAP-0001: `revit_get_context`
- Autodesk Revit API `ElementId`: https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/44f3f7b1-3229-3404-93c9-dc5e70337dd6.htm
- Autodesk Revit API `Element.UniqueId`: https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/f9a9cb77-6913-6d41-ecf5-4398a24e8ff8.htm
