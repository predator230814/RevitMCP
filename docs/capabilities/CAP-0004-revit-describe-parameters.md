# CAP-0004: `revit_describe_parameters`

- Status: Accepted
- Operation class: Read
- Date: 2026-09-11

## Purpose

`revit_describe_parameters` discovers and describes the visible parameter definitions available on a small bounded set of known Revit elements.

It does not return parameter values.

It establishes a parameter-addressing and semantic layer between:

```text
revit_query_elements
→ element_refs

revit_describe_parameters
→ parameter_refs + parameter semantics

future:
→ typed parameter reads
→ BIM audits
→ controlled parameter writes
```

CAP-0003 remains the lightweight human-readable inspection capability.

CAP-0004 establishes parameter identity.

Typical agent use cases:

- discover which visible instance/type parameters exist on a small CAP-0002 result set;
- distinguish duplicate display names that belong to different definitions;
- obtain a document-scoped `parameter_ref` for later typed reads or writes;
- observe presence and read-only counts without treating them as authorization.

## MCP tool

### Name

`revit_describe_parameters`

### Title

`Describe Revit Parameters`

### Description

Discover visible parameter definitions on a bounded set of known Revit element references and return opaque parameter identity plus data-type semantics, without parameter values.

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
  "element_refs": [
    "opaque-element-ref-1",
    "opaque-element-ref-2"
  ],
  "source": "both",
  "name_contains": "Flow",
  "limit": 50
}
```

### `instance_id`

Optional Server routing value.

Same semantics as CAP-0001/2/3.

It must not enter the transport-neutral capability request.

### `document_id`

Required.

Same ADR-0006 active-document guard used by CAP-0003.

A mismatch returns `DOCUMENT_CONTEXT_CHANGED`.

No fallback to another document.

### `element_refs`

Required array of 1..10 unique opaque refs.

Same document/ref semantics as CAP-0003.

Order is preserved in the minimal element-status result.

The expected source is `revit_query_elements`.

Clients must not construct or parse Revit UniqueIds.

### `source`

Optional enum:

```text
instance
type
both
```

Default: `both`.

`both` inspects both surfaces independently.

Result descriptors always contain `source = instance` or `source = type`. Never `both`.

### `name_contains`

Optional string, 1..256 characters.

Case-insensitive substring filter against the Revit-visible parameter display name.

No trimming or normalization.

This is only a discovery convenience. It is not parameter identity.

### `limit`

Optional integer.

```text
default: 50
minimum: 1
maximum: 100
```

The limit applies to unique returned parameter descriptors after aggregation, not raw parameter occurrences.

## Parameter discovery surface

CAP-0004 uses the same visible parameter philosophy as CAP-0003.

For every successfully resolved input element:

```text
instance source
→ element.GetOrderedParameters()

type source
→ resolve ElementType
→ type.GetOrderedParameters()
```

Only Revit-visible parameters are discovered.

CAP-0004 does not enumerate hidden/API-only parameters.

It does not use `LookupParameter()` as an identity mechanism.

## `parameter_ref`

Every returned parameter descriptor has:

```text
parameter_ref: string
```

`parameter_ref` is:

- opaque;
- document scoped;
- meaningful only with the matching `document_id`;
- stable for reuse while the corresponding parameter definition/source remains available in that open-document lifetime;
- owned and resolved by the Revit Addin;
- not an authorization token.

A `parameter_ref` identifies a parameter definition/source binding, not one parameter value occurrence on one element.

Therefore multiple elements that expose the same parameter definition in the same source aggregate under one `parameter_ref`.

The representation is an implementation detail.

The initial Addin may use Revit's parameter type identity internally.

Clients must not parse or manufacture `parameter_ref`.

No numeric Revit `ElementId` becomes part of the agent contract.

## Canonical identity

Every descriptor contains `identity`.

Identity has exactly one of three kinds:

```text
built_in
shared
local
```

### Built-in

```json
{
  "kind": "built_in",
  "parameter_type_id": "Autodesk ForgeTypeId string"
}
```

`parameter_type_id` comes from Revit's built-in parameter identity API.

It is language-independent metadata.

The exact ForgeTypeId string is returned as supplied by Revit.

RevitMCP does not manually parse its namespace or version suffix.

`parameter_type_id` is canonical metadata, not the normal operational parameter address.

Agents should reuse `parameter_ref` for capability chaining.

### Shared

```json
{
  "kind": "shared",
  "guid": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
}
```

The GUID comes from Revit's shared-parameter API.

This is intentionally exposed because shared-parameter GUID identity is designed to survive across documents using the same shared definition.

Formatting is canonical lowercase hyphenated GUID.

Normal capability chaining uses `parameter_ref`.

### Local

```json
{
  "kind": "local"
}
```

This covers non-built-in, non-shared document/family/project parameter definitions for which RevitMCP has no legitimate portable identity to expose.

RevitMCP must not invent a global identity for such parameters.

The opaque `parameter_ref + document_id` remains sufficient for operations inside the current open-document lifetime.

### Detection rule

Identity classification uses Revit API semantics.

Do not infer identity by parsing ForgeTypeId string prefixes.

Conceptually:

```text
built-in API identity available
    → built_in

else Parameter.IsShared
    → shared + Parameter.GUID

else
    → local
```

## Data type semantics

Every parameter descriptor contains `data_type`.

Conceptually:

```json
{
  "kind": "measurable_spec",
  "forge_type_id": "autodesk.spec..."
}
```

Accepted `kind` values:

```text
measurable_spec
spec
category
unknown
```

Classification is based on `Definition.GetDataType()` with Revit API classification helpers:

```text
empty data type
    → unknown

UnitUtils.IsMeasurableSpec(...)
    → measurable_spec

Category.IsBuiltInCategory(...)
    → category

SpecUtils.IsSpec(...)
    → spec

otherwise
    → unknown
```

`forge_type_id` is present when Revit supplied a non-empty data-type identifier.

It is omitted when Revit reports an empty data type.

CAP-0004 does not expose a numeric parameter storage value or typed parameter value.

## Units

CAP-0004 does not define machine-readable parameter values or unit conversion.

It does not return a numeric value or claim a unit/value pair.

Typed quantities are deferred to a later capability.

A raw `AsDouble()` must never become an agent-facing quantity without explicit units.

## Read-only observation

Each descriptor includes `read_only_on_count`.

This is the number of successfully resolved input elements for which the corresponding parameter occurrence reports `Parameter.IsReadOnly == true`.

This field is observational only.

`read_only_on_count == 0` does not authorize or guarantee a future write.

Future write capabilities must independently validate authorization, document state, parameter state, target validity, and transaction conditions.

CAP-0004 exposes no `writable=true` promise.

## Presence aggregation

Each descriptor contains `present_on_count`.

This is the number of successfully resolved input element refs for which that parameter definition/source is present.

For `source = type`, the count is still based on supplied element refs.

If ten supplied instances share one type containing the parameter, `present_on_count = 10`.

It is not the number of unique Revit ElementType objects.

For one element, a parameter definition/source contributes at most once to the count.

## Duplicate display names

Parameters are never grouped by `name`.

If Revit exposes built-in, shared, and local parameters that share the display name `Mark`, they remain separate descriptors when their identities differ.

Their `parameter_ref` values must be distinct.

This is a core CAP-0004 requirement.

## Result

Conceptually:

```json
{
  "context": {
    "instance_id": "opaque",
    "document_id": "opaque"
  },
  "elements": [
    {
      "element_ref": "ref-1",
      "status": "ok"
    },
    {
      "element_ref": "ref-2",
      "status": "not_found"
    }
  ],
  "matched_count": 12,
  "truncated": false,
  "parameters": [
    {
      "parameter_ref": "opaque-parameter-ref",
      "name": "Flow",
      "source": "instance",
      "identity": {
        "kind": "built_in",
        "parameter_type_id": "..."
      },
      "data_type": {
        "kind": "measurable_spec",
        "forge_type_id": "..."
      },
      "present_on_count": 1,
      "read_only_on_count": 0
    }
  ]
}
```

Top-level and nested objects are closed.

## Element resolution

`elements` contains exactly one minimal item per requested `element_ref`, in request order.

Statuses:

```text
ok
not_found
```

Exact shapes:

```json
{
  "element_ref": "...",
  "status": "ok"
}
```

or

```json
{
  "element_ref": "...",
  "status": "not_found"
}
```

An arbitrary/deleted/unresolvable ref is item-level `not_found`.

It does not fail discovery for valid refs.

No parameter descriptors are produced from that element.

`ElementType` objects resolved as the requested ref are `not_found`, matching CAP-0003.

## Parameter grouping

Parameter occurrences are aggregated by RevitMCP parameter identity and source, not name.

```text
parameter definition identity
+
instance/type source
=
one parameter_ref descriptor
```

Occurrences with the same display name but different definitions remain separate.

## Filtering, ordering and truncation

Required sequence:

```text
validate document guard
→ resolve element refs
→ collect requested visible instance/type parameter surfaces
→ establish parameter identity
→ aggregate occurrences
→ apply name_contains
→ deterministically sort
→ compute matched_count
→ take limit
→ set truncated
```

Deterministic descriptor ordering:

```text
1. name, OrdinalIgnoreCase
2. name, Ordinal
3. source: instance before type
4. parameter_ref, Ordinal
```

`matched_count` is the number of unique descriptors after filtering but before `limit`.

```text
truncated = matched_count > parameters.length
```

No pagination in v1.

The recovery strategy when truncated is to narrow using `name_contains` or a smaller/more homogeneous element set.

## Transport-neutral request

Conceptually:

```text
DescribeParametersRequest
{
    document_id
    element_refs[1..10]
    source
    name_contains?
    limit
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
- agent-controlled timeout.

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
INVALID_PARAMETER_DISCOVERY
REVIT_EXECUTION_TIMEOUT
REVIT_EXECUTION_FAILED
```

MCP-boundary malformed requests:

```text
INVALID_REQUEST
```

Expected missing element refs remain item-level `not_found`.

## Revit execution

All Autodesk Revit API work executes through EXEC-0001.

No Revit API object escapes into Contracts.

CAP-0004 creates no `Transaction`, `SubTransaction`, or `TransactionGroup`.

The capability is strictly read-only.

## Context / security boundaries

CAP-0004 does not return:

- parameter values;
- raw doubles;
- numeric Revit ElementIds;
- Parameter.Id;
- InternalDefinition.Id;
- file paths;
- usernames;
- cloud/project ids;
- formulas;
- global-parameter associations;
- geometry;
- connectors;
- full Revit parameter objects;
- hidden/API-only parameters;
- authorization decisions.

Shared parameter GUIDs and built-in parameter ForgeTypeIds are intentional semantic identity metadata, not routing secrets.

## MCP success policy

Modern successful result:

```text
isError = false
structuredContent = DescribeParametersResult
content = []
```

Errors:

```text
isError = true
structuredContent absent
one compact JSON TextContent error
```

The official MCP tool is specified here. SERVER-0004 owns stdio registration after BRIDGE-0005 is implemented and live-tested through the typed Bridge.

## Acceptance criteria

CAP-0004 is acceptable when:

1. It is one coherent read-only capability.
2. `document_id` is mandatory.
3. `element_refs` contains 1..10 unique opaque refs.
4. Parameter discovery is bounded to the supplied elements.
5. Only visible Revit parameters are discovered.
6. Instance and type surfaces are explicitly distinguishable.
7. Display-name equality is never treated as parameter identity.
8. Duplicate names from distinct definitions remain distinct descriptors.
9. `parameter_ref` is opaque and document scoped.
10. `parameter_ref` represents definition/source identity, not one value occurrence.
11. Built-in parameters expose API-derived `parameter_type_id`.
12. Shared parameters expose the actual stable shared GUID.
13. Local parameters do not receive a fabricated portable identity.
14. Identity kind is determined through Revit APIs, not ForgeTypeId string parsing.
15. `Definition.GetDataType()` is projected into measurable_spec/spec/category/unknown semantics.
16. Empty/special data types are handled safely as `unknown`.
17. No parameter values or raw internal-unit doubles are returned.
18. `present_on_count` aggregates across supplied elements deterministically.
19. `read_only_on_count` reflects `Parameter.IsReadOnly` only and is not treated as authorization.
20. Missing/deleted element refs remain partial success through `not_found`.
21. Filtering is limited to optional display-name substring search in v1.
22. Default descriptor limit is 50 and maximum is 100.
23. `matched_count` is calculated before limiting.
24. Ordering and truncation are deterministic.
25. All Revit API access executes through EXEC-0001.
26. No Revit transaction is created.
27. Contracts remain transport neutral.
28. Server remains Revit-API independent.
29. Revit 2025, 2026 and 2027 variants compile in CI.
30. Automated tests cover built-in/shared/local identity, duplicate names, source handling, aggregation, datatype classification, partial not_found and bounds.
31. Live Revit 2026.5 validation demonstrates parameter discovery through the official MCP client.
32. CAP-0001, CAP-0002 and CAP-0003 continue working on the resulting Bridge protocol version.

## Explicitly deferred

- typed parameter values;
- machine-readable quantity values;
- unit conversion;
- parameter-value filtering;
- accepting GUID or ForgeTypeId directly as a parameter target;
- parameter writes;
- write authorization;
- write preview/approval;
- hidden/API-only parameter discovery;
- whole-document parameter catalog;
- FamilyManager-wide parameter catalog;
- linked-document parameters;
- Autodesk Parameters Service;
- cloud parameter identity;
- persistent `parameter_ref` across document close/reopen;
- arbitrary parameter metadata dumping.

## Bridge / Server sequencing

CAP-0004 itself is transport-neutral.

After this capability contract is accepted:

1. define BRIDGE-0005;
2. extend explicit Bridge protocol compatibility;
3. implement Addin + Contracts + Bridge;
4. live-test typed Bridge;
5. define SERVER-0004;
6. expose the fourth MCP tool;
7. validate official MCP client → stdio Server → Revit 2026.5.

Do not implement protocol v5 merely by numeric `>=` behavior.

Existing capabilities must be explicitly preserved on the new protocol version.

## References

- ADR-0005: Agent context and token efficiency
- ADR-0006: Document and element reference identity
- CAP-0003: `revit_get_elements`
- BRIDGE-0004: `revit.get_elements`
- EXEC-0001: serialized Revit execution dispatcher
