# CAP-0005: `revit_get_parameter_values`

- Status: Accepted
- Operation class: Read
- Date: 2026-09-21

## Purpose

`revit_get_parameter_values` reads typed values for a bounded explicit set of known `element_ref + parameter_ref` pairs in the active Revit document.

It consumes the opaque parameter identity established by CAP-0004. It must not fall back to parameter display-name matching.

It is the typed-read half of the existing read workflow:

```text
revit_query_elements
→ element_refs

revit_describe_parameters
→ parameter_refs + data_type semantics

revit_get_parameter_values
→ typed values for explicit pairs
```

CAP-0003 remains the lightweight human-readable inspection capability. CAP-0005 does not replace it.

Typical agent use cases:

- read a small set of already-discovered parameters after CAP-0004;
- distinguish duplicate display names by using `parameter_ref` rather than `LookupParameter(name)`;
- obtain a machine-readable quantity with an exact Revit unit identifier;
- tolerate missing elements or stale parameter refs without failing an otherwise useful batch.

CAP-0005 is strictly read-only. It does not authorize writes.

## MCP tool

### Name

`revit_get_parameter_values`

### Title

`Get Revit Parameter Values`

### Description

Read typed values for a bounded explicit set of known Revit element and parameter references in the active document.

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
  "reads": [
    {
      "element_ref": "opaque-element-ref",
      "parameter_ref": "opaque-parameter-ref"
    }
  ]
}
```

### `instance_id`

Optional Server routing value.

Same semantics as CAP-0001/2/3/4.

It must not enter the transport-neutral capability request.

### `document_id`

Required opaque string defined by ADR-0006.

Same active-document guard used by CAP-0003 and CAP-0004.

Required behavior:

- absent/null at a layer where the field is required -> `INVALID_PARAMETER_READ`;
- no active document -> `NO_ACTIVE_DOCUMENT`;
- a present string is opaque and is never trimmed, normalized, parsed, or treated as omitted;
- exact match against the active open-document lifetime -> continue;
- exact mismatch, including empty or whitespace strings, -> `DOCUMENT_CONTEXT_CHANGED`;
- never fall back to another open document.

### `reads`

Required array of **1..50** explicit pairs.

Each pair:

```text
element_ref: required opaque string
parameter_ref: required opaque string
```

Each pair must contain non-null string fields. String content remains opaque and is not trimmed, normalized, or parsed.

Pairs must be unique using ordinal string equality on the raw strings.

An arbitrary, empty, or whitespace `element_ref` that cannot resolve becomes item-level `element_not_found`.

An arbitrary, empty, or whitespace `parameter_ref` not known to the document-lifetime reverse map becomes item-level `parameter_ref_not_found`.

The same `element_ref` may appear with several parameter refs.

The same `parameter_ref` may appear with several element refs.

Do not use a Cartesian `element_refs[] × parameter_refs[]` request model.

Clients must not construct, parse, or manufacture either ref.

The expected source of `element_ref` is `revit_query_elements`.

The expected source of `parameter_ref` is `revit_describe_parameters`.

## Parameter resolution

`parameter_ref` remains:

- opaque;
- document-scoped;
- source-bound (`instance` or `type`);
- meaningful only during the matching open-document lifetime;
- owned and resolved by the Revit Addin;
- never parsed or manufactured by clients.

A `parameter_ref` identifies a CAP-0004 parameter definition/source binding, not one stored value occurrence by itself. CAP-0005 therefore requires the explicit pair: which element, which already-identified definition/source.

CAP-0005 must resolve `parameter_ref` through the Addin-owned parameter identity service established for CAP-0004.

The current service mints a stable document-lifetime ref from definition identity plus source. The future implementation may extend that service with reverse lookup data. It must not persist or return Revit `Parameter` API objects.

Resolution must re-find the current visible parameter on the relevant instance or type surface using the accepted CAP-0004 identity semantics:

```text
built-in API identity
or shared GUID
or local document-scoped identity
+
source = instance | type
```

Do not infer identity by parsing ForgeTypeId namespace or version strings.

No `LookupParameter(name)` identity fallback.

No display-name equality.

No accepting built-in ForgeTypeId or shared GUID as the operational address in v1.

## Result

Top-level context:

```text
instance_id
document_id
```

Return exactly one result item per requested pair, in request order.

Statuses:

```text
ok
element_not_found
parameter_ref_not_found
parameter_not_present
unsupported_value
```

Meaning:

- `element_not_found`: the requested element cannot be resolved, or the resolved object is an `ElementType` input, matching CAP-0003/CAP-0004;
- `parameter_ref_not_found`: the opaque parameter ref is not known for this active document lifetime;
- `parameter_not_present`: the ref is valid, but its source/definition is not present on that element or resolved type;
- `unsupported_value`: the parameter exists and has a value, but CAP-0005 cannot expose it safely under the accepted typed-value contract.

Missing or unknown individual refs are item statuses. They do not fail the whole batch.

### `ok` item

```text
element_ref
parameter_ref
status
data_type
has_value
value?   // present only when has_value=true
```

`data_type` reuses the CAP-0004 classification. Do not invent a second model.

Accepted `data_type.kind` values remain:

```text
measurable_spec
spec
category
unknown
```

Classification uses `Definition.GetDataType()` with `UnitUtils.IsMeasurableSpec`, `Category.IsBuiltInCategory`, and `SpecUtils.IsSpec`. `forge_type_id` is present when Revit supplied a non-empty data-type identifier and omitted when Revit reports an empty data type.

### Non-`ok` item

```text
element_ref
parameter_ref
status
```

No `data_type`, `has_value`, or `value`.

Top-level and nested objects are closed.

## Typed value variants

Closed discriminated variants. The discriminator is `kind`.

### `string`

For Revit `StorageType.String` when `HasValue == true`.

```text
kind = string
value: string
truncated: bool
```

Use `Parameter.AsString()`.

Bound non-null string values to **512** characters using the same explicit truncation philosophy as CAP-0003:

- if the stored string exceeds 512 characters, return a valid prefix of at most 512 characters and set `truncated = true`;
- otherwise set `truncated = false`.

`truncated` reports truncation performed by RevitMCP.

A null `AsString()` result for a valued string parameter is `unsupported_value`.

### `integer`

For Revit `StorageType.Integer` when `HasValue == true`.

```text
kind = integer
value: JSON integer
```

Use `Parameter.AsInteger()` exactly.

Do not reinterpret integer parameters as Boolean or enum values, including Yes/No parameters. The accompanying CAP-0004-style `data_type` remains available for semantics.

### `quantity`

Only for safe measurable Double parameters.

```text
kind = quantity
value: finite JSON number
unit_type_id: exact Revit ForgeTypeId string
```

Required sequence:

1. `StorageType` is `Double` and `HasValue == true`;
2. `Definition.GetDataType()` identifies a measurable spec using `UnitUtils.IsMeasurableSpec`;
3. obtain the Revit unit quantifying the parameter value with `Parameter.GetUnitTypeId()`;
4. verify that identifier with `UnitUtils.IsUnit` and that it is valid for the spec with `UnitUtils.IsValidUnit`;
5. convert `Parameter.AsDouble()` from Revit internal units using `UnitUtils.ConvertFromInternalUnits`;
6. expose the converted value, never the raw internal-unit double;
7. preserve the exact ForgeTypeId string supplied by Revit (`ForgeTypeId.TypeId` or equivalent).

Do not parse ForgeTypeId namespace or version strings to infer semantics.

CAP-0005 v1 does not accept a caller-selected output unit.

If any step fails, including a non-finite converted number or `GetUnitTypeId()` throwing because the parameter is not of value type, return item status `unsupported_value`.

Do not invent a generic raw-number fallback in v1.

Non-measurable Double parameters are `unsupported_value`.

### `element_reference`

For Revit `StorageType.ElementId` when `HasValue == true`.

```text
kind = element_reference
resolved: bool
name?: bounded string
element_ref?: opaque string
```

Rules:

- never expose numeric `ElementId`, `ElementId.Value`, or `ElementId.ToString()`;
- resolve the referenced Revit object in the active document when possible;
- if the referenced target is compatible with the existing opaque `element_ref` contract (the same UniqueId-based, non-`ElementType` handle used by CAP-0002/0003/0004), `element_ref` may be returned;
- do not fabricate an `element_ref` for `ElementType` or special/sentinel `ElementId` values merely to make them chainable;
- `resolved = false` is valid when no safe target can be resolved;
- any returned display name is a prefix of at most 512 characters. Name truncation is not a separate flag in v1.

`resolved = true` means a Revit object was found. An `ElementType` target may be `resolved = true` with a bounded `name` and no `element_ref`.

## No-value semantics

`Parameter.HasValue == false` is not an error.

Return:

```text
status = ok
has_value = false
```

and omit `value`.

`data_type` remains present because the definition was found.

## Document guard and execution

Follow the existing capability pattern:

1. validate the transport-neutral request before Revit execution;
2. execute Revit API access only through EXEC-0001;
3. require an active document;
4. validate exact `document_id` before resolving requested items;
5. resolve each explicit read in request order;
6. create no transaction.

Per-item resolution after the document guard:

```text
reverse-lookup parameter_ref in this document lifetime
    unknown → parameter_ref_not_found

resolve element_ref without parsing UniqueId
    missing / ElementType input → element_not_found

select the source surface bound to the parameter_ref
    instance → element.GetOrderedParameters()
    type → resolve ElementType, then type.GetOrderedParameters()
    type surface unavailable → parameter_not_present

re-find the visible parameter by CAP-0004 identity + source
    not present → parameter_not_present

HasValue == false → ok, has_value = false
else emit the matching typed variant or unsupported_value
```

Document mismatch must return `DOCUMENT_CONTEXT_CHANGED` with no fallback onto another document.

No Revit API object escapes into Contracts.

## Transport-neutral request

Conceptually:

```text
GetParameterValuesRequest
{
    document_id
    reads[1..50]
      element_ref
      parameter_ref
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
- caller-selected unit;
- display names used as identity.

Transport-neutral validation must reject with `INVALID_PARAMETER_READ` when, at minimum:

- `document_id` is absent or null;
- `reads` is missing, empty, or longer than 50;
- any pair is missing a non-null `element_ref` or `parameter_ref` string field;
- pairs are not unique under ordinal equality of the raw strings.

Do not treat empty or whitespace `document_id`, `element_ref`, or `parameter_ref` strings as omitted or as a special representation. Those present strings remain opaque and are handled by the document guard or item statuses.

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
INVALID_PARAMETER_READ
REVIT_EXECUTION_TIMEOUT
REVIT_EXECUTION_FAILED
```

MCP-boundary malformed requests remain future Server `INVALID_REQUEST`, consistent with existing tools.

Expected missing or unknown individual element or parameter refs remain item statuses rather than whole-batch failures.

## Bounds / context efficiency

This capability satisfies ADR-0005 by returning the smallest deterministic payload that fulfills an explicit typed read:

- 1..50 explicit read pairs, independent of model size;
- no Cartesian expansion;
- exactly one result item per pair;
- string and name bounds of 512 characters;
- no all-parameter mode;
- no whole-document scan;
- no parameter metadata dump beyond `data_type` needed to interpret the typed value;
- compact item statuses instead of batch failure for expected misses;
- no formatted `AsValueString()` duplicate of a typed quantity;
- no raw internal-unit doubles.

Batching related explicit pairs in one request reduces round-trips without creating a generic mega-tool or an unbounded catalog.

## Context / security boundaries

CAP-0005 does not return:

- raw internal-unit doubles;
- numeric durable `ElementId` or `Parameter.Id`;
- formulas;
- geometry, connectors, or paths;
- usernames or cloud/project identifiers;
- hidden/API-only parameters;
- writeability or authorization decisions;
- full Revit parameter objects.

`readOnlyHint` is not a write-authorization substitute.

## MCP success policy

Modern successful result:

```text
isError = false
structuredContent = GetParameterValuesResult
content = []
```

Errors:

```text
isError = true
structuredContent absent
one compact JSON TextContent error
```

The official MCP tool is specified here. Do not register it until BRIDGE-0006 is designed, implemented, and live-tested through the typed Bridge, then SERVER-0005 is designed and implemented.

## Compatibility

RevitMCP continues to target Revit 2025, 2026, and 2027 from one shared Addin project. The APIs used here (`StorageType`, `HasValue`, `GetDataType`, `UnitUtils`, `GetUnitTypeId`, `ConvertFromInternalUnits`, and the typed accessors) belong to that supported matrix.

This specification does not claim live CAP-0005 validation on Revit 2025, 2026, or 2027. Those remain future implementation and validation work.

Do not treat compile-time availability as live proof.

## Acceptance criteria

CAP-0005 is acceptable as a capability contract when:

1. It is one coherent read-only capability.
2. `document_id` is required as a present string. Absent/null is `INVALID_PARAMETER_READ`. A present string is opaque, is never trimmed or normalized, and is compared exactly to the active document id. Empty or whitespace therefore yields `DOCUMENT_CONTEXT_CHANGED`.
3. `reads` contains 1..50 unique explicit `element_ref + parameter_ref` pairs.
4. Pair uniqueness uses ordinal equality on the raw strings. Ref content is opaque and is not trimmed or normalized.
5. The request is not a Cartesian product of element and parameter arrays.
6. `instance_id` remains Server routing-only.
7. `parameter_ref` is resolved through the CAP-0004 Addin identity service, with optional reverse-lookup extension only.
8. Resolution re-finds the current visible parameter by CAP-0004 identity and source.
9. No `LookupParameter(name)` or display-name identity fallback is used.
10. Result order matches request order with exactly one item per pair.
11. Item statuses are `ok`, `element_not_found`, `parameter_ref_not_found`, `parameter_not_present`, and `unsupported_value` with the meanings above. Arbitrary, empty, or whitespace `element_ref` values that cannot resolve are `element_not_found`. Arbitrary, empty, or whitespace `parameter_ref` values unknown to the document-lifetime reverse map are `parameter_ref_not_found`.
12. `ok` items include CAP-0004-style `data_type`, `has_value`, and `value` only when `has_value` is true.
13. `HasValue == false` is `ok` with `has_value = false` and omitted `value`.
14. String values use `AsString()` and are bounded to 512 characters with explicit truncation.
15. Integer values use `AsInteger()` exactly and are not reinterpreted as Boolean or enum.
16. Quantity values are emitted only for measurable Double parameters after `IsMeasurableSpec`, valid unit checks, and `ConvertFromInternalUnits`.
17. Quantity `unit_type_id` is the exact Revit ForgeTypeId string; no ForgeTypeId parsing.
18. Non-measurable or otherwise unsafe Double values are `unsupported_value`, not a raw-number fallback.
19. ElementId values never expose a numeric id; `element_ref` is returned only for targets compatible with the existing handle contract.
20. `DOCUMENT_CONTEXT_CHANGED` is a top-level capability error with no document fallback.
21. All Revit API access executes through EXEC-0001.
22. No Revit transaction is created.
23. Contracts remain transport-neutral.
24. Server remains Revit-API independent.
25. Revit 2025, 2026, and 2027 variants compile in CI when implemented.
26. Automated tests will cover pair uniqueness, identity reverse lookup, each item status, each value variant, no-value, unit conversion failure, document guard, and dependency boundaries.
27. Official MCP live validation is a later SERVER-0005 gate, not part of this specification PR.
28. CAP-0001 through CAP-0004 remain unchanged by this specification.

## Explicitly deferred

- caller-selected unit conversion;
- non-measurable Double / raw-number exposure;
- typed parameter filtering;
- accepting built-in ForgeTypeId or shared GUID directly instead of `parameter_ref`;
- hidden/API-only parameters;
- FamilyManager-wide family parameter reads;
- linked-document parameters;
- cross-document or persistent `parameter_ref` values;
- writes;
- write authorization;
- write preview/approval;
- stale-write protection;
- transaction/audit design;
- Boolean/enum reinterpretation of integer storage;
- pagination;
- all-parameter or whole-document value dumps.

## Bridge / Server sequencing

CAP-0005 itself is transport-neutral.

Expected future implementation sequence:

```text
CAP-0005 accepted
-> BRIDGE-0006 design/implementation
-> typed Bridge live validation
-> SERVER-0005 design/implementation
-> official MCP live validation
```

Do not create BRIDGE-0006 or SERVER-0005 in this specification.

Do not implement a later protocol version by numeric `>=` behavior. Existing capabilities must be explicitly preserved on any new protocol version.

## References

- ADR-0005: Agent context and token efficiency
- ADR-0006: Document and element reference identity
- ADR-0007: Federated MCP boundaries and optional orchestration
- CAP-0003: `revit_get_elements`
- CAP-0004: `revit_describe_parameters`
- EXEC-0001: serialized Revit execution dispatcher
- Autodesk Revit API `Parameter` class: https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/333ff41b-e6a7-d959-60bf-c3bfae495581.htm
- Autodesk Revit API `Parameter.AsDouble()`: https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/8831936d-965b-ec90-7e96-b2933c80b88e.htm
- Autodesk Revit API `Definition.GetDataType()`: https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/1c008d27-9e61-362c-308c-8b718ee0f8df.htm
- Autodesk Revit API `Element.GetOrderedParameters()`: https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/4bf4c0da-f841-0943-f9e0-246a666c1775.htm
