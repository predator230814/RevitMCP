# CAP-0003: `revit_get_elements`

- Status: Accepted
- Operation class: Read
- Date: 2026-09-10

## Purpose

`revit_get_elements` inspects a small bounded set of known Revit element references in the active document and returns only the fields and named parameters explicitly requested by the caller.

It is the inspection half of the intentional RevitMCP workflow:

```text
revit_query_elements
-> document_id + bounded element_ref[]
-> revit_get_elements
-> projected element details
```

CAP-0003 does not perform broad model discovery. CAP-0002 remains responsible for finding and narrowing elements before inspection.

The design follows ADR-0005: RevitMCP projects and bounds element data before returning it rather than sending broad element or parameter dumps to the LLM.

Typical agent use cases:

- inspect the name/category/family/type/level of a small set of elements returned by CAP-0002;
- retrieve a small explicit set of visible instance/type parameters such as `Mark` or `Flow`;
- tolerate elements being deleted between query and inspection without failing an otherwise useful batch;
- chain read operations safely through `document_id + element_ref` before future controlled write capabilities exist.

## MCP tool

### Name

`revit_get_elements`

### Title

`Get Revit Elements`

### Description

Inspect a bounded set of Revit element references in the active document and return only explicitly requested fields and named parameters.

### Annotations

- `readOnlyHint: true`
- `openWorldHint: false`

Annotations are descriptive hints, not security boundaries.

## MCP input

Conceptually:

```json
{
  "instance_id": "optional opaque RevitMCP instance identifier",
  "document_id": "required opaque active-document guard",
  "element_refs": [
    "opaque-element-ref-1",
    "opaque-element-ref-2"
  ],
  "projection": {
    "fields": [
      "name",
      "category_name",
      "family_name",
      "type_name",
      "level_name"
    ],
    "parameter_names": [
      "Mark",
      "Flow"
    ]
  }
}
```

### `instance_id`

Optional opaque string using the same ADR-0003 routing semantics as CAP-0001/CAP-0002.

It is a Server routing concern and is removed before constructing the transport-neutral CAP-0003 request.

Only omitted/null means unspecified. Empty or whitespace strings remain explicit opaque values and are not trimmed or normalized.

### `document_id`

Required opaque string defined by ADR-0006.

Unlike CAP-0002 discovery, CAP-0003 requires the document guard because the caller is reusing element references from an earlier operation.

Required behavior:

- no active document -> `NO_ACTIVE_DOCUMENT`;
- explicit `document_id` matches the active open-document lifetime -> continue;
- explicit `document_id` does not match -> `DOCUMENT_CONTEXT_CHANGED`;
- never fall back to another open document;
- never treat an empty or whitespace string as omitted;
- never parse, normalize, or advertise the value as a GUID/UUID.

The guard is validated inside valid Revit execution context before any element is inspected.

### `element_refs`

Required array of **1 to 10** unique opaque strings.

Rules:

- order is significant and is preserved in the result;
- uniqueness is ordinal/case-sensitive string uniqueness;
- values are not trimmed, normalized, parsed, or exposed as Revit `ElementId` values;
- no UUID/GUID format is advertised;
- an arbitrary value that cannot resolve to an eligible live element is an item-level `not_found`, not a whole-batch failure;
- duplicate refs are `INVALID_INSPECTION` at the transport-neutral capability boundary.

The expected source is CAP-0002 `element_refs`, whose initial implementation is backed by Revit `Element.UniqueId`. Clients must continue treating the values as opaque.

CAP-0003 v1 inspects the non-`ElementType` element population intended to be returned by CAP-0002. A supplied reference that does not resolve to an eligible live target is reported as `not_found`.

### `projection`

Required closed object.

At least one of the following must be present and non-empty:

```text
fields
parameter_names
```

An empty projection, an empty supplied array, or an unsupported projection property is invalid.

#### `fields`

Optional array containing **1 to 5** unique values chosen only from:

```text
name
category_name
family_name
type_name
level_name
```

Each accepted field may appear at most once.

Field names are contract enums and are matched exactly; no aliases are accepted.

For a successfully resolved element:

- requested fields are always present in that element result;
- a requested field whose value is unavailable is returned as `null`;
- unrequested fields are omitted.

This distinction lets callers tell the difference between "not requested" and "requested but unavailable" without adding a second metadata structure.

#### Basic field semantics

`name`
: Revit element name when available.

`category_name`
: Revit category display name (`element.Category?.Name`) when available.

`family_name`
: family name from resolved element type metadata when available, using the same semantics as CAP-0002 family filtering.

`type_name`
: resolved `ElementType.Name` when available, using the same semantics as CAP-0002 type filtering.

`level_name`
: directly associated/resolvable level name using the same CAP-0002 rule (`element.LevelId -> Level.Name`). No geometry, bounding-box, room, host, elevation, or custom-parameter inference is allowed.

The basic field semantics must remain consistent with CAP-0002 so an element that matched a CAP-0002 family/type/level filter does not receive a materially different interpretation during inspection.

#### `parameter_names`

Optional array of **1 to 10** parameter display names.

Each name:

- contains 1 to 256 characters;
- is compared using `OrdinalIgnoreCase` semantics;
- is not trimmed or otherwise normalized;
- must be unique within the request under `OrdinalIgnoreCase` comparison.

Parameter names are intentionally Revit display names in CAP-0003 v1 and can therefore be localized. They are a read-oriented convenience, not a durable parameter identity contract for future writes.

A stable language-independent parameter-addressing model is explicitly deferred.

## Visible parameter semantics

CAP-0003 inspects only parameters visible through Revit's ordered parameter surface for:

1. the target element itself (`source = instance`);
2. the target element's resolved `ElementType`, when one exists (`source = type`).

The implementation should use Revit `Element.GetOrderedParameters()` or an equivalent API that preserves the accepted visible-parameter semantics.

CAP-0003 must not use `LookupParameter(name)` as a single-result shortcut because Revit may expose multiple parameters with the same display name.

For each requested parameter name, every matching visible parameter is eligible for return until the per-element parameter-entry cap is reached.

If the same display name exists more than once, CAP-0003 returns the matching entries rather than arbitrarily selecting the first one.

No parameter GUID, numeric id, built-in parameter id, definition id, or other would-be write identity is exposed in CAP-0003 v1.

### Parameter ordering

For each resolved element, parameter entries are produced deterministically in this order:

1. requested `parameter_names` order;
2. for each requested name, matching `instance` entries before matching `type` entries;
3. within one source/name group, the order returned by the Revit visible ordered-parameter API.

Requested parameter names are unique, so the same request name is not processed twice.

### Parameter-entry bound

Return at most **20** parameter entries per resolved element.

When more than 20 matching visible entries exist:

```text
parameters_truncated = true
```

and the first 20 entries according to the deterministic ordering above are returned.

Otherwise:

```text
parameters_truncated = false
```

If `parameter_names` was requested but none match, return:

```json
{
  "parameters": [],
  "parameters_truncated": false
}
```

If `parameter_names` was not requested, both `parameters` and `parameters_truncated` are omitted.

## Parameter result shape

Each returned parameter entry is exactly:

```json
{
  "name": "Flow",
  "source": "instance",
  "value_text": "850 CFM",
  "value_truncated": false
}
```

Required fields:

```text
name: string
source: "instance" | "type"
value_text: string | null
value_truncated: boolean
```

No raw storage value, parameter id/GUID, unit id, definition object, writeability flag, or parameter object is returned.

### `value_text`

`value_text` is a bounded human-readable inspection value, not a machine-unit contract.

Required conversion intent:

- parameter has no assigned value -> `null`;
- string storage -> use the stored string representation exactly;
- Integer and Double storage -> prefer Revit's formatted `AsValueString()` when Revit supplies a usable formatted value;
- Integer storage with no formatted value -> invariant integer text is an acceptable fallback;
- Double storage with no safe formatted representation -> `null`; never expose `AsDouble()` or a raw Revit internal-unit double as `value_text`;
- ElementId storage -> resolve to the referenced element's display name in the active document when possible; otherwise `null`;
- never expose the numeric ElementId, `ElementId.Value`, or `ElementId.ToString()` as `value_text`;
- unsupported/unrepresentable value -> `null`.

This deliberately avoids making an unqualified raw Revit double part of the agent contract. Future quantitative/analytical capabilities should define explicit machine-readable unit semantics rather than asking the LLM to infer them from internal Revit units.

### Value-length bound

A non-null `value_text` may contain at most **512 characters**.

If the source value exceeds that bound:

- return a valid truncated prefix not exceeding 512 characters;
- set `value_truncated = true`.

Otherwise set `value_truncated = false`.

`value_truncated` reports truncation performed by RevitMCP. It does not claim that Revit's own formatted display string is a lossless representation of the underlying parameter storage.

## Transport-neutral request

Conceptually:

```text
GetElementsRequest
{
    document_id: string
    element_refs: string[1..10]
    projection
      fields?: GetElementField[]
      parameter_names?: string[1..10]
}
```

It contains no:

- `instance_id`;
- MCP request metadata;
- model-provider metadata;
- pipe/process/session identifiers;
- file paths;
- agent-supplied execution timeout.

## Result contract

Conceptually:

```text
GetElementsResult
{
    context
      instance_id
      document_id
    elements[]
}
```

Exact top-level shape:

```json
{
  "context": {
    "instance_id": "opaque-instance-id",
    "document_id": "opaque-document-id"
  },
  "elements": [
    {
      "element_ref": "opaque-element-ref-1",
      "status": "ok",
      "name": "VAV Box 12",
      "category_name": "Mechanical Equipment",
      "family_name": "VAV Box",
      "type_name": "VAV-6in",
      "level_name": "L2",
      "parameters": [
        {
          "name": "Flow",
          "source": "instance",
          "value_text": "850 CFM",
          "value_truncated": false
        }
      ],
      "parameters_truncated": false
    },
    {
      "element_ref": "opaque-element-ref-2",
      "status": "not_found"
    }
  ]
}
```

The concrete result contains only the projected fields requested by the caller. The example above illustrates all basic fields for readability; an implementation must not return unrequested projection fields.

### `context`

Required:

```text
instance_id: string
document_id: string
```

Both values remain opaque.

### `elements`

Required array with exactly one result item per requested `element_ref`, in the same order as the request.

Because the input contains 1 to 10 unique refs, the result contains 1 to 10 items.

No top-level result count is required; the input/result positional correspondence is sufficient and avoids redundant payload.

## Per-element status

Accepted statuses are only:

```text
ok
not_found
```

### `ok`

The ref resolved to an eligible live element in the guarded active document.

Required fields:

```text
element_ref
status = "ok"
```

Then:

- every requested basic field is included, nullable when unavailable;
- unrequested basic fields are omitted;
- if parameters were requested, `parameters` and `parameters_truncated` are included;
- otherwise parameter result fields are omitted.

### `not_found`

The ref could not resolve to an eligible live CAP-0003 target in the guarded active document at inspection time.

Exact item shape:

```json
{
  "element_ref": "opaque-element-ref",
  "status": "not_found"
}
```

No projected fields or parameter fields are returned for a `not_found` item.

This is a normal partial-success condition. For example, an element may have been deleted after CAP-0002 returned its ref.

A malformed/arbitrary opaque ref that cannot be resolved is also `not_found`; it does not expose internal UniqueId parsing rules.

## Batch partial-success rule

One missing element must not cause the entire batch to fail.

Example:

```text
requested refs: A, B, C
A exists
B was deleted
C exists
```

returns:

```text
A -> ok
B -> not_found
C -> ok
```

in the same order.

Top-level capability failure is reserved for request/context/execution failures that prevent the batch from being interpreted safely.

## Revit execution behavior

All Autodesk Revit API access must execute through EXEC-0001:

```text
Bridge background thread
-> Addin CAP-0003 service
-> RevitExecutionDispatcher.EnqueueAsync
-> ExternalEvent
-> UIApplication / active Document
-> validate document_id
-> resolve refs
-> project requested fields/parameters
-> GetElementsResult
```

No Revit API object may escape through `RevitMCP.Contracts`.

No `Transaction`, `SubTransaction`, or `TransactionGroup` is created. CAP-0003 is read-only.

### Element lookup

The initial implementation may resolve each opaque ref using `Document.GetElement(string)` because ADR-0006 currently backs `element_ref` with `Element.UniqueId`.

The Addin owns this representation knowledge. Contracts, Bridge, Server, clients, and agents must not parse or depend on the representation.

Expected missing/invalid-ref lookup outcomes become item-level `not_found`. Unexpected Revit API failures become normal capability execution failures; do not hide arbitrary exceptions as missing elements.

## Validation

Transport-neutral CAP-0003 validation must reject with `INVALID_INSPECTION` when, at minimum:

- `document_id` is absent at a layer where the transport-neutral request requires it;
- `element_refs` is null, empty, contains more than 10 refs, or contains ordinal duplicates;
- `projection` is absent;
- neither `fields` nor `parameter_names` is supplied with at least one value;
- a supplied projection array is empty;
- `fields` contains an unsupported/undefined value or duplicate;
- `parameter_names` contains more than 10 names;
- a parameter name is null, empty, longer than 256 characters, or duplicated under `OrdinalIgnoreCase`.

Do not create a generic validation framework.

At the MCP boundary, malformed JSON types, missing MCP-required fields, and unexpected closed-schema properties use the existing Server `INVALID_REQUEST` policy. `INVALID_INSPECTION` remains capability/business validation available to Bridge callers as well.

## Errors

### Server routing

Existing deterministic instance-routing codes remain:

```text
NO_REVIT_INSTANCE
INSTANCE_REQUIRED
INSTANCE_NOT_FOUND
INSTANCE_UNAVAILABLE
```

### Capability

```text
NO_ACTIVE_DOCUMENT
DOCUMENT_CONTEXT_CHANGED
INVALID_INSPECTION
REVIT_EXECUTION_TIMEOUT
REVIT_EXECUTION_FAILED
```

`not_found` is not a top-level error code; it is an item status.

No alternate document or Revit instance is retried after an explicit context/target failure.

Errors remain compact and must not expose stack traces, file paths, usernames, pipe names, raw Revit objects, or implementation details.

## MCP result policy

For MCP revisions supporting structured output:

```text
isError = false
structuredContent = GetElementsResult
content = []
```

Do not duplicate the structured payload as text.

Errors follow the established Server policy:

```text
isError = true
structuredContent absent
one compact JSON TextContent block
```

Legacy compatibility remains the official SDK behavior already established for SERVER-0001/0002; no custom MCP protocol fork is introduced.

## Context-size and performance requirements

CAP-0003 is bounded independently of total Revit model size:

- maximum 10 inspected element refs per call;
- maximum 5 basic field selectors;
- maximum 10 requested parameter names;
- maximum 20 returned parameter entries per resolved element;
- maximum 512 characters per non-null parameter `value_text`;
- no geometry, full parameter dump, or broad model enumeration;
- unrequested fields are omitted.

The implementation should resolve shared type metadata efficiently within one execution when multiple target elements reference the same type, but this must remain a local optimization rather than a persistent model cache.

No hard latency/byte SLA is accepted yet. Live validation must record representative UTF-8 structured-content sizes for basic-only, parameter-only, mixed, and partial-not-found results.

## Security and data minimization

Normal CAP-0003 results must not expose unless explicitly added by a future accepted capability:

- document/file/central paths;
- username or account identity;
- cloud project/model ids;
- process/session/pipe metadata;
- numeric Revit `ElementId` values;
- parameter ids/GUIDs;
- raw internal-unit doubles;
- arbitrary .NET/Revit class names;
- geometry/bounding boxes/location;
- worksets/phases/design options;
- ownership/editability data;
- diagnostics or exception stacks.

Read-only parameter inspection must not be treated as establishing a safe future parameter-write identity.

## Live validation expectations

Before CAP-0003 is considered implemented, validate the complete path on Autodesk Revit 2026.5 using a modeled Autodesk sample such as Snowdon Towers Sample HVAC:

```text
official MCP client
-> stdio RevitMCP.Server
-> revit_query_elements
-> collect document_id + refs
-> revit_get_elements
-> Bridge protocol v4
-> EXEC-0001 / ExternalEvent
-> Revit API
-> projected structured result
```

Required evidence should include:

- query one known category and inspect at least two returned refs;
- basic-fields-only projection;
- parameter-only projection using names observed in the sample;
- mixed basic + parameter projection;
- duplicate visible parameter-name behavior if a safe sample case is available, otherwise automated coverage of ordering logic;
- one unknown/non-resolving ref in a mixed batch returning `not_found` while valid refs remain `ok`;
- required `document_id` retry succeeds on the same active document;
- switching to another active document causes the old id to fail with `DOCUMENT_CONTEXT_CHANGED`;
- a value longer than the result bound is covered by automated tests even if a convenient live sample value is unavailable;
- modern MCP success uses structuredContent with empty content;
- CAP-0001 and CAP-0002 regressions pass against the same v4 host;
- structured-content byte sizes are recorded;
- no model writes or Revit transactions occur.

Do not use a production/client model for validation.

## Acceptance criteria

CAP-0003 is accepted as implemented when:

1. exactly 1..10 unique opaque `element_ref` values can be inspected per call;
2. `document_id` is required and validated against the active open-document lifetime before inspection;
3. result item order exactly matches request ref order;
4. missing/deleted refs return item-level `not_found` without failing valid siblings;
5. basic projection is limited to the five accepted fields;
6. requested available/unavailable fields are distinguishable through present value vs `null`, while unrequested fields are omitted;
7. parameter projection accepts at most 10 unique display names and never becomes an all-parameters mode;
8. only visible target-element/type parameters participate;
9. duplicate parameter display names are not silently collapsed to one value;
10. parameter entries identify `source = instance | type` but expose no durable write identity;
11. at most 20 parameter entries are returned per element with explicit truncation metadata;
12. parameter value text is bounded to 512 characters with explicit value-level truncation;
13. raw internal-unit doubles and numeric ElementId parameter fallbacks are not exposed;
14. all Revit API work executes through EXEC-0001;
15. no Revit transaction/model write occurs;
16. result and error shapes remain deterministic and token-efficient;
17. automated tests cover validation, projection, ordering, duplicate-name handling, partial not-found, value formatting/truncation, identity guard, timeout/cancellation, and dependency boundaries;
18. live Revit 2026.5 validation proves the complete MCP -> Server -> Bridge -> Addin -> Revit path;
19. CAP-0001 and CAP-0002 remain functional on the new bridge protocol version.

## Explicitly deferred

- all-parameter/full element dumps;
- parameter discovery/list-all-names mode;
- language-independent parameter addressing;
- parameter ids, GUIDs, and future write-target identity;
- raw machine-readable quantities and unit-conversion contracts;
- geometry, bounding boxes, and locations;
- connectors and MEP network topology;
- materials;
- worksets, phases, design options, ownership, and editability;
- linked-document element addressing;
- subelements;
- inactive-document targeting;
- pagination;
- write operations.

## References

- ADR-0005: Agent context and token efficiency
- ADR-0006: Document and element reference identity
- CAP-0002: `revit_query_elements`
- EXEC-0001: serialized Revit execution dispatcher
- Autodesk Revit API `Element.UniqueId`: https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/f9a9cb77-6913-6d41-ecf5-4398a24e8ff8.htm
- Autodesk Revit API `Element.GetOrderedParameters()`: https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/4bf4c0da-f841-0943-f9e0-246a666c1775.htm
- Autodesk Revit API `Element.GetParameters(String)`: https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/0cf342ef-c64f-b0b7-cbec-da8f3428a7dc.htm
- Autodesk Revit API `Parameter.AsValueString()`: https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/5015755d-ee80-9d74-68d9-55effc60ed0c.htm
