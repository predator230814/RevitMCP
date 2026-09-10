# CAP-0002: `revit_query_elements`

- Status: Accepted
- Operation class: Read
- Date: 2026-09-10

## Purpose

`revit_query_elements` finds Revit elements in the active document using a small deterministic filter set and returns only bounded opaque element references.

The capability is the discovery half of a deliberate `query -> inspect` workflow. It does not return element parameters, geometry, bounding boxes, or repeated metadata for every match. A later CAP-0003 may inspect a bounded set of returned `element_ref` values with explicit field projection.

Typical agent use cases:

- find all instances of one or more categories in the active document or view;
- narrow elements by family, type, or associated level;
- search element/family/type names before requesting deeper details;
- obtain stable opaque element references for a follow-up inspection capability;
- determine exact match count without sending the matched element bodies to the LLM.

The design follows ADR-0005: RevitMCP filters, counts, sorts, limits, and projects before returning data.

## MCP tool

### Name

`revit_query_elements`

### Title

`Query Revit Elements`

### Description

Find elements in the active Revit document using bounded filters and return opaque element references. Use this before requesting details about matching elements.

### Annotations

- `readOnlyHint: true`
- `openWorldHint: false`

Annotations are descriptive hints, not security boundaries.

## MCP input

Conceptually:

```json
{
  "instance_id": "optional opaque RevitMCP instance identifier",
  "document_id": "optional opaque active-document guard",
  "scope": "document",
  "filters": {
    "category_names": ["Mechanical Equipment"],
    "family_names": ["VAV Box"],
    "type_names": ["VAV-6in"],
    "level_names": ["Level 2"],
    "text_contains": "VAV"
  },
  "limit": 50
}
```

### `instance_id`

Optional. It follows ADR-0003 and CAP-0001 routing semantics and is treated as an opaque string.

It is a Server routing concern and is not part of the transport-neutral Revit query request after a target Revit process has been selected.

### `document_id`

Optional opaque string defined by ADR-0006.

- omitted: use the active document and return its RevitMCP `document_id`;
- supplied and equal to the active document identity: execute normally;
- supplied but not equal to the active document identity: return `DOCUMENT_CONTEXT_CHANGED`;
- no active document: return `NO_ACTIVE_DOCUMENT`.

No automatic fallback to a different document is allowed when `document_id` was explicitly supplied.

The MCP schema must not require UUID/GUID formatting even if the initial implementation generates `document_id` values from GUIDs.

### `scope`

Required enum:

```text
document
active_view
```

`document` searches eligible elements in the active document.

`active_view` searches eligible elements belonging to/represented in the active view according to Revit's view-scoped element collection semantics. If no active view is available, or the active view cannot be used for Revit view-scoped element iteration, return `NO_ACTIVE_VIEW` rather than silently widening the query to document scope.

CAP-0002 v1 does not accept arbitrary view ids.

### `filters`

Required object. At least one filter property must be present.

Supported v1 properties:

```text
category_names: string[]
family_names: string[]
type_names: string[]
level_names: string[]
text_contains: string
```

For each array property:

- values are compared using ordinal case-insensitive text matching;
- values within the same array are ORed;
- an empty array is invalid;
- maximum array length is **20** values;
- each value must contain between **1 and 256 characters**;
- values are not trimmed or otherwise normalized before comparison beyond the specified case-insensitive comparison.

Different filter properties are ANDed.

Example:

```text
category_names = ["Mechanical Equipment", "Air Terminals"]
level_names = ["Level 2"]
```

means:

```text
(category is Mechanical Equipment OR Air Terminals)
AND
(level is Level 2)
```

#### Category semantics

`category_names` matches the Revit category display name of the element.

Category names are intentionally Revit-display names in v1 and may therefore be localized. A future stable category taxonomy may be introduced only if real workflows justify the additional schema surface.

#### Family semantics

`family_names` matches the family name exposed by the element's resolved type metadata when available.

Elements without a resolvable family name cannot match this filter.

#### Type semantics

`type_names` matches the resolved element type name when available.

Elements without a resolvable type cannot match this filter.

#### Level semantics

`level_names` matches the name of the element's directly associated/resolvable Revit level when available.

Elements without a resolvable associated level cannot match this filter. CAP-0002 v1 does not infer a level from geometry, bounding boxes, rooms, hosts, or custom parameters.

#### Text semantics

`text_contains` is a case-insensitive substring match containing between **1 and 256 characters**. The supplied value is not trimmed or normalized before matching.

It matches when the supplied text occurs in at least one available value among:

- element name;
- resolved family name;
- resolved type name.

Unavailable values are ignored. CAP-0002 does not search parameter values or geometry text.

### `limit`

Optional integer:

```text
default: 50
minimum: 1
maximum: 100
```

The limit controls the number of returned `element_ref` values, not the exact `matched_count`.

There is no unbounded mode.

## Query population

CAP-0002 v1 returns element instances/model/view elements, not Revit `ElementType` objects.

Element types are excluded from the query result population. Family/type information may still be resolved internally for filtering.

Linked-document contents are not traversed. A `RevitLinkInstance` in the host document may only participate as an ordinary host-document element if it otherwise matches the query; elements inside linked documents are deferred.

The capability does not enumerate every element body into the result. RevitMCP may inspect candidate metadata internally as needed to evaluate the accepted filters.

## Matching, ordering, and limiting

Required processing order:

```text
establish active document and document identity
-> establish requested scope
-> find eligible non-ElementType elements
-> apply all filters deterministically
-> compute exact matched_count
-> derive opaque element_ref values
-> order references using ordinal string ordering
-> take first limit references
-> set truncated = matched_count > returned reference count
-> return minimal structured result
```

Ordering by opaque `element_ref` keeps the result deterministic without making numeric `ElementId` part of the agent contract.

CAP-0002 v1 intentionally has no cursor/pagination. When `truncated = true`, the intended recovery behavior is for the agent to refine the query. Pagination over a mutable Revit model is deferred until a demonstrated workflow requires it.

## Transport-neutral request

Conceptually:

```text
QueryElementsRequest
{
    document_id?: string
    scope: QueryScope
    filters: QueryElementFilters
    limit: integer
}
```

It contains no:

- `instance_id`;
- MCP request metadata;
- model-provider metadata;
- pipe/process/session identifiers;
- timeout supplied by the agent.

## Result contract

Conceptually:

```text
QueryElementsResult
{
    context
    matched_count
    truncated
    element_refs[]
}
```

Exact agent-facing shape:

```json
{
  "context": {
    "instance_id": "a81e...",
    "document_id": "opaque-document-id"
  },
  "matched_count": 12,
  "truncated": false,
  "element_refs": [
    "opaque-element-ref-1",
    "opaque-element-ref-2"
  ]
}
```

### `context`

Required fields:

```text
instance_id: string
document_id: string
```

Both are opaque. `document_id` is defined by ADR-0006 and identifies the active open-document lifetime for this result.

### `matched_count`

Required integer `>= 0`.

It is the exact number of elements that match all query filters before `limit` is applied.

### `truncated`

Required boolean.

```text
true  when matched_count > element_refs.length
false otherwise
```

### `element_refs`

Required array of zero to `limit` unique opaque strings.

The initial implementation derives `element_ref` from Revit `Element.UniqueId`, but clients must not parse or depend on that representation.

An `element_ref` is meaningful only with the matching `document_id`.

The result intentionally does not return per-element:

- element id;
- name;
- category;
- family;
- type;
- level;
- parameters;
- bounding boxes;
- geometry;
- location;
- workset;
- phase;
- design option;
- ownership/user data.

Those belong in later explicitly projected capabilities.

## Empty result

Zero matches is a successful query:

```json
{
  "context": {
    "instance_id": "a81e...",
    "document_id": "opaque-document-id"
  },
  "matched_count": 0,
  "truncated": false,
  "element_refs": []
}
```

It is not an execution error.

## Revit execution behavior

CAP-0002 is read-only and must not create a Revit `Transaction`, `SubTransaction`, or `TransactionGroup`.

All live Revit API access required for document identity, scope collection, filtering, type/family/level resolution, and element-reference derivation must execute through EXEC-0001 in a valid Revit API execution context.

No Autodesk Revit API object may escape through `RevitMCP.Contracts`.

## Deterministic errors

Server routing errors remain:

```text
NO_REVIT_INSTANCE
INSTANCE_REQUIRED
INSTANCE_NOT_FOUND
INSTANCE_UNAVAILABLE
```

Capability errors introduced/used by CAP-0002:

```text
NO_ACTIVE_DOCUMENT
DOCUMENT_CONTEXT_CHANGED
NO_ACTIVE_VIEW
INVALID_QUERY
REVIT_EXECUTION_TIMEOUT
REVIT_EXECUTION_FAILED
```

### `NO_ACTIVE_DOCUMENT`

No active Revit document exists at execution time.

### `DOCUMENT_CONTEXT_CHANGED`

The caller supplied a `document_id` that does not identify the currently active document.

This error is intentional protection against running a chained query in the wrong document.

### `NO_ACTIVE_VIEW`

`scope = active_view` was requested but there is no active view that can be used for Revit view-scoped element iteration.

This error must not cause automatic fallback to document scope.

### `INVALID_QUERY`

The transport-neutral request violates query invariants such as empty filters, more than 20 values in one filter array, an out-of-range filter string length, or an out-of-range limit. MCP schema validation should reject malformed MCP inputs before normal capability execution where supported.

Unknown/non-matching category/family/type/level names are not errors; they may legitimately produce zero matches.

## MCP output policy

The MCP adapter must advertise a strict output schema matching the accepted result exactly.

For modern structured-content-capable MCP revisions:

```text
structuredContent = QueryElementsResult
content = []
isError = false
```

Do not duplicate the full JSON result into text.

Tool execution errors use the same compact agent-facing error policy established by SERVER-0001: `isError = true`, no success-shaped `structuredContent`, and one compact machine-readable JSON text block.

Legacy fallback may serialize one compact JSON text result only when the official MCP SDK's negotiated compatibility behavior requires it. RevitMCP must not implement a custom protocol-version state machine.

## Context-size and performance requirements

- Output is bounded to at most 100 element references.
- Default output returns at most 50 references.
- At least one filter is required; whole-model unfiltered enumeration is not a normal CAP-0002 call.
- Every filter array is bounded to at most 20 values and individual filter strings are bounded to at most 256 characters.
- The capability returns references, not element DTOs.
- `matched_count` is computed server-side/Revit-side so the LLM does not need a broad payload to count matches.
- Result payload must not grow with parameter count, geometry complexity, or the number of matches beyond the explicit reference limit.
- No hard latency SLA is accepted yet; live measurements should be recorded during implementation validation on both small and meaningfully larger disposable/sample models when practical.

## Security and data minimization

CAP-0002 does not return file paths, usernames, cloud project ids, process ids, pipe names, local environment metadata, or full element properties.

`document_id` and `element_ref` are identifiers required for correctness and safe chaining; they are not authorization tokens.

Future write capabilities must perform their own authorization/confirmation checks even when given valid references.

## Acceptance criteria

CAP-0002 is acceptable when:

1. it is exposed as exactly one additional read-only MCP tool;
2. it follows existing deterministic instance routing;
3. it requires bridge protocol version 3 as defined by BRIDGE-0003;
4. omitted `document_id` targets the active document and returns its identity;
5. explicit mismatched `document_id` fails with `DOCUMENT_CONTEXT_CHANGED` and does not query another document;
6. document and active-view scopes behave deterministically and active-view failure never widens silently to document scope;
7. at least one filter is required;
8. filter arrays are bounded to 20 values and filter strings to 256 characters;
9. supported filters follow the exact AND/OR/text semantics above;
10. Revit `ElementType` objects are excluded from returned query population;
11. result ordering is deterministic;
12. `matched_count` is exact before limiting;
13. default limit is 50 and maximum is 100;
14. `truncated` is correct;
15. result contains only context, count, truncation flag, and opaque element references;
16. returned element references are based on stable Revit element identity rather than numeric `ElementId` retention;
17. modern MCP success has empty `content` and authoritative `structuredContent`;
18. no Revit transaction/write occurs;
19. all Revit API access runs through EXEC-0001;
20. automated tests cover filters, boundaries, context guards, errors, and output shape;
21. live Revit 2026.5 validation proves a real MCP client can query a disposable project end to end.

## Explicitly deferred

- CAP-0003 element inspection/details;
- parameter-value filters;
- bounding-box/spatial filters;
- arbitrary/specific view targeting;
- linked-document traversal;
- geometry/location queries;
- phases and design options;
- workset/ownership filters;
- cursor pagination;
- element-type enumeration;
- stable category taxonomy independent of Revit localization;
- write/select/zoom/open-view operations.

## References

- ADR-0003: Revit instance registration/discovery/addressing
- ADR-0005: Agent context and token efficiency
- ADR-0006: Document and element reference identity
- CAP-0001: `revit_get_context`
- BRIDGE-0003: CAP-0002 bridge RPC and protocol v3
- SERVER-0002: stdio exposure of `revit_query_elements`
