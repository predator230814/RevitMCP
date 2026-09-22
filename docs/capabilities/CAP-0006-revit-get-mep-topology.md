# CAP-0006: `revit_get_mep_topology`

- Status: Accepted
- Operation class: Read
- Date: 2026-09-22

## Purpose

`revit_get_mep_topology` traverses bounded physical MEP connectivity from known Revit elements and returns a deterministic element-level graph.

It is the topology half of an existing read workflow:

```text
revit_query_elements
→ element_refs

revit_get_mep_topology
→ bounded nodes + undirected physical edges
```

Typical agent use cases:

- start from one or a few known MEP elements and discover physically adjacent equipment, fittings, and segments;
- bound a multi-hop neighborhood without dumping a whole MEP system;
- obtain a deterministic graph that can be repeated, compared, and later inspected with CAP-0003 or CAP-0005;
- tolerate missing or non-connectable seeds without failing an otherwise useful batch.

CAP-0006 is strictly read-only. It does not authorize writes.

This is a v1 physical, element-to-element capability. Logical MEP systems, connector dumps, flow direction, and linked-document traversal are explicitly deferred.

## MCP tool

### Name

`revit_get_mep_topology`

### Title

`Get Revit MEP Topology`

### Description

Traverse bounded physical MEP connectivity from known Revit elements and return a deterministic element-level graph.

### Annotations

- `readOnlyHint: true`
- `openWorldHint: false`

Annotations are descriptive hints, not security boundaries.

## MCP input

Conceptually:

```json
{
  "instance_id": "optional opaque instance",
  "document_id": "required opaque document",
  "seed_element_refs": ["opaque-element-ref"],
  "domain": "hvac",
  "max_depth": 3,
  "max_elements": 100,
  "max_edges": 200
}
```

Required:

```text
document_id
seed_element_refs
```

All objects are closed. Unexpected properties are later Server `INVALID_REQUEST`.

### `instance_id`

Optional Server routing value.

Same semantics as CAP-0001 through CAP-0005.

It must not enter the transport-neutral capability request.

### `document_id`

Required opaque string defined by ADR-0006.

Same active-document guard used by CAP-0003 through CAP-0005.

Required behavior:

- absent/null at a layer where the field is required -> `INVALID_MEP_TOPOLOGY`;
- no active document -> `NO_ACTIVE_DOCUMENT`;
- a present string is opaque and is never trimmed, normalized, parsed, or treated as omitted;
- exact match against the active open-document lifetime -> continue;
- exact mismatch, including empty or whitespace strings, -> `DOCUMENT_CONTEXT_CHANGED`;
- never fall back to another open document.

### `seed_element_refs`

Required array of **1..10** opaque strings.

Uniqueness uses ordinal equality on the raw strings.

String content remains opaque and is not trimmed, normalized, or parsed.

Empty or whitespace present strings are valid syntax and normally become seed status `not_found`.

Null, missing, or non-string items are invalid at the MCP boundary.

Seed result order matches input order.

### `domain`

Optional closed enum:

```text
hvac
piping
electrical
cable_tray_conduit
```

Omitted `domain` means all supported physical connector domains.

A present value restricts both traversal and returned edge domains to that one public domain.

### Bounds

All three bounds are optional integers with hard defaults and closed ranges.

| Field | Default | Min | Max |
| --- | ---: | ---: | ---: |
| `max_depth` | 3 | 1 | 10 |
| `max_elements` | 100 | 10 | 250 |
| `max_edges` | 200 | 10 | 500 |

Omitted fields use the defaults.

`max_depth` is hop distance from the nearest valid seed. Seeds that become nodes have depth `0`.

A later transport-neutral validator must reject out-of-range present values as `INVALID_MEP_TOPOLOGY`.

Malformed MCP types or unexpected properties remain Server `INVALID_REQUEST`.

Do not add:

- pagination cursors;
- system-id filters;
- connector filters;
- direction / upstream / downstream selectors;
- geometry or view-scope options;
- timeout;
- linked-document addressing.

## MCP / transport-neutral output

Closed top-level object:

```text
context
seeds
nodes
edges
truncated
truncation_reasons
```

All required. `additionalProperties` is false.

### `context`

Closed object:

```text
instance_id: string
document_id: string
```

Both required.

### `seeds`

Exactly one item per requested seed, in request order.

Closed item:

```text
element_ref: string
status: ok | not_found | no_connectors
```

Meanings:

- `not_found` — the seed cannot be resolved in the active document, including empty/whitespace or arbitrary opaque strings and `ElementType` inputs;
- `no_connectors` — the element exists but has no connectors eligible for this request after physical/logical and optional domain filtering;
- `ok` — the element exists and has at least one eligible connector. Degree may still be zero because connectors can exist without a physical connection.

Missing seeds are item-level statuses, not whole-call failures.

Only `ok` seeds become depth-`0` nodes and BFS origins.

`not_found` and `no_connectors` seeds appear only in `seeds`.

### `nodes`

Closed items:

```text
element_ref: string
depth: integer
```

`depth` is the minimum hop distance from any valid (`ok`) seed.

Deterministic order:

1. `depth` ascending;
2. then opaque `element_ref` ordinal.

An empty `nodes` array is valid when every seed is `not_found` or `no_connectors`.

### `edges`

Closed undirected items:

```text
element_ref_a: string
element_ref_b: string
domains: unique array of hvac | piping | electrical | cable_tray_conduit
```

Canonicalization:

- `element_ref_a < element_ref_b` using ordinal string comparison;
- edges are then sorted by `(element_ref_a, element_ref_b)` ordinal;
- multiple physical connector pairs between the same two elements collapse into one edge;
- `domains` aggregates unique normalized public domains and is sorted ordinal.

Every edge endpoint must exist in `nodes`.

Self-loops are omitted.

### Truncation

```text
truncated: bool
truncation_reasons: unique array of depth | elements | edges
```

`truncated = true` only when known eligible topology is omitted because of the requested bounds.

`truncation_reasons` identifies which bound omitted known topology. The array is unique and sorted ordinal. It is empty if and only if `truncated = false`.

Do not claim `depth` truncation merely because the result contains nodes at `max_depth`. Record `depth` only after observing that a deeper eligible physical neighbor exists and was omitted.

`elements` means an eligible node was omitted because `max_elements` was reached.

`edges` means an eligible canonical edge between already-accepted or otherwise eligible nodes was omitted because `max_edges` was reached.

More than one reason may be present.

## Physical connection semantics

Traversal uses the Revit API physical connection model, not MEP system membership.

Do not treat two elements as adjacent merely because they belong to the same `MEPSystem`.

Do not traverse `ConnectorType.Logical` references.

The Revit-side connector access boundary must support the normal project-element surfaces used by:

```text
MEPCurve.ConnectorManager
FamilyInstance.MEPModel?.ConnectorManager
FabricationPart.ConnectorManager
```

Elements that expose none of those eligible surfaces, or that expose no remaining connector after filtering, are `no_connectors` when used as seeds and are not expanded.

A physical neighbor is another active-document element reached through a non-logical connector reference (`AllRefs` / equivalent physical connection API) whose owner is a different element.

Skip:

- the connector's own owner;
- logical connector references;
- linked-document owners;
- owners that cannot produce an opaque `element_ref` compatible with ADR-0006 / CAP-0002 (`ElementType` and special/sentinel targets);
- connectors whose Revit `Domain` is not one of the four supported public domains.

### Public domain mapping

Normalize only these Revit domains:

```text
DomainHvac              -> hvac
DomainPiping            -> piping
DomainElectrical        -> electrical
DomainCableTrayConduit  -> cable_tray_conduit
```

Other Revit `Domain` values are unsupported in v1 and do not participate in traversal or edge-domain aggregation.

The Addin owns this mapping. Contracts expose only the four public strings.

## Deterministic multi-source BFS

Use multi-source BFS from every `ok` seed.

Rules:

- each node stores the minimum hop distance from any valid seed;
- seed nodes have depth `0`;
- expansion from a node at depth `D` produces candidates at depth `D + 1`;
- do not expand a node whose depth is already `max_depth`;
- do not enqueue a candidate whose depth would exceed `max_depth`;
- stop accepting new nodes when `max_elements` is reached;
- stop accepting new canonical edges when `max_edges` is reached;
- never emit an edge unless both endpoints are present in `nodes`.

Traversal must be independent of Revit connector enumeration order.

Before enqueue or comparison:

1. collect unique neighbor `element_ref` values;
2. sort those refs ordinal;
3. keep the BFS frontier ordered by `(depth, element_ref)` ordinal.

Repeat requests with the same active document, same seeds, same domain, and same bounds must return the same `seeds`, `nodes`, `edges`, `truncated`, and `truncation_reasons`.

## Transport-neutral request

Conceptually:

```text
GetMepTopologyRequest
{
    document_id: string
    seed_element_refs[1..10]
    domain?: hvac | piping | electrical | cable_tray_conduit
    max_depth?: integer
    max_elements?: integer
    max_edges?: integer
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
- connector identifiers;
- system identifiers;
- direction flags.

Transport-neutral validation must reject with `INVALID_MEP_TOPOLOGY` when, at minimum:

- `document_id` is absent or null;
- `seed_element_refs` is missing, empty, or longer than 10;
- any seed item is not a non-null string;
- seed refs are not unique under ordinal equality of the raw strings;
- `domain` is present and not one of the four public values;
- a present bound is outside its accepted range.

Do not treat empty or whitespace `document_id` or seed strings as omitted. Those present strings remain opaque and are handled by the document guard or seed statuses.

Omitted optional fields receive the accepted defaults before execution.

## Errors

Server routing:

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
INVALID_MEP_TOPOLOGY
REVIT_EXECUTION_TIMEOUT
REVIT_EXECUTION_FAILED
```

Malformed MCP input later maps to `INVALID_REQUEST`.

Do not convert malformed MCP input into `INVALID_MEP_TOPOLOGY`.

Expected missing or non-connectable individual seeds remain seed statuses rather than whole-batch failures.

## Document guard and execution

Follow the existing capability pattern:

1. validate the transport-neutral request before Revit execution;
2. execute Revit API access only through EXEC-0001;
3. require an active document;
4. validate exact `document_id` before resolving seeds;
5. resolve each seed in request order;
6. run deterministic multi-source BFS;
7. create no `Transaction`, `SubTransaction`, or `TransactionGroup`.

`element_ref` resolution uses the existing opaque UniqueId-based handle without parsing.

No Autodesk `Connector`, `Connector.Id`, connector index, numeric `ElementId`, coordinates, geometry, file path, or Revit API object may enter Contracts or the MCP result.

## Bounds / context efficiency

This capability satisfies ADR-0005 by returning the smallest deterministic graph that fulfills an explicit bounded neighborhood:

- 1..10 seed refs, independent of model size;
- hard node, edge, and depth caps;
- element-level graph rather than a connector dump;
- collapsed undirected edges rather than connector multiplicity;
- item-level seed statuses instead of batch failure for expected misses;
- no system-wide MEP catalog;
- no geometry or coordinates.

## Context / security boundaries

CAP-0006 does not return:

- connector objects or public `connector_ref` values;
- connector indexes or `Connector.Id`;
- numeric durable `ElementId`;
- coordinates, geometry, or bounding boxes;
- MEP system names, ids, or membership lists;
- flow direction, pressure, or calculated system results;
- file paths, usernames, or cloud/project identifiers;
- writeability or authorization decisions.

`readOnlyHint` is not a write-authorization substitute.

## MCP success policy

Modern successful result:

```text
isError = false
structuredContent = GetMepTopologyResult
content = []
```

Errors:

```text
isError = true
structuredContent absent
one compact JSON TextContent error
```

The official MCP tool is specified here. Do not register it until BRIDGE-0007 is designed, implemented, and live-tested through the typed Bridge, then SERVER-0006 is designed and implemented.

## Compatibility

RevitMCP continues to target Revit 2025, 2026, and 2027 from one shared Addin project. The connector-manager surfaces named above belong to that supported matrix.

This specification does not claim live CAP-0006 validation on Revit 2025, 2026, or 2027. Those remain future implementation and validation work.

Do not treat compile-time availability as live proof.

## Acceptance criteria

CAP-0006 is acceptable as a capability contract when:

1. It is one coherent read-only capability.
2. `document_id` is required as a present string. Absent/null is `INVALID_MEP_TOPOLOGY`. A present string is opaque, is never trimmed or normalized, and is compared exactly to the active document id. Empty or whitespace therefore yields `DOCUMENT_CONTEXT_CHANGED`.
3. `seed_element_refs` contains 1..10 unique opaque strings. Uniqueness uses ordinal equality on the raw strings. Content is not trimmed or normalized.
4. Empty or whitespace seed strings remain valid syntax and normally become `not_found`.
5. `instance_id` remains Server routing-only.
6. `domain` is omitted or exactly one of `hvac`, `piping`, `electrical`, `cable_tray_conduit`. Omitted means all supported physical domains.
7. Bounds use the accepted defaults and closed ranges. Present out-of-range values are `INVALID_MEP_TOPOLOGY`.
8. Output is a closed `{context, seeds, nodes, edges, truncated, truncation_reasons}` object.
9. Seed results preserve request order and use only `ok`, `not_found`, and `no_connectors`.
10. Only `ok` seeds become depth-`0` nodes and BFS origins.
11. Nodes are ordered by depth ascending, then `element_ref` ordinal. Depth is the minimum hop distance from any valid seed.
12. Edges are undirected, canonicalized `element_ref_a < element_ref_b`, sorted `(a,b)`, and collapse connector multiplicity into unique normalized domains.
13. Every edge endpoint exists in `nodes`. Self-loops are omitted.
14. Traversal is physical-connector based and independent of Revit connector enumeration order.
15. Logical connectors, MEP-system membership, linked documents, and public connector identity are out of scope.
16. `truncated` and `truncation_reasons` report omitted known topology only. Depth truncation requires an observed deeper eligible neighbor.
17. `DOCUMENT_CONTEXT_CHANGED` is a top-level capability error with no document fallback.
18. All Revit API access executes through EXEC-0001 and creates no transaction.
19. Contracts remain transport-neutral and expose no Revit API objects.
20. Server remains Revit-API independent.
21. Revit 2025, 2026, and 2027 variants compile in CI when implemented.
22. Automated coverage includes request validation, uniqueness, defaults/ranges, seed statuses, deterministic node/edge ordering, edge canonicalization/domain aggregation, truncation-reason rules, and protocol/Bridge gating. Live typed-Bridge Revit validation covers real physical adjacency, multi-hop depth, deterministic repeat, truncation, missing seed, no-connectors seed if naturally available, and the document guard. Naturally unavailable Revit cases may be recorded rather than manufactured.
23. Official MCP live validation is a later SERVER-0006 gate, not part of this specification PR.
24. CAP-0001 through CAP-0005 remain unchanged by this specification.

## Explicitly deferred

- logical connector traversal and `ConnectorType.Logical`;
- MEP system membership as adjacency;
- public `connector_ref` or connector dump;
- connector coordinates, orientation, or flow direction;
- upstream/downstream or implicit flow analysis;
- calculated system results;
- linked-document traversal;
- `ElementType` seeds or nodes;
- pagination;
- whole-document or whole-system topology dumps;
- writes, write authorization, preview/approval, or transactions.

## Bridge / Server sequencing

CAP-0006 itself is transport-neutral.

Expected implementation sequence:

```text
CAP-0006 accepted
-> BRIDGE-0007 design/implementation
-> typed Bridge live validation
-> SERVER-0006 design/implementation
-> official MCP live validation
```

This documentation PR defines CAP-0006, BRIDGE-0007, and SERVER-0006 together. It does not implement any of them.

Do not implement a later protocol version by numeric `>=` behavior. Existing capabilities must be explicitly preserved on any new protocol version.

## References

- ADR-0005: Agent context and token efficiency
- ADR-0006: Document and element reference identity
- ADR-0007: Federated MCP boundaries and optional orchestration
- CAP-0002: `revit_query_elements`
- CAP-0003: `revit_get_elements`
- EXEC-0001: serialized Revit execution dispatcher
- Autodesk Revit API `Connector` class
- Autodesk Revit API `ConnectorManager` class
- Autodesk Revit API `Domain` enumeration
- Autodesk Revit API `ConnectorType` enumeration
