# CAP-0010: `revit_get_selection`

- Status: Accepted
- Operation class: Read
- Date: 2026-10-14
- Product Owner acceptance: Dave, 2026-10-14

## Authorization

This specification is **Accepted**. Dave, Product Owner, accepted it on 2026-10-14.

Implementation is not part of this specification. CAP-0010, bridge protocol version 13, and the MCP tool `revit_get_selection` are not implemented. Production Bridge remains protocol v12. Production `tools/list` remains exactly 10 tools.

## Purpose

`revit_get_selection` returns the current bounded element selection of the active Revit document as opaque ADR-0006 `element_ref` values.

CAP-0001 returns the selection count and does not return selected elements. CAP-0010 fills that gap. It does not change CAP-0001.

```text
CAP-0001
  -> selection count only

CAP-0010
  -> bounded current selected element_refs
```

Typical workflows:

```text
revit_get_selection
-> document_id + element_ref[]
-> revit_get_elements
```

and later:

```text
revit_get_selection
-> revit_get_mep_topology
```

The intended design remains selection, then inspect, then optionally explore topology, including item-level downstream failures. This capability is not an inspect-everything call.

## MCP tool

### Name

`revit_get_selection`

### Title

`Get Revit Selection`

### Description

Return a bounded deterministic snapshot of elements currently selected in the active Revit document as opaque element references.

### Annotations

- `readOnlyHint: true`
- `openWorldHint: false`

Annotations are descriptive hints, not security boundaries.

## MCP input

Closed object:

```json
{
  "instance_id": "optional opaque instance",
  "document_id": "optional opaque document",
  "limit": 25
}
```

All three fields are optional. `additionalProperties` is false.

Malformed MCP types and unknown properties are Server `INVALID_REQUEST`.

Transport-neutral validation errors are `INVALID_SELECTION`.

### `instance_id`

Optional Server routing value.

Same ADR-0003 routing semantics as the existing ordinary read tools.

It must not enter the transport-neutral capability request.

Omitted or JSON `null` means unspecified. A present string is routed exactly. An empty or whitespace present string never auto-selects. The Server does not require a caller to send `instance_id`.

### `document_id`

Optional opaque active-document guard, defined by ADR-0006.

- omitted, or JSON `null`: use the current active document and return its `document_id`;
- a present string that ordinal-matches the active open-document identity: continue;
- a present string that does not ordinal-match: `DOCUMENT_CONTEXT_CHANGED`;
- no active document: `NO_ACTIVE_DOCUMENT`.

A present string is opaque raw text. The capability never trims, normalizes, parses, or requires GUID format. Empty and whitespace present strings stay present and are compared exactly. They are not treated as omitted.

The capability never switches to another open document.

### `limit`

Optional integer.

```text
default: 25
minimum: 1
maximum: 100
```

Omitted `limit` uses 25. There is no unbounded mode and no cursor.

A present MCP value that is not an integer, or that is outside 1..100, is `INVALID_REQUEST`.

A transport-neutral present value outside 1..100 is `INVALID_SELECTION`.

The limit bounds `element_refs`. It does not change `selected_count`.

## MCP / transport-neutral output

Closed result object. `additionalProperties` is false.

```json
{
  "context": {
    "instance_id": "opaque",
    "document_id": "opaque"
  },
  "selected_count": 0,
  "truncated": false,
  "element_refs": []
}
```

Required fields:

```text
context
selected_count
truncated
element_refs
```

### `context`

Closed object:

```text
instance_id: string
document_id: string
```

Both required.

`instance_id` is the actual addressed Revit process. The transport-neutral request does not carry it. The capability result reports the process that executed the read, including when the caller omitted MCP `instance_id`.

`document_id` is the current active open-document identity from the existing ADR-0006 open-document identity service. It is not derived from path, title, username, or cloud metadata.

### `selected_count`

Integer `>= 0`.

Exact count of the current selection population after the active-document ids from `UIDocument.Selection.GetElementIds()` are resolved to unique opaque `element_ref` values.

The count is computed before `limit` is applied.

### `element_refs`

Array of opaque ADR-0006 strings.

- CAP-0010 uses the same ADR-0006 opaque `element_ref` representation, currently backed by `Element.UniqueId`;
- a returned ref is valid opaque input to a later capability for the same `document_id`;
- valid input does not guarantee downstream success;
- clients must not parse or infer the representation;
- schemas must not declare UUID/GUID format;
- values are unique by ordinal string equality;
- order is ascending ordinal string order;
- length is at most `limit`.

### `truncated`

Boolean.

```text
truncated = selected_count > element_refs.Count
```

### Empty selection

An empty current selection is a successful result:

```text
selected_count = 0
element_refs = []
truncated = false
```

## Revit selection semantics

v1 reads the selection with:

```text
UIDocument.Selection.GetElementIds()
```

Conceptual shaping, inside one EXEC-0001 execution:

```text
GetElementIds()
-> resolve active-document Elements
-> derive opaque element_ref
-> ordinal unique
-> ordinal sort
-> compute selected_count
-> take limit
-> truncated
```

The result must not depend on Revit's selection enumeration order.

Ordinal unique means two derived refs that compare equal with ordinal string equality contribute one ref. Ordinal sort uses ascending ordinal string comparison, the same ordering CAP-0002 uses for `element_ref`.

Collect that snapshot in one EXEC-0001 execution.

Create no `Transaction`, `SubTransaction`, or `TransactionGroup`.

Do not call `Selection.SetElementIds`.

Do not initiate `PickObject`, `PickObjects`, or any other interactive selection operation.

### Relationship to CAP-0001

CAP-0001 `selection.count` remains `UIDocument.Selection.GetElementIds().Count`. This specification does not change that field.

CAP-0010 `selected_count` is the unique resolved `element_ref` population before `limit`. The two integers are not required to be equal.

An id returned by `GetElementIds()` that does not resolve to an element in the active document, or that cannot produce an ADR-0006 `element_ref`, is omitted from `selected_count` and from `element_refs`. This result has no unresolved-count field.

A `RevitLinkInstance` that is itself present in `GetElementIds()` is an active-document element. It may be returned as that host element's `element_ref`. Contents of the link are not read.

### Downstream composability

Membership comes from `UIDocument.Selection.GetElementIds()`. v1 does not add the CAP-0002 `ElementType` exclusion. This result has no new output field for element class.

CAP-0010 uses the same ADR-0006 opaque `element_ref` representation, currently backed by `Element.UniqueId`. It may emit a ref that CAP-0002 itself would not emit, including an `ElementType` selected through the `GetElementIds()` surface.

Downstream capabilities retain their own eligibility and status semantics. CAP-0003 and CAP-0006 may therefore report `not_found` for an `ElementType` ref. The selection, inspect, and optional topology workflow includes those item-level failures.

"Valid input" means a valid opaque ref input. It does not guarantee that CAP-0002 would have emitted the ref, and it does not guarantee downstream success.

### Active document and family documents

No `ActiveUIDocument` returns `NO_ACTIVE_DOCUMENT`.

That differs from CAP-0001, which succeeds with selection count `0` when no active UI document exists. CAP-0001 stays on that success path. CAP-0010 does not.

Project documents are supported.

Family documents are supported.

The capability does not require:

- a writable document;
- active-view element iteration;
- model modification.

A read-only document remains eligible. An empty selection is not an error.

## Linked elements and subelements

CAP-0010 v1 exposes only the element selection represented by `Selection.GetElementIds()` in the active document.

v1 does not introduce `Selection.GetReferences()` semantics.

The following are deferred:

- linked-document element identity;
- link-document addressing;
- selected elements inside Revit Links;
- subelement references;
- faces, edges, and geometry references;
- a stable `linked_element_ref`;
- `subelement_ref`.

Selection references that require `GetReferences()`, including linked-document or subelement semantics not represented by the `GetElementIds()` surface, are outside CAP-0010 v1.

Do not manufacture host `element_ref` values for linked-document contents.

ADR-0006 remains authoritative for `document_id` and `element_ref`.

## Data minimization

CAP-0010 returns only selection refs and context.

Do not add:

- element names;
- categories;
- families;
- types;
- levels;
- parameters;
- parameter refs;
- geometry;
- bounding boxes;
- coordinates;
- MEP topology;
- connector information;
- linked-model metadata;
- local paths;
- cloud ids;
- usernames;
- writeability;
- authorization decisions.

Those belong to existing or future capabilities.

## Transport-neutral request

Conceptually:

```text
GetSelectionRequest
{
    document_id?: string
    limit?: integer
}
```

It contains no:

- `instance_id`;
- MCP metadata;
- LLM/provider information;
- process id;
- pipe name;
- file path;
- user identity;
- agent-controlled timeout;
- numeric `ElementId` values;
- pagination cursor.

Transport-neutral validation must reject with `INVALID_SELECTION` when a present `limit` is outside 1..100.

Do not treat empty or whitespace `document_id` as omitted.

Omitted `limit` receives the default 25 before execution. Omitted `document_id` means the active document, not an empty string.

JSON property names are snake_case. Contract types live in `BimIt.Revit.Contracts` and must not reference Autodesk Revit API types.

## Errors

Server routing, unchanged from the ordinary read tools:

```text
NO_REVIT_INSTANCE
INSTANCE_REQUIRED
INSTANCE_NOT_FOUND
INSTANCE_UNAVAILABLE
```

Capability:

```text
NO_ACTIVE_DOCUMENT
DOCUMENT_CONTEXT_CHANGED
INVALID_SELECTION
REVIT_EXECUTION_TIMEOUT
REVIT_EXECUTION_FAILED
```

Malformed MCP input maps to `INVALID_REQUEST`.

Do not convert malformed MCP input into `INVALID_SELECTION`.

An empty selection is a successful result.

Do not add retries.

## Document guard and execution

1. Validate the transport-neutral request before Revit execution.
2. Execute Revit API access only through EXEC-0001.
3. Require an `ActiveUIDocument`.
4. When `document_id` is present, compare it with ordinal equality to the active open-document identity before shaping the selection.
5. Read `UIDocument.Selection.GetElementIds()` once for the call.
6. Resolve, uniquify, sort, count, and limit as specified above.
7. Create no `Transaction`, `SubTransaction`, or `TransactionGroup`.

No numeric `ElementId` and no Revit API object may enter Contracts or the MCP result.

Repeat a request against an unchanged selection and an unchanged document and the result is the same: the same `document_id`, `selected_count`, `truncated`, and `element_refs`.

## Bounds / context efficiency

This capability satisfies ADR-0005 by returning handles instead of element bodies:

- exact `selected_count` before the limit;
- at most `limit` refs, default 25, hard maximum 100;
- no unbounded mode;
- no names, parameters, geometry, or topology in this result;
- no pagination cursor over a mutable selection.

When `truncated` is true, the caller narrows the selection in Revit or raises `limit` within 1..100. Cursor pagination is deferred.

## Context / security boundaries

`document_id` and `element_ref` are identifiers required for chaining. They are not authorization tokens.

`readOnlyHint` is not a write-authorization substitute.

The capability returns no local path, cloud id, username, or writeability flag.

## MCP success policy

Modern successful result:

```text
isError = false
structuredContent = GetSelectionResult
content = []
```

Errors:

```text
isError = true
structuredContent absent
one compact JSON TextContent error
```

The official MCP tool is specified here and in [SERVER-0010](../server/SERVER-0010-stdio-get-selection-tool.md). Do not register it in this specification.

## Compatibility

RevitMCP continues to target Revit 2025, 2026, and 2027 from one shared Addin project. `UIDocument.Selection.GetElementIds()` belongs to that supported matrix.

When CAP-0010 is implemented, the Revit 2025, 2026, and 2027 variants must compile.

This specification does not claim live validation. Compile-time availability is not live proof.

## Acceptance criteria

CAP-0010 is acceptable as a capability contract when:

1. It is one coherent Read capability.
2. CAP-0001 remains unchanged and continues to expose only the selection count.
3. `instance_id` remains Server-routing only and does not enter the transport-neutral request.
4. `document_id` is optional and acts only as an exact active-document guard.
5. No active document returns `NO_ACTIVE_DOCUMENT`.
6. An empty current selection succeeds with `selected_count = 0` and `element_refs = []`.
7. Project documents are supported.
8. Family documents are supported.
9. The selection source is `UIDocument.Selection.GetElementIds()`.
10. The result uses ADR-0006 opaque `element_ref` identity.
11. `selected_count` is computed before the limit.
12. `element_refs` are unique and ordinal-sorted.
13. `limit` defaults to 25 and is constrained to 1..100.
14. `truncated` is true if and only if `selected_count` exceeds the returned ref count.
15. A repeated request against an unchanged selection and document returns the same result.
16. A `document_id` mismatch returns `DOCUMENT_CONTEXT_CHANGED` and never falls back to another document.
17. No numeric `ElementId` enters the contract or the result.
18. No Revit API object enters Contracts.
19. No transaction or selection mutation occurs.
20. `GetReferences`, linked-element, and subelement semantics are explicitly deferred.
21. Revit 2025, 2026, and 2027 compilation is required when the capability is implemented.
22. Automated tests are required when the capability is implemented.
23. Typed Bridge live validation is required before the Server implementation is considered complete.
24. Official MCP-client live validation is required before CAP-0010 is considered implementation-complete.
25. CAP-0001 through CAP-0009 remain behaviorally unchanged.

## Explicitly deferred

- linked-document selection contents;
- link-document addressing;
- subelements;
- faces, edges, and geometry references;
- `GetReferences`-based selection;
- selection modification;
- Revit pick UI;
- parameters or element details inside get-selection;
- automatic MEP topology expansion;
- Assistant `bimit_inspect_selection`;
- Assistant `bimit_explore_selected_mep`;
- writes;
- arbitrary Revit API execution;
- pagination and cursors;
- an unresolved-id count on this result;
- any change to CAP-0001 selection count.

## Bridge / Server sequencing

CAP-0010 itself is transport-neutral.

The bridge contract is [BRIDGE-0013](../bridge/BRIDGE-0013-get-selection-capability-rpc.md). It assigns protocol version 13 and preserves the v12 guarantees by explicit version sets. That version is not implemented by numeric `>=` behavior.

The server contract is [SERVER-0010](../server/SERVER-0010-stdio-get-selection-tool.md).

Expected sequence after this accepted specification:

```text
CAP-0010 / BRIDGE-0013 / SERVER-0010 accepted
-> Contracts, Addin, and typed Bridge implementation
-> typed Bridge live validation
-> Server implementation
-> official MCP-client live validation
```

This specification does not declare any of those later steps complete.

## References

- ADR-0003: Revit instance registration, discovery, and addressing
- ADR-0005: Agent context and token efficiency
- ADR-0006: Document and element reference identity
- CAP-0001: `revit_get_context`
- CAP-0002: `revit_query_elements`
- CAP-0003: `revit_get_elements`
- CAP-0006: `revit_get_mep_topology`
- EXEC-0001: serialized Revit execution dispatcher
- Autodesk Revit API `UIDocument.Selection.GetElementIds`
