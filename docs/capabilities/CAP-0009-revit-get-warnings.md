# CAP-0009: `revit_get_warnings`

- Status: Accepted
- Operation class: Read
- Date: 2026-10-08
- Product Owner acceptance: Dave, 2026-10-09

## Authorization

This specification is **Accepted**. Dave, Product Owner, accepted it on 2026-10-09.

Acceptance does not implement the capability. No Addin, Bridge, Server, protocol-version, or test code is included. Implementation starts only through the later bridge and server specifications named below. User-interface work is outside this acceptance.

## Purpose

`revit_get_warnings` returns a bounded, deterministic view of the persistent failures posted on the active Revit document.

It is the warnings half of an existing read workflow:

```text
revit_get_warnings
→ failure_key + element_ref

revit_get_elements / revit_describe_parameters / revit_get_parameter_values
→ inspect the cited elements
```

Typical agent use cases:

- see which persistent warning definitions dominate the active document without receiving every posted message;
- narrow to one severity, one failure definition, or a few known elements;
- obtain opaque `element_ref` values that chain into CAP-0003, CAP-0004, and CAP-0005;
- repeat the same request and compare the result.

CAP-0009 is strictly read-only. It does not authorize writes, warning resolution, or warning deletion.

The source is the active document's persistent posted failures. That is the set Revit exposes through `Document.GetWarnings()`. It is not the transient warning or error dialog raised while a transaction is committing. That dialog remains the deferred CAP-0008 validation debt and is out of scope here.

## MCP tool

### Name

`revit_get_warnings`

### Title

`Get Revit Warnings`

### Description

Return a bounded, deterministic summary of persistent failures posted on the active Revit document.

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
  "severity": "warning",
  "failure_key": "opaque-failure-key",
  "element_refs": ["opaque-element-ref"],
  "max_warnings": 25,
  "max_elements_per_warning": 10
}
```

Required:

```text
document_id
```

All objects are closed. Unexpected properties are later Server `INVALID_REQUEST`.

### `instance_id`

Optional Server routing value.

Same semantics as CAP-0001 through CAP-0008.

It must not enter the transport-neutral capability request.

### `document_id`

Required opaque string defined by ADR-0006.

Same active-document guard used by CAP-0003 through CAP-0008.

Required behavior:

- absent/null at a layer where the field is required -> `INVALID_WARNINGS`;
- no active document -> `NO_ACTIVE_DOCUMENT`;
- a present string is opaque and is never trimmed, normalized, parsed, or treated as omitted;
- exact match against the active open-document lifetime -> continue;
- exact mismatch, including empty or whitespace strings, -> `DOCUMENT_CONTEXT_CHANGED`;
- never fall back to another open document.

Project documents, family documents, and read-only documents are all eligible. This capability does not require a writable document.

### `severity`

Optional closed enum:

```text
warning
error
document_corruption
other
```

Omitted `severity` means every persistent failure, whatever severity Revit reports.

A present value keeps only messages whose mapped severity equals that value.

### `failure_key`

Optional opaque string.

Omitted means every failure definition.

A present string, including empty or whitespace, is an exact ordinal match against the emitted `failure_key`. It is never trimmed, normalized, parsed, or treated as a wildcard.

### `element_refs`

Optional array of **1..10** opaque strings.

Omitted means no element filter.

Uniqueness uses ordinal equality on the raw strings.

String content remains opaque and is not trimmed, normalized, or parsed.

Empty or whitespace present strings are valid syntax. They normally match nothing.

Null, missing, or non-string items are invalid at the MCP boundary.

A present array restricts results to messages that cite at least one requested ref, using the match rule below.

An unknown ref does not fail the call.

### Bounds

Both bounds are optional integers with hard defaults and closed ranges.

| Field | Default | Min | Max |
| --- | ---: | ---: | ---: |
| `max_warnings` | 25 | 1 | 100 |
| `max_elements_per_warning` | 10 | 1 | 20 |

Omitted fields use the defaults.

A later transport-neutral validator must reject out-of-range present values as `INVALID_WARNINGS`.

Malformed MCP types or unexpected properties remain Server `INVALID_REQUEST`.

The definition rollup has a fixed cap of **50** rows. That cap is not a request field.

Do not add:

- pagination cursors;
- warning-dialog or transaction-failure filters;
- resolution selectors;
- geometry or view-scope options;
- timeout;
- linked-document addressing.

## MCP / transport-neutral output

Closed top-level object:

```text
context
matched_count
counts_by_severity
definitions
warnings
truncated
truncation_reasons
```

`unmatched_element_refs` is present only when the request included `element_refs`.

All listed fields are required when present. `additionalProperties` is false.

### `context`

Closed object:

```text
instance_id: string
document_id: string
```

Both required.

`instance_id` is the addressed RevitMCP process. The transport-neutral request still does not carry it. The capability result reports the process that executed the read.

### `matched_count`

Integer. Exact number of persistent failures that pass the severity, failure-key, and element filters, counted before `max_warnings` and before the definition-row cap.

### `counts_by_severity`

Closed object, always all four keys, counted on the same filtered population as `matched_count`:

```text
warning: integer
error: integer
document_corruption: integer
other: integer
```

The four integers sum to `matched_count`.

These counts are not truncated when `warnings` or `definitions` are truncated.

### `definitions`

Rollup of the filtered population, before the `max_warnings` cut.

Closed item:

```text
failure_key: string
severity: warning | error | document_corruption | other
matched_count: integer
```

One row per observed `(failure_key, severity)` pair.

Order:

1. `matched_count` descending;
2. then `failure_key` ordinal;
3. then severity rank: `document_corruption`, `error`, `warning`, `other`.

Return at most 50 rows. Rows after that are omitted.

When rows are omitted, the sum of returned `definitions[].matched_count` may be less than the top-level `matched_count`. Callers must use `matched_count` and `counts_by_severity` for totals.

An empty `definitions` array is valid when `matched_count` is 0.

### `warnings`

The first `max_warnings` filtered messages in the instance order below.

Closed item:

```text
failure_key: string
severity: warning | error | document_corruption | other
description_text: string
description_truncated: boolean
has_resolutions: boolean
elements: array
elements_truncated: boolean
unresolved_element_count: integer
```

`elements` items are closed:

```text
element_ref: string
role: failing | additional
```

`has_resolutions` reports whether Revit exposes at least one resolution for that message. It does not authorize executing a resolution.

`unresolved_element_count` is the number of failing or additional element ids on that message that could not be turned into an ADR-0006 `element_ref`. It is `0` when every cited id was resolved. Unresolved ids are not given a placeholder ref.

### `unmatched_element_refs`

Present only when `element_refs` was supplied.

Array of requested refs, in request order, that are not cited by any message in the full filtered population. Compute this before the `max_warnings` cut, so a ref cited only by an omitted message is not reported as unmatched.

An empty array means every requested ref was cited at least once.

Omit the property entirely when `element_refs` was omitted. Do not return `null`.

### Truncation

```text
truncated: bool
truncation_reasons: unique array of definitions | elements | warnings
```

`truncated = true` only when known data is omitted because of a cap.

`truncation_reasons` is unique and sorted ordinal. It is empty if and only if `truncated = false`.

- `warnings` — at least one filtered message was omitted because `max_warnings` was exhausted;
- `definitions` — at least one definition row was omitted because the 50-row cap was exhausted;
- `elements` — at least one returned warning omitted a resolved element because `max_elements_per_warning` was exhausted.

`description_truncated` is per warning. It does not by itself set `truncated` or add a top-level reason. The warning itself was returned.

## Warning semantics

Read persistent failures with `Document.GetWarnings()` on the active document inside EXEC-0001.

Do not call another failures API to add errors, dialog contents, or cleared messages that `GetWarnings()` does not return.

Do not show the warnings dialog. Do not call `Document.ClearWarnings`. Do not resolve, delete, or suppress a failure.

### Severity mapping

Map Revit `FailureSeverity` only as follows:

```text
Warning              -> warning
Error                -> error
DocumentCorruption  -> document_corruption
```

`None` and any other value map to `other`.

The Addin owns this mapping. Contracts expose only the four public strings.

### `failure_key`

Emit one opaque string for `FailureMessage.GetFailureDefinitionId()`.

The initial implementation may use the definition id's `Guid` text. That is an implementation detail. Schemas must not declare UUID/GUID format. Clients must not parse the value.

Messages that share a definition share a `failure_key`.

If a message has no definition id, emit `failure_key` as an empty string and still return the message. An empty key is a real key, not "all keys".

### Description

`description_text` is `FailureMessage.GetDescriptionText()`.

Null becomes an empty string. An empty string is valid, with `description_truncated = false`.

A non-empty value may contain at most **512 characters**, using the same rule as CAP-0003 `value_text`.

If the source value exceeds that bound:

- return a valid truncated prefix not exceeding 512 characters;
- set `description_truncated = true`.

Otherwise set `description_truncated = false`.

`description_truncated` reports truncation performed by RevitMCP. It does not claim that Revit's own description is a stable identifier. Group and filter by `failure_key`, not by description text.

### Elements

Resolve cited ids in the active document only.

`element_ref` uses the same opaque ADR-0006 / CAP-0002 handle, initially backed by `Element.UniqueId`. A returned ref is valid input to CAP-0003, CAP-0004, and CAP-0005 for the same `document_id`.

Role mapping:

```text
GetFailingElements()      -> failing
GetAdditionalElements()   -> additional
```

If the same resolved ref appears in both sets, keep a single element with role `failing`.

An element id that does not resolve in the active document, or that cannot produce an `element_ref`, increments `unresolved_element_count` and is omitted from `elements`. Do not open a linked document to resolve it.

Element admission order, after that de-duplication:

1. when `element_refs` was supplied, resolved refs that ordinal-match a requested ref, in request order;
2. remaining `failing` refs, ordinal;
3. remaining `additional` refs, ordinal.

Emit the prefix of that order up to `max_elements_per_warning`.

If any resolved ref is left unemitted, set `elements_truncated = true`. Otherwise set it false.

Unresolved ids do not consume element slots and do not set `elements_truncated`.

### Element filter match

When `element_refs` is supplied, a message matches if any resolved failing or additional ref is ordinal-equal to a requested ref.

The match uses the full resolved set, including refs later omitted by `max_elements_per_warning`.

Unresolved ids never match.

When `element_refs` is omitted, this filter does not remove messages.

### Instance order

Sort filtered messages independently of `GetWarnings()` enumeration order:

1. severity rank: `document_corruption`, `error`, `warning`, `other`;
2. then `failure_key` ordinal;
3. then the ordinal lexicographic compare of the sorted unique `failing` refs;
4. then the ordinal lexicographic compare of the sorted unique `additional` refs;
5. then full, untruncated `description_text` ordinal.

Items that compare equal are both kept. Their relative order is not significant.

`warnings` is the prefix of this order, length `min(matched_count, max_warnings)`.

Repeat requests against the same document state and the same arguments must return the same `matched_count`, `counts_by_severity`, `definitions`, `warnings`, `unmatched_element_refs` when present, `truncated`, and `truncation_reasons`.

## Transport-neutral request

Conceptually:

```text
GetWarningsRequest
{
    document_id: string
    severity?: warning | error | document_corruption | other
    failure_key?: string
    element_refs?: string[1..10]
    max_warnings?: integer
    max_elements_per_warning?: integer
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
- resolution identifiers;
- numeric `ElementId` values.

Transport-neutral validation must reject with `INVALID_WARNINGS` when, at minimum:

- `document_id` is absent or null;
- `severity` is present and not one of the four public values;
- `failure_key` is present and is not a string;
- `element_refs` is present and is null, empty, longer than 10, contains a non-string item, or contains ordinal duplicates;
- a present bound is outside its accepted range.

Do not treat empty or whitespace `document_id`, `failure_key`, or element-ref strings as omitted. Those present strings remain opaque and are handled by the document guard, the exact key match, or the element match.

Omitted optional fields receive the accepted defaults before execution. Omitted `severity`, `failure_key`, and `element_refs` mean "no filter", not a default enum or empty array.

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
INVALID_WARNINGS
REVIT_EXECUTION_TIMEOUT
REVIT_EXECUTION_FAILED
```

Malformed MCP input later maps to `INVALID_REQUEST`.

Do not convert malformed MCP input into `INVALID_WARNINGS`.

An unknown `element_ref` or an empty warning set is a successful result, not a capability error.

## Document guard and execution

Follow the existing capability pattern:

1. validate the transport-neutral request before Revit execution;
2. execute Revit API access only through EXEC-0001;
3. require an active document;
4. validate exact `document_id` before reading warnings;
5. read `Document.GetWarnings()` once for the call;
6. filter, aggregate, and project in the deterministic order above;
7. create no `Transaction`, `SubTransaction`, or `TransactionGroup`.

No Autodesk `FailureMessage`, `FailureDefinitionId`, `ElementId`, file path, or Revit API object may enter Contracts or the MCP result.

## Bounds / context efficiency

This capability satisfies ADR-0005 by aggregating before returning instances:

- exact `matched_count` and `counts_by_severity` without listing every message;
- at most 50 definition rows;
- at most `max_warnings` messages, default 25, hard maximum 100;
- at most `max_elements_per_warning` refs per returned message, default 10, hard maximum 20;
- description text capped at 512 characters;
- optional severity, failure-key, and element filters;
- item-level element omission instead of failing the call when an id will not resolve;
- no whole-document warning dump as the default.

## Context / security boundaries

CAP-0009 does not return:

- resolution captions, resolution ids, or instructions to execute a resolution;
- numeric durable `ElementId`;
- coordinates, geometry, or bounding boxes;
- file paths, usernames, or cloud/project identifiers;
- the contents of a transaction failure dialog;
- warnings that exist only inside a linked document;
- writeability or authorization decisions.

Description text can contain model-authored names and parameter values. The 512-character cap is the minimization rule for that text. Do not add a second unbounded prose field.

`readOnlyHint` is not a write-authorization substitute.

`has_resolutions` must not be treated as permission to modify the model.

## MCP success policy

Modern successful result:

```text
isError = false
structuredContent = GetWarningsResult
content = []
```

Errors:

```text
isError = true
structuredContent absent
one compact JSON TextContent error
```

The official MCP tool is specified here. Do not register it until a bridge contract for it is implemented and live-tested through the typed Bridge, and a server contract is implemented.

## Compatibility

RevitMCP continues to target Revit 2025, 2026, and 2027 from one shared Addin project. `Document.GetWarnings()` and `FailureMessage` belong to that supported matrix.

This specification does not claim live validation on Revit 2025, 2026, or 2027.

Do not treat compile-time availability as live proof.

## Acceptance criteria

CAP-0009 is acceptable as a capability contract when:

1. It is one coherent read-only capability.
2. `document_id` is required as a present string. Absent/null is `INVALID_WARNINGS`. A present string is opaque, is never trimmed or normalized, and is compared exactly to the active document id. Empty or whitespace therefore yields `DOCUMENT_CONTEXT_CHANGED`.
3. `instance_id` remains Server routing-only on input and is reported on result `context` for the process that executed the read.
4. `severity`, when present, is exactly one of `warning`, `error`, `document_corruption`, or `other`. Omitted means all mapped severities.
5. `failure_key`, when present, is an exact ordinal match, including empty or whitespace. Omitted means all keys.
6. `element_refs`, when present, contains 1..10 unique opaque strings. Omitted means no element filter. Unknown refs do not fail the call.
7. Bounds use the accepted defaults and closed ranges. Present out-of-range values are `INVALID_WARNINGS`.
8. The source is `Document.GetWarnings()` on the active document. Transient transaction failure dialogs are not included.
9. Output is a closed object of `context`, `matched_count`, `counts_by_severity`, `definitions`, `warnings`, `truncated`, and `truncation_reasons`, plus `unmatched_element_refs` only when an element filter was supplied.
10. `counts_by_severity` always has four integers and sums to `matched_count`, including when lists are truncated.
11. Definition rows are one per `(failure_key, severity)`, ordered by count descending, then key, then severity rank, and capped at 50.
12. Warning instances use the specified order, independent of Revit enumeration order, and return only the `max_warnings` prefix.
13. Element refs use the ADR-0006 handle. `failing` wins over `additional` for the same ref. Requested refs are admitted first when an element filter is present. Unresolved ids increment `unresolved_element_count` and do not consume element slots.
14. Description text follows the CAP-0003 512-character prefix rule.
15. `truncated` and `truncation_reasons` report omitted definition rows, omitted messages, and omitted resolved elements. Description truncation stays on the item.
16. `unmatched_element_refs` is computed on the full filtered population, before the `max_warnings` cut, and preserves request order.
17. `DOCUMENT_CONTEXT_CHANGED` is a top-level capability error with no document fallback.
18. All Revit API access executes through EXEC-0001 and creates no transaction. No resolution, deletion, suppression, or warnings dialog runs.
19. Contracts remain transport-neutral and expose no Revit API objects.
20. Server remains Revit-API independent.
21. Revit 2025, 2026, and 2027 variants compile in CI when implemented.
22. Automated coverage, when implemented, includes request validation, opaque empty strings, defaults and ranges, severity mapping, definition aggregation and the 50-row cap, instance order independent of source order, element admission including the requested-ref prefix, unresolved ids, description truncation, truncation reasons, and the document guard. Live typed-Bridge validation, when implemented, covers a document with persistent warnings, a document with none, the element filter, truncation, and the document guard. Naturally unavailable Revit cases may be recorded rather than manufactured.
23. Official MCP live validation is a later server gate, not part of accepting this specification.
24. CAP-0001 through CAP-0008 remain unchanged by this specification.
25. This specification does not close the deferred CAP-0008 induced failure-dialog validation.

## Explicitly deferred

- executing, deleting, or suppressing a warning;
- resolution captions and resolution arguments;
- the Revit warnings dialog;
- transient transaction failure dialogs, including the deferred CAP-0008 induced failure-dialog case;
- linked-document traversal;
- pagination cursors;
- numeric `ElementId` results;
- geometry, coordinates, or view screenshots;
- a request field that raises the 50-row definition cap;
- any write, including CAP-0008 apply, as part of this capability.

## Bridge / Server sequencing

CAP-0009 itself is transport-neutral.

Expected sequence after acceptance:

```text
CAP-0009 accepted
-> bridge contract for revit.get_warnings
-> typed Bridge live validation
-> server contract for revit_get_warnings
-> official MCP live validation
```

The bridge contract is [BRIDGE-0011](../bridge/BRIDGE-0011-get-warnings-capability-rpc.md). It assigns the protocol version and preserves current capabilities by explicit version sets. Do not implement that version by numeric `>=` behavior. This capability specification does not assign the version number.

This document does not implement the capability, the bridge method, or the MCP tool.

## References

- ADR-0005: Agent context and token efficiency
- ADR-0006: Document and element reference identity
- ADR-0008: Controlled write safety model
- CAP-0002: `revit_query_elements`
- CAP-0003: `revit_get_elements`
- CAP-0008: `revit_apply_parameter_updates`
- EXEC-0001: serialized Revit execution dispatcher
- Autodesk Revit API `Document.GetWarnings`
- Autodesk Revit API `FailureMessage`
- Autodesk Revit API `FailureSeverity`
