# SERVER-0007: stdio `revit_preview_parameter_updates` tool

- Status: Proposed
- Date: 2026-10-01

## Purpose

Expose accepted CAP-0007 `revit_preview_parameter_updates` through the existing standalone `RevitMCP.Server` stdio MCP process while preserving SERVER-0001 through SERVER-0006 behavior.

SERVER-0007 applies the existing explicit Server pattern to accepted CAP-0007 / BRIDGE-0008. It does not create a new architecture decision, server process, transport, or generic capability framework.

This specification does not implement Server code, schemas, tests, routing, or tool registration. Current runtime MCP tools remain exactly six. After a later implementation PR, `tools/list` will expose seven tools. This specification does not claim that the seventh tool exists now.

SERVER-0007 must not change CAP-0007, BRIDGE-0008, or LIFECYCLE-0002 semantics.

## Scope

- expose exactly one additional MCP tool: `revit_preview_parameter_updates`;
- preserve `revit_get_context`, `revit_query_elements`, `revit_get_elements`, `revit_describe_parameters`, `revit_get_parameter_values`, and `revit_get_mep_topology`;
- use existing fresh current-Windows-session discovery and deterministic instance routing;
- negotiate Bridge protocol through `BridgeProtocol.SupportedVersions` and invoke preview only when `BridgeProtocol.SupportsPreviewParameterUpdates` accepts the selected version;
- preserve typed Named Pipe clients and per-call fresh capability invocation;
- require CAP-0007 `document_id` context guarding end to end;
- advertise a closed input schema for one bounded batch of proposed instance-parameter updates;
- advertise a closed `PreviewParameterUpdatesResult`, with `ready=false` and `ready=true` as distinct result families;
- enforce closed MCP input before discovery/Bridge work, including semantic `(element_ref, parameter_ref)` uniqueness;
- preserve ADR-0005 structured-content/token behavior;
- preserve explicit capability-version sets rather than numeric comparisons;
- preserve ADR-0008: a successful preview is not approval and is not a Revit model write.

## Non-goals

- changing CAP-0007 write eligibility, item status order, value semantics, intent fingerprint, or intent-store rules;
- changing BRIDGE-0008 protocol sets, RPC method semantics, or timeout/orphan-intent behavior;
- CAP-0008/apply;
- Revit transactions, `Parameter.Set`, save, or sync;
- an approval provider, MRTR approval, or `confirm=true`;
- MCP Apps, Revit UI, APS, an orchestrator, or a WebMCP implementation;
- a generic capability registry, reflection scanning, or a dynamic tool list;
- an MCP SDK upgrade;
- intent lookup, recovery, or retry after timeout;
- changing global `McpCallResultFactory` success/error policy.

## MCP SDK and hosting

Continue the accepted Server stack:

```text
ModelContextProtocol 2.2.0
.NET 10
stdio transport
explicit tool registration
```

Do not upgrade the MCP SDK in SERVER-0007.

No MCP SDK dependency may be introduced into Contracts, Bridge, or Addin.

stdout remains reserved for MCP protocol traffic. Diagnostics must not contaminate stdout.

Tool registration remains explicit. Do not switch to assembly-wide scanning, reflection-driven discovery, a generic tool registry, or dynamic tool-list exposure.

After later SERVER-0007 implementation, `tools/list` will expose exactly:

```text
revit_get_context
revit_query_elements
revit_get_elements
revit_describe_parameters
revit_get_parameter_values
revit_get_mep_topology
revit_preview_parameter_updates
```

Count is 7. The existing six names stay unchanged and stay in that order, with the preview tool added.

This specification does not claim that seven tools exist now.

No Bridge/internal JSON-RPC method is exposed directly as an MCP tool. In particular, `revit.preview_parameter_updates` remains a Bridge RPC, not an MCP tool name.

## Tool metadata

Use the CAP-0007 accepted metadata exactly.

### Name

`revit_preview_parameter_updates`

### Title

`Preview Revit Parameter Updates`

### Description

Validate and preview one bounded batch of proposed instance-parameter updates in the active project document, and create an immutable ephemeral intent when every update is eligible. Does not modify the Revit model.

### Annotations

All four hints are explicit:

```text
readOnlyHint = false
destructiveHint = false
idempotentHint = false
openWorldHint = false
```

- `readOnlyHint = false` because a successful preview mutates ephemeral RevitMCP intent state. The hint does not mean the tool mutates the Revit model.
- `destructiveHint = false` because preview does not delete, clear, or change Revit elements.
- `idempotentHint = false` because repeated semantic previews may create distinct `intent_ref` values. The same ordered semantic contents still produce the same fingerprint.
- `openWorldHint = false` because the tool does not reach outside the addressed Revit document and the RevitMCP intent store.

Annotations are descriptive hints. They are never approval. ADR-0008 forbids treating them as authorization to apply a change.

## MCP input schema

The MCP schema is a closed JSON Schema 2020-12 object.

Top-level fields:

```text
instance_id?: string | null
document_id: string
updates: array[1..20]
```

Required:

```text
document_id
updates
```

Top-level `additionalProperties` is false.

### Opaque ids

`instance_id`, `document_id`, `element_ref`, and `parameter_ref` remain opaque strings.

Do not advertise UUID/GUID formats. Do not require clients to parse `intent_ref` or `intent_fingerprint`.

Do not trim, normalize, or treat empty/whitespace present strings as omitted.

Required MCP fields that are missing, JSON `null`, or an uninterpretable JSON type are `INVALID_REQUEST` at the Server boundary.

### `instance_id`

Optional Server routing only.

Advertise:

```json
{
  "type": ["string", "null"]
}
```

Semantics stay:

- omitted or JSON `null` means unspecified;
- a present string is routed exactly;
- empty/whitespace present strings remain explicit opaque ids and must never trigger auto-selection;
- `instance_id` never enters `PreviewParameterUpdatesRequest`.

No UUID format. The alias `instanceId` is `INVALID_REQUEST`.

### `document_id`

Required opaque string. No `minLength`.

Empty or whitespace present strings are syntactically valid MCP input. They must reach CAP-0007 document guarding and normally yield `DOCUMENT_CONTEXT_CHANGED`.

Missing or JSON `null` `document_id` is `INVALID_REQUEST` before discovery or Bridge. Do not convert that malformed MCP input into `INVALID_PARAMETER_UPDATE_PREVIEW`, and do not convert a later `DOCUMENT_CONTEXT_CHANGED` into `INVALID_REQUEST`.

The alias `documentId` is `INVALID_REQUEST`.

### `updates`

```text
type = array
minItems = 1
maxItems = 20
items = closed object
```

`uniqueItems = true` may be advertised. It is not sufficient for CAP-0007 uniqueness. See semantic pair uniqueness below.

Each item is a closed object and requires:

```text
element_ref: string
parameter_ref: string
value: closed union
```

Item `additionalProperties` is false.

Do not add `minLength` to either ref.

Preserve raw strings exactly. Do not trim or rewrite them.

Empty or whitespace refs are syntactically valid MCP input and must reach CAP-0007 item resolution.

A non-object update, or a missing, null, or non-string ref, is `INVALID_REQUEST`.

The same `element_ref` may appear with several parameter refs. The same `parameter_ref` may appear with several element refs. Uniqueness is on the pair, not on each ref alone.

Do not add `element_reference`.

Aliases such as `elementRef` and `parameterRef` are `INVALID_REQUEST`.

### Semantic pair uniqueness

CAP-0007 uniqueness is ordinal equality on the raw `(element_ref, parameter_ref)` pair, not on the entire update object.

`uniqueItems = true` compares array items. Two updates that share a pair and differ in `value` are different JSON objects, so `uniqueItems` alone accepts them. That acceptance is wrong.

This request must be `INVALID_REQUEST` before discovery or Bridge:

```json
{
  "document_id": "opaque-document",
  "updates": [
    {
      "element_ref": "e1",
      "parameter_ref": "p1",
      "value": { "kind": "string", "value": "A" }
    },
    {
      "element_ref": "e1",
      "parameter_ref": "p1",
      "value": { "kind": "string", "value": "B" }
    }
  ]
}
```

The same pair with properties in a different order is also a duplicate and is `INVALID_REQUEST`.

Future implementation must add a small targeted semantic-pair duplicate guard analogous to SERVER-0005, before discovery or Bridge invocation.

Do not create another validation framework.

### Proposed value schema

`value` is a closed, mutually exclusive union. The discriminator is `kind`. No additional properties. No null, clear, or unset variant.

Missing or JSON `null` `value` is `INVALID_REQUEST`.

Unknown `kind` values and extra value properties are `INVALID_REQUEST`.

#### `string`

```text
kind = "string"
value: string
maxLength = 512
```

The empty string is allowed. It is a proposed value, not a clear.

A string longer than 512 characters is `INVALID_REQUEST` at the MCP boundary.

#### `integer`

```text
kind = "integer"
value: integer
minimum = -2147483648
maximum = 2147483647
```

The JSON value must be an integer token. A fractional number, or an integer outside the Int32 range, is `INVALID_REQUEST`.

#### `quantity`

```text
kind = "quantity"
value: number
unit_type_id: string
minLength = 1
```

`unit_type_id` is required and non-empty. An empty `unit_type_id`, a missing field, a non-number `value`, or any other invalid quantity JSON shape is `INVALID_REQUEST`.

The alias `unitTypeId` is `INVALID_REQUEST`.

MCP-boundary rejection does not replace CAP-0007. A quantity that is syntactically valid at the MCP boundary and later fails Revit unit checks remains item status `invalid_unit` or capability error `INVALID_PARAMETER_UPDATE_PREVIEW`, whichever CAP-0007 already defines. The Server must not convert those results into `INVALID_REQUEST`.

## MCP-boundary validation

Use the existing:

```text
StrictInputMcpServerTool
ClosedSchemaArgumentValidator
```

Valid MCP input maps exactly to CAP-0007 Contracts.

Malformed MCP input returns `INVALID_REQUEST` before discovery or Bridge invocation.

This includes at minimum:

- missing or null `document_id`;
- missing or null `updates`;
- update count outside 1..20;
- a non-object update;
- missing, null, or non-string refs;
- missing or null `value`;
- an unknown value kind;
- extra fields at the top level, update, or value;
- a string longer than 512 characters;
- an integer outside Int32, or a fractional number offered as an integer;
- an invalid quantity JSON shape;
- an empty `unit_type_id`;
- a duplicate semantic `(element_ref, parameter_ref)` pair, including the same pair with different proposed values;
- aliases such as `documentId`, `elementRef`, `parameterRef`, `unitTypeId`, and `instanceId`;
- `confirm` or any equivalent approval field.

Do not leak binder, serializer, stack, path, or implementation detail in the error.

Do not convert a valid-transport CAP-0007 failure into `INVALID_REQUEST`.

`INVALID_PARAMETER_UPDATE_PREVIEW` remains the transport-neutral capability error when the Addin or Bridge returns it after a valid MCP-boundary mapping.

## Transport-neutral mapping

Accepted MCP input maps exactly to:

```text
PreviewParameterUpdatesRequest
{
  DocumentId = document_id
  Updates = updates in original order
}
```

Each update maps unchanged:

```text
PreviewParameterUpdate
{
  ElementRef = element_ref
  ParameterRef = parameter_ref
  Value = the proposed closed union
}
```

Preserve:

- order;
- refs;
- proposed typed values;
- the supplied quantity unit.

No rewriting. No trimming. No `instance_id`. No timeout field. No approval, confirmation, client, model, or provider fields.

No Server-generated hidden document id, element-ref translation, or parameter-ref invention is allowed.

## Output schema

Successful structured content is exactly `PreviewParameterUpdatesResult`.

Use a closed schema. Model the two top-level result families explicitly. A single object with optional intent fields is not enough, because `ready=true` must require intent metadata and `ready=false` must forbid it.

### `ready = false`

Required:

```text
context
ready = false
items
```

Absent:

```text
intent_ref
intent_fingerprint
expires_at
```

`additionalProperties` is false.

### `ready = true`

Required:

```text
context
ready = true
items
intent_ref
intent_fingerprint
expires_at
```

Every item status is `ok`.

`additionalProperties` is false.

`intent_ref` and `intent_fingerprint` are opaque strings. Do not advertise a GUID or UUID semantic. Do not require clients to parse them. Do not add a hex-length pattern to the public schema.

`expires_at` is an ISO-8601 date-time string. CAP-0007 serializes it as UTC with a `Z` offset. Clients may display it. They cannot extend it.

### `context`

Exact closed shape:

```text
instance_id: string
document_id: string
```

Both are required. `additionalProperties` is false.

This result `instance_id` is the instance that executed the preview. It is not a copy of an omitted routing input.

### Failure items

Exactly:

```text
element_ref
parameter_ref
status
```

`status` is one of:

```text
parameter_ref_not_found
element_not_found
unsupported_parameter_source
parameter_not_present
parameter_not_writable
value_type_mismatch
invalid_unit
unsupported_value
```

No display metadata. No `before`. No `proposed`. `additionalProperties` is false.

### `ok` and `no_change` items

Closed shape:

```text
element_ref
parameter_ref
status
element_name
element_name_truncated
category_name
category_name_truncated
parameter_name
parameter_name_truncated
data_type
before
proposed
```

`status` is `ok` or `no_change`.

Names have `maxLength` 512.

`data_type` reuses the CAP-0004 schema exactly:

```text
kind              // measurable_spec | spec | category | unknown
forge_type_id?    // present only when Revit supplied a non-empty data-type identifier
```

Do not invent a second data-type model. Do not add a sibling `forge_type_id` beside `data_type`.

`before`:

```text
has_value: boolean
value?: the same preview value union
```

When `has_value` is false, `value` is absent.

`proposed` is always the exact preview value union.

`additionalProperties` is false on the item, on `before`, and on `data_type`.

Do not expose internal Revit doubles or numeric Revit ids.

The Server must not recalculate fingerprints, reorder items, rewrite values, parse intents, or reinterpret statuses. It returns the Bridge result.

## Normal success semantics

Both `ready=true` and `ready=false` are successful MCP tool results:

```text
isError = false
structuredContent = authoritative PreviewParameterUpdatesResult
content = []
```

A `no_change`, `parameter_not_writable`, `invalid_unit`, or other item status remains normal successful structured output when the preview result is returned.

`INTENT_CAPACITY_REACHED` remains an error. It is not `ready=false` and it is not a success-shaped result.

Errors keep the existing shape:

```text
isError = true
structuredContent absent
one compact JSON TextContent payload
```

Reuse `McpCallResultFactory`. Do not change its global policy.

## Routing

Add, in a later implementation:

```text
ResolveForPreviewParameterUpdates(...)
IsPreviewParameterUpdatesEligible(...)
```

Back them only with:

```text
BridgeProtocol.SupportsPreviewParameterUpdates
```

Exact eligibility:

```text
v1 false
v2 false
v3 false
v4 false
v5 false
v6 false
v7 false
v8 true
v9 false
```

Never numeric `>=`. Any other unknown version is ineligible because the support predicate is the only gate.

Preserve existing routing behavior:

- 0 eligible -> `NO_REVIT_INSTANCE`;
- 1 eligible -> auto-select;
- more than 1 eligible -> `INSTANCE_REQUIRED`.

Candidates remain deterministic by ordinal opaque `instance_id`.

Explicit id:

- unknown -> `INSTANCE_NOT_FOUND`;
- known but unavailable or ineligible -> `INSTANCE_UNAVAILABLE`;
- eligible v8 -> selected.

Empty or whitespace explicit `instance_id` never auto-selects.

The existing six tools retain their current inherited v8 support sets unchanged:

```text
get-context            {2,3,4,5,6,7,8}
query-elements         {3,4,5,6,7,8}
get-elements           {4,5,6,7,8}
describe-parameters    {5,6,7,8}
get-parameter-values   {6,7,8}
get-mep-topology       {7,8}
```

CAP-0001 through CAP-0006 semantics are unchanged.

## Application service

Future implementation follows the current explicit SERVER-0006 pattern.

Accepted capability errors, preserved unchanged:

```text
NO_ACTIVE_DOCUMENT
DOCUMENT_CONTEXT_CHANGED
UNSUPPORTED_DOCUMENT_KIND
DOCUMENT_NOT_WRITABLE
INVALID_PARAMETER_UPDATE_PREVIEW
INTENT_CAPACITY_REACHED
REVIT_EXECUTION_TIMEOUT
REVIT_EXECUTION_FAILED
```

Routing errors remain:

```text
NO_REVIT_INSTANCE
INSTANCE_REQUIRED
INSTANCE_NOT_FOUND
INSTANCE_UNAVAILABLE
```

MCP malformed input:

```text
INVALID_REQUEST
```

Do not expose arbitrary Bridge or IO exceptions. Map other Bridge, IO, and bootstrap failures through the existing `INSTANCE_UNAVAILABLE` behavior.

Execution:

```text
fresh discovery
-> ResolveForPreviewParameterUpdates
-> fresh Bridge client
-> connect
-> handshake with BridgeProtocol.SupportedVersions
-> verify exact instance identity
-> require SupportsPreviewParameterUpdates(selectedVersion)
-> PreviewParameterUpdatesAsync(
     request,
     ServerTimeouts.CapabilityTimeout,
     cancellationToken)
-> dispose
```

Handshake uses `BridgeProtocol.SupportedVersions`. For the current full host that list is `[8, 7, 6, 5, 4, 3, 2, 1]`. Do not advertise a preview-only version list, and do not drop inherited versions.

A selected version that fails `SupportsPreviewParameterUpdates` does not invoke `PreviewParameterUpdatesAsync`.

No retries.

This no-retry rule is especially important for CAP-0007. Caller timeout or response loss can occur after a valid intent was created. `REVIT_EXECUTION_TIMEOUT` is not proof that no intent exists. The Server must not interpret timeout that way.

No intent lookup, recovery, or retry protocol is added. An unreported intent expires through LIFECYCLE-0002: the 10-minute lifetime, successful document close, and process shutdown.

Caller cancellation remains cancellation. Timeout after Revit execution has begun does not forcibly abort Revit API execution.

There is no agent-facing timeout input. SERVER-0007 does not increase global timeout values.

## Security boundary

State these limits explicitly:

- preview is not apply;
- preview creates no Revit transaction;
- preview calls no `Parameter.Set`;
- `intent_ref` is not authorization;
- a fingerprint is not approval;
- a successful MCP invocation is not approval;
- client, model, or provider identity is not approval;
- `confirm=true` or an equivalent field is forbidden;
- SERVER-0007 has no MRTR or `inputResponses` approval semantics;
- SERVER-0007 has no trusted approval provider;
- ADR-0008 remains authoritative for a later CAP-0008.

The Server does not re-evaluate write eligibility, recompute fingerprints, or decide whether an intent may later be applied.

## Dependency boundaries

Required direction remains:

```text
Contracts <- Bridge <- Server
Contracts <- Bridge <- Addin -> Revit API
```

Server must not reference Autodesk Revit API assemblies.

Contracts and Bridge must not reference MCP SDK types.

Seven explicit tools do not justify a new generic framework.

If a later implementation discovers that a lower-layer production change appears necessary, stop and report the conflict rather than expanding Server scope silently.

## Expected implementation shape

This specification PR adds no source code.

Follow the current explicit Server conventions. Expected later components:

```text
Cap0007JsonSchemas
PreviewParameterUpdatesToolMetadata
PreviewParameterUpdatesMcpTools
PreviewParameterUpdatesApplicationService
PreviewParameterUpdatesOutcome
PreviewParameterUpdatesToolRegistration
```

Plus explicit small additions to:

```text
InstanceTargetResolver
ServerHost
```

Reuse:

```text
StrictInputMcpServerTool
ClosedSchemaArgumentValidator
McpCallResultFactory
McpJson
ServerTimeouts
```

`PreviewParameterUpdatesOutcome` follows the existing outcome shape: success carries `PreviewParameterUpdatesResult`; failure carries an error code, message, and optional candidates.

Register exactly one new MCP tool through an explicit `ServerHost` extension, analogous to `.WithGetMepTopologyTool()`.

Do not use assembly scanning. Do not change global `McpCallResultFactory` behavior. No generic tool or capability framework.

## Automated implementation acceptance criteria

Later implementation tests must cover at least:

1. exactly seven MCP tools;
2. exact metadata and all four annotations;
3. a closed input schema;
4. `updates` bounded to 1..20;
5. all three value variants;
6. semantic duplicate-pair rejection, including the same pair with different proposed values;
7. aliases and extra properties rejected;
8. malformed input performs no discovery or Bridge work;
9. optional `instance_id` routing semantics, including that empty or whitespace present ids do not auto-select;
10. the `ready=false` output shape, with intent metadata absent;
11. the `ready=true` output shape, with intent metadata required and every item `ok`;
12. a failure item of exactly three fields;
13. the full `ok` / `no_change` item shape;
14. `before.has_value = false` omits `value`;
15. `data_type` keeps the existing CAP-0004 semantics;
16. intent metadata only when `ready=true`;
17. `ready=false` remains `isError=false`;
18. `INTENT_CAPACITY_REACHED` survives as an error;
19. routing protocol `{8}` only;
20. v1..v7 and v9 ineligible;
21. fresh discovery, client, and handshake on every call;
22. no retries;
23. timeout behavior, including that timeout is not treated as proof that no intent exists;
24. client disposal;
25. the existing six tools remain eligible on v8;
26. process-level `tools/list` exposes exactly seven tools and no Bridge RPC method;
27. no Server dependency on the Revit API;
28. no production write code.

Also cover:

- empty or whitespace `document_id` is not `INVALID_REQUEST` and is mapped through to document guarding;
- transport-neutral CAP-0007 errors are not rewritten into `INVALID_REQUEST`;
- request order, refs, proposed values, and quantity units are preserved;
- `instance_id` is absent from `PreviewParameterUpdatesRequest`;
- errors have no success-shaped `structuredContent`;
- the existing six-tool Server regression suites remain green.

Do not require live Revit for these automated Server tests.

## Process-level validation

Using official `ModelContextProtocol` 2.2.0 against built Release `RevitMCP.Server` after implementation:

```text
tools/list count = 7
```

Exactly:

```text
revit_get_context
revit_query_elements
revit_get_elements
revit_describe_parameters
revit_get_parameter_values
revit_get_mep_topology
revit_preview_parameter_updates
```

No internal Bridge RPC methods.

Process-level strict-boundary smoke must also prove a malformed preview input returns `INVALID_REQUEST` before Revit discovery or Bridge work.

## Official MCP live gate after implementation

After SERVER-0007 implementation, validate:

```text
official ModelContextProtocol client
-> stdio RevitMCP.Server
-> fresh discovery/routing
-> Bridge v8
-> Addin CAP-0007
-> EXEC-0001
-> Revit 2026.5
```

Use a disposable Autodesk project. Do not use a production model.

Required:

```text
tools/list = 7
get_context PASS
query PASS
describe_parameters PASS
get_parameter_values PASS
preview A PASS
preview B PASS
preview C PASS
reread unchanged PASS
is_modified unchanged PASS
malformed MCP request -> INVALID_REQUEST
wrong document_id -> DOCUMENT_CONTEXT_CHANGED
```

Preview cases keep the accepted CAP-0007 meanings:

- A: a successful preview returns `ok`, `ready=true`, and intent metadata;
- B: the same semantic preview returns the same fingerprint and a new `intent_ref`;
- C: the exact current value returns `no_change` and `ready=false`.

Quantity invalid-unit case D is required only when a suitable quantity candidate is naturally available. Otherwise record D as not run. Do not manufacture a quantity candidate or change the model to create one.

No `Parameter.Set`. No transaction. No save. No sync.

## Acceptance criteria

SERVER-0007 is accepted as a specification when:

1. It proposes exactly one new MCP tool, `revit_preview_parameter_updates`, without claiming that seven tools exist in the current runtime.
2. The tool name, title, description, and all four annotations match accepted CAP-0007 metadata, including the explanation that `readOnlyHint=false` is ephemeral intent state and not a Revit model write.
3. MCP input is a closed JSON Schema 2020-12 object with required `document_id` and `updates`, optional `instance_id` as `string | null`, and `additionalProperties = false`.
4. `updates` is 1..20 closed items. Semantic uniqueness is the raw `(element_ref, parameter_ref)` pair. `uniqueItems=true` alone is not the uniqueness rule. The same pair with different proposed values is `INVALID_REQUEST` before discovery or Bridge.
5. The proposed value union is exactly string, integer, and quantity, with the bounds in this specification. There is no null, clear, or unset variant and no `element_reference`.
6. Empty or whitespace `document_id` and refs stay opaque and are not schema-rejected. Missing or null required fields are `INVALID_REQUEST`.
7. Accepted MCP input maps to `PreviewParameterUpdatesRequest` in original order, without `instance_id`, timeout, or approval fields.
8. Output is a closed `PreviewParameterUpdatesResult` with distinct `ready=false` and `ready=true` families. Intent metadata is required only when `ready=true`. Failure items are exactly three fields. `ok` and `no_change` items carry the CAP-0007 display and value shape. `data_type` is the existing CAP-0004 schema.
9. Both ready values are successful MCP results. `INTENT_CAPACITY_REACHED` stays an error. The Server does not recalculate fingerprints or reinterpret statuses.
10. Routing is explicit `{8}` through `BridgeProtocol.SupportsPreviewParameterUpdates`. v1..v7 and v9 are ineligible. Never numeric `>=`. The existing six tools keep their current v8 eligibility.
11. Every invocation performs fresh discovery, a fresh typed Bridge client, handshake with `BridgeProtocol.SupportedVersions`, identity validation, the preview support check, `PreviewParameterUpdatesAsync`, and disposal. No retry. Timeout is not proof that no intent exists.
12. The security boundary states that preview is not apply, not a transaction, not `Parameter.Set`, and not approval. ADR-0008 remains authoritative for later CAP-0008.
13. Later automated tests cover the listed schema, duplicate-pair, routing, handshake, timeout, disposal, seven-tool, and inherited-regression cases without live Revit.
14. Official MCP-client-to-Revit-2026.5 live validation is required after implementation, including the A/B/C/reread/`is_modified` cases. Case D is required only when a quantity candidate is naturally available.
15. No CAP-0008/apply, transaction, approval provider, MRTR approval, MCP Apps, SDK upgrade, generic registry, or dynamic tool list.

SERVER-0007 is accepted as implemented only after a later implementation PR satisfies those criteria, automated coverage, and the official MCP live gate.

## Explicitly deferred

SERVER-0007 must not introduce:

- CAP-0008/apply;
- a Revit transaction;
- an approval provider;
- MRTR approval;
- MCP Apps;
- Revit UI;
- an MCP SDK upgrade;
- APS;
- an orchestrator;
- a WebMCP implementation;
- a generic capability registry;
- a dynamic tool list;
- intent lookup, listing, or recovery.

## References

- SERVER-0001 through SERVER-0006
- CAP-0007: `revit_preview_parameter_updates`
- BRIDGE-0008: `revit.preview_parameter_updates` and protocol v8
- LIFECYCLE-0002: ephemeral write-intent store
- ADR-0005: agent context and token efficiency
- ADR-0006: document and element reference identity
- ADR-0008: controlled write safety model
