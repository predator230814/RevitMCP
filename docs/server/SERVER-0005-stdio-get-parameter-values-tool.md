# SERVER-0005: stdio `revit_get_parameter_values` tool

- Status: Accepted
- Date: 2026-09-22

## Purpose

Expose accepted CAP-0005 `revit_get_parameter_values` through the existing standalone `RevitMCP.Server` stdio MCP process while preserving SERVER-0001 through SERVER-0004 behavior.

SERVER-0005 applies the existing SERVER-0001..0004 pattern to accepted CAP-0005 / BRIDGE-0006. It does not create a new architecture decision, server process, transport, or generic capability framework.

This specification does not implement Server code, schemas, tests, routing, or tool registration. After a later implementation PR, `tools/list` will expose five tools. Current runtime MCP tools remain exactly four.

SERVER-0005 must not change CAP-0005 or BRIDGE-0006 semantics.

## Scope

- expose exactly one additional MCP tool: `revit_get_parameter_values`;
- preserve `revit_get_context`, `revit_query_elements`, `revit_get_elements`, and `revit_describe_parameters`;
- use existing fresh current-Windows-session discovery and deterministic instance routing;
- negotiate Bridge protocol v6 for CAP-0005;
- preserve typed Named Pipe clients and per-call fresh capability invocation;
- require CAP-0005 `document_id` context guarding end to end;
- advertise a closed input schema of explicit `element_ref + parameter_ref` pairs;
- advertise closed mutually exclusive output item and typed-value variants;
- enforce closed MCP input before discovery/Bridge work, including semantic pair uniqueness;
- reuse the accepted CAP-0004 `data_type` output schema;
- preserve ADR-0005 structured-content/token behavior;
- preserve explicit capability-version sets rather than numeric comparisons.

## Non-goals

- changing CAP-0005 typed-value, quantity, document-guard, or item-status rules;
- changing BRIDGE-0006 protocol sets or RPC method semantics;
- caller-selected unit conversion;
- display-name parameter lookup;
- persistent `parameter_ref` reuse after document reopen;
- writes, transactions, approvals, or authorization;
- preview/apply workflows;
- linked-document addressing;
- hidden/API-only parameter reads;
- new MCP transports or remote HTTP hosting;
- MCP SDK upgrade;
- generic tool/capability registries;
- changing global `McpCallResultFactory` success/error policy;
- Revit product UI.

## MCP SDK and hosting

Continue the accepted Server stack:

```text
ModelContextProtocol 2.2.0
.NET 10
stdio transport
```

Do not upgrade the MCP SDK in this work.

No MCP SDK dependency may be introduced into Contracts, Bridge, or Addin.

stdout remains reserved for MCP protocol traffic. Diagnostics must not contaminate stdout.

Tool registration remains explicit. Do not switch to assembly-wide scanning, reflection-driven discovery, a generic tool registry, or dynamic tool-list exposure.

After later SERVER-0005 implementation, `tools/list` will expose exactly:

```text
revit_get_context
revit_query_elements
revit_get_elements
revit_describe_parameters
revit_get_parameter_values
```

This specification does not claim that five tools exist now.

No Bridge/internal JSON-RPC method is exposed directly as an MCP tool. In particular, `revit.get_parameter_values` remains a Bridge RPC, not an MCP tool name.

## Tool metadata

Use the CAP-0005 accepted metadata exactly.

### Name

`revit_get_parameter_values`

### Title

`Get Revit Parameter Values`

### Description

Read typed values for a bounded explicit set of known Revit element and parameter references in the active document.

### Annotations

```text
readOnlyHint = true
openWorldHint = false
```

Annotations are descriptive hints, not security boundaries.

## MCP input schema

The MCP schema is a closed JSON Schema 2020-12 object.

Top-level fields:

```text
instance_id?: string | null
document_id: string
reads: array
```

Required:

```text
document_id
reads
```

Top-level `additionalProperties` is false.

Do not advertise a Cartesian `element_refs[] × parameter_refs[]` form.

### Opaque ids

`instance_id`, `document_id`, `element_ref`, and `parameter_ref` remain opaque strings.

Do not advertise UUID/GUID formats or parse them in Server code.

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

```text
omitted -> unspecified
null -> unspecified
empty string -> explicit opaque value
whitespace string -> explicit opaque value
```

A present empty/whitespace `instance_id` must never trigger auto-selection.

`instance_id` must not enter `GetParameterValuesRequest`.

### `document_id`

Required opaque string.

```text
type: string
```

Do not add `minLength`.

A present empty or whitespace `document_id` is valid MCP syntax and must reach CAP-0005. Exact active-document guarding then produces `DOCUMENT_CONTEXT_CHANGED`.

Do not default `document_id` from another MCP call or hidden Server state.

### `reads`

Required array of explicit pairs:

```text
type: array
minItems: 1
maxItems: 50
uniqueItems: true
items: closed object
```

Each item:

```text
element_ref: string
parameter_ref: string
```

Required on each item:

```text
element_ref
parameter_ref
```

Item `additionalProperties` is false.

Do not add `minLength` to either ref.

Empty/whitespace `element_ref` and `parameter_ref` remain opaque valid MCP strings and must reach CAP-0005 item resolution.

Null, missing, or non-string `element_ref` / `parameter_ref` fields are invalid MCP requests.

The same `element_ref` may appear with several parameter refs. The same `parameter_ref` may appear with several element refs. Pair uniqueness is on the raw pair, not on each field independently.

## Duplicate-pair enforcement

Accepted CAP-0005 uniqueness uses ordinal equality on the raw `(element_ref, parameter_ref)` strings.

Advertise `uniqueItems: true` for `reads`.

The current `ClosedSchemaArgumentValidator` compares non-string array items using raw JSON text for `uniqueItems`. That is not sufficient for CAP-0005 because JSON object property order must not allow the same semantic pair to bypass uniqueness.

Example that must be rejected as `INVALID_REQUEST` before discovery/Bridge:

```json
{
  "document_id": "opaque-document",
  "reads": [
    { "element_ref": "e1", "parameter_ref": "p1" },
    { "parameter_ref": "p1", "element_ref": "e1" }
  ]
}
```

Future implementation must reject duplicate semantic pairs before discovery/Bridge invocation even when the JSON properties are presented in a different order.

Allowed implementation approaches:

- a small targeted improvement to the existing strict validator so object uniqueness is structural; or
- an explicit SERVER-0005 pair-uniqueness guard before application-service/discovery execution.

Do not create a second general validation framework.

The behavioral invariant is more important than prescribing the internal implementation.

## Closed input enforcement

The existing Server rule from SERVER-0002 through SERVER-0004 remains mandatory: advertised closed schemas are enforced at runtime before discovery or Bridge invocation.

`revit_get_parameter_values` must use the existing `StrictInputMcpServerTool` / `ClosedSchemaArgumentValidator` rather than relying on ModelContextProtocol 2.2.0 binder behavior.

Reject as `INVALID_REQUEST`, before discovery/Bridge work, at minimum:

- missing/null `document_id`;
- missing/null `reads`;
- `reads` count outside 1..50;
- null/non-object read item;
- missing/null/non-string `element_ref`;
- missing/null/non-string `parameter_ref`;
- unexpected top-level property;
- unexpected read-item property;
- duplicate raw `(element_ref, parameter_ref)` pair, including property-order variation;
- aliases/casing such as `documentId`, `elementRef`, `parameterRef`, `instanceId`.

Do not leak binder, STJ, stack, path, or implementation detail in the error.

Do not convert malformed MCP input into `INVALID_PARAMETER_READ`.

`INVALID_PARAMETER_READ` remains the transport-neutral capability error if it is returned by the Addin/Bridge after valid MCP-boundary mapping.

## Transport-neutral mapping

Accepted MCP input maps to existing Contracts:

```text
GetParameterValuesRequest
{
    DocumentId = document_id,
    Reads = reads exactly in input order
}
```

Each pair maps unchanged to:

```text
GetParameterValueRead
{
    ElementRef = element_ref,
    ParameterRef = parameter_ref
}
```

`instance_id` is not included.

Do not add:

- PID;
- pipe name;
- paths;
- usernames;
- agent/model metadata;
- timeout input;
- unit conversion request;
- parameter display-name lookup.

No Server-generated hidden document id, element-ref translation, or parameter-ref invention is allowed.

## Strict MCP output schema

Successful structured content is exactly the transport-neutral `GetParameterValuesResult` serialized with the existing MCP JSON options.

Top-level closed object:

```text
context
items
```

Required:

```text
context
items
```

Top-level `additionalProperties` is false.

The Server must not sort, aggregate, group, retry, or rewrite CAP-0005 items.

### `context`

Closed object, same accepted shape as CAP-0004:

```text
instance_id: string
document_id: string
```

Both required.

### `items`

```text
type: array
minItems: 1
maxItems: 50
```

Exactly one item per requested pair. Input order is preserved.

Advertise explicit mutually exclusive JSON Schema variants (`oneOf` / equivalent composition). Never model this as one loose object where unrelated fields are optional.

All variants are closed (`additionalProperties = false`).

### Failure item

Required:

```text
element_ref
parameter_ref
status
```

`status` is one of:

```text
element_not_found
parameter_ref_not_found
parameter_not_present
unsupported_value
```

Failure items must not contain:

```text
data_type
has_value
value
```

### `ok` / no-value item

Required:

```text
element_ref
parameter_ref
status
data_type
has_value
```

```text
status = "ok"
has_value = false
```

`value` must be absent.

### `ok` / valued item

Required:

```text
element_ref
parameter_ref
status
data_type
has_value
value
```

```text
status = "ok"
has_value = true
```

`value` must be one of the four accepted closed variants below.

## Data type

Reuse the exact CAP-0004 `data_type` output schema semantics. Do not create a second datatype model.

Explicit closed variants:

### `measurable_spec`

Required: `kind`, `forge_type_id`.

### `spec`

Required: `kind`, `forge_type_id`.

### `category`

Required: `kind`, `forge_type_id`.

### `unknown`

Required: `kind`.

`forge_type_id` remains optional only to stay aligned with the existing CAP-0004 contract/schema shape.

Do not advertise UUID/GUID format on `forge_type_id`.

## Typed value variants

Advertise explicit mutually exclusive closed variants. The discriminator is `kind`.

Do not expose numeric Revit IDs anywhere in the output schema.

### `string`

Closed object. All required:

```text
kind = "string"
value: string, maxLength 512
truncated: boolean
```

### `integer`

Closed object. All required:

```text
kind = "integer"
value: JSON integer
```

Do not advertise Boolean/enum reinterpretation.

### `quantity`

Closed object. All required:

```text
kind = "quantity"
value: JSON number
unit_type_id: string
```

No raw/internal Revit double field.

No caller-selected unit.

### `element_reference`

Use an exact closed representation consistent with CAP-0005.

Unresolved variant. Required:

```text
kind = "element_reference"
resolved = false
```

No fabricated numeric id or `element_ref`.

Resolved variant. Required:

```text
kind = "element_reference"
resolved = true
```

Optional:

```text
name?: string, maxLength 512
element_ref?: string
```

No numeric `ElementId` field.

An ElementType-like target may therefore be:

```json
{
  "kind": "element_reference",
  "resolved": true,
  "name": "Exhaust Air"
}
```

with no `element_ref`.

## Modern success result

Preserve the existing Server convention for this slice.

For MCP revisions supporting structured output:

```text
isError = false
structuredContent = GetParameterValuesResult
content = []
```

The structured result is authoritative and is not duplicated as text.

Do not change global `McpCallResultFactory` behavior in SERVER-0005.

Legacy fallback follows the official SDK compatibility behavior already used by SERVER-0001 through SERVER-0004. Do not create custom protocol-version state.

## MCP tool errors

Tool errors remain:

```text
isError = true
structuredContent absent
one compact JSON TextContent block
```

Conceptually:

```json
{
  "code": "DOCUMENT_CONTEXT_CHANGED",
  "message": "..."
}
```

CAP-0005 item statuses are successful structured output and must never be converted into MCP tool errors:

```text
ok
element_not_found
parameter_ref_not_found
parameter_not_present
unsupported_value
```

## Instance discovery and routing

Every invocation performs fresh current-session discovery.

No selected instance or discovery snapshot is cached across MCP calls.

SERVER-0005 adds explicit resolver concepts:

```text
ResolveForGetParameterValues(...)
IsGetParameterValuesEligible(...)
BridgeProtocol.SupportsGetParameterValues(...)
```

Eligibility must use `BridgeProtocol.SupportsGetParameterValues(...)`.

Accepted CAP-0005 Bridge eligibility is exactly `{6}`:

```text
v1 -> ineligible
v2 -> ineligible
v3 -> ineligible
v4 -> ineligible
v5 -> ineligible
v6 -> eligible
v7 -> ineligible / unknown
```

Never use numeric `>=`.

Unknown future v7 supports none of these tools unless a future accepted Bridge specification explicitly guarantees it.

### Unspecified `instance_id`

Filter discovered instances to `Ready` instances whose negotiated protocol explicitly supports get-parameter-values.

Then:

```text
0 eligible -> NO_REVIT_INSTANCE
1 eligible -> auto-select
>1 eligible -> INSTANCE_REQUIRED
```

`INSTANCE_REQUIRED` candidates remain minimal and deterministic:

```text
instance_id
revit_version
revit_build
```

Sort candidates by opaque `instance_id` using ordinal ordering.

### Explicit `instance_id`

- unknown target -> `INSTANCE_NOT_FOUND`;
- known but unavailable/incompatible/not v6 eligible, including an explicit v1..v5 or v7 target -> `INSTANCE_UNAVAILABLE`;
- exact eligible v6 target -> select;
- never fall back to another instance after explicit-target failure.

Empty/whitespace explicit ids remain explicit and must not auto-select.

## Fresh Bridge invocation

After selection, create a fresh typed Bridge client for the capability call.

Required sequence:

```text
fresh discovery
-> ResolveForGetParameterValues
-> fresh typed Bridge client
-> connect selected Named Pipe
-> handshake using BridgeProtocol.SupportedVersions
     [6,5,4,3,2,1]
-> verify returned instance_id matches selected registration
-> require BridgeProtocol.SupportsGetParameterValues(selectedVersion)
-> GetParameterValuesAsync(request, existing capability timeout)
-> dispose client
```

Use the existing `BridgeProtocol.SupportedVersions` list rather than constructing an ad-hoc version array.

Use the RevitMCP-owned Bridge abstraction. Server application logic must not call StreamJsonRpc directly.

Do not call CAP-0005 on protocol v1..v5.

Connection/handshake/identity/protocol races map through the same established Server instance-unavailable model used by SERVER-0001..0004.

No retries.

## Inherited tool compatibility

SERVER-0005 must preserve the current explicit inherited support sets on v6:

```text
get-context            {2,3,4,5,6}
query-elements         {3,4,5,6}
get-elements           {4,5,6}
describe-parameters    {5,6}
get-parameter-values   {6}
```

Unknown v7 supports none until explicitly accepted.

Do not change existing CAP-0001..CAP-0004 semantics merely because the Server now knows about CAP-0005.

An unchanged get-parameter-values check using numeric `>= 6` would incorrectly accept a future v7 host.

Regression tests for CAP-0001, CAP-0002, CAP-0003, and CAP-0004 are mandatory.

## Document context guard

Server does not resolve document identity.

CAP-0005 `document_id` is required and passed unchanged to the Addin through the transport-neutral request.

The Addin validates it against the active open-document identity inside EXEC-0001.

On `DOCUMENT_CONTEXT_CHANGED`:

- preserve the stable capability code/message;
- do not retry another document;
- do not retry another Revit instance;
- do not reinterpret element refs or invent parameter refs.

## Item partial success

`GetParameterValuesResult` may contain mixed `ok` and non-`ok` item statuses.

Server must preserve this normal structured success exactly.

Do not:

- convert item statuses into tool errors;
- omit missing-item placeholders;
- reorder results;
- retry missing refs;
- enrich missing refs with diagnostics.

One output item corresponds to each input pair in the same order.

## Agent-facing error mapping

Routing:

```text
NO_REVIT_INSTANCE
INSTANCE_REQUIRED
INSTANCE_NOT_FOUND
INSTANCE_UNAVAILABLE
```

Capability, passed through unchanged when accepted:

```text
NO_ACTIVE_DOCUMENT
DOCUMENT_CONTEXT_CHANGED
INVALID_PARAMETER_READ
REVIT_EXECUTION_TIMEOUT
REVIT_EXECUTION_FAILED
```

MCP boundary:

```text
INVALID_REQUEST
```

Unknown Bridge/bootstrap failures remain internal and map to established availability/execution errors rather than leaking arbitrary codes.

## Timeouts and cancellation

Continue current Server/Bridge ownership:

- no agent-facing timeout input;
- bootstrap/handshake wait bounded by existing `ServerTimeouts`;
- capability wait uses existing bounded capability timeout policy;
- no automatic retries;
- caller cancellation remains cancellation;
- timeout after Revit execution has begun does not forcibly abort Revit API execution.

CAP-0005 does not silently increase global timeout values.

## Security and scope boundaries

SERVER-0005 must expose no:

- raw internal-unit doubles;
- numeric Revit ElementIds;
- `Parameter.Id` / `InternalDefinition.Id`;
- file paths;
- usernames;
- formulas;
- geometry;
- connectors;
- authorization decisions;
- writes;
- transactions.

`readOnlyHint` is not a write-authorization substitute.

`parameter_ref` remains document-scoped and is forgotten on successful document close. SERVER-0005 must not manufacture persistent identity across reopen.

## Dependency boundaries

Required direction remains:

```text
Contracts <- Bridge <- Server
Contracts <- Bridge <- Addin -> Revit API
```

Server must not reference Autodesk Revit API assemblies.

Contracts/Bridge must not reference MCP SDK types.

Five explicit tools do not justify a new generic framework.

## Expected implementation shape

This specification PR adds no source code.

Follow the current explicit Server pattern used by SERVER-0004. Expected later components:

```text
Cap0005JsonSchemas
GetParameterValuesToolMetadata
GetParameterValuesMcpTools
GetParameterValuesApplicationService
GetParameterValuesOutcome
GetParameterValuesToolRegistration
```

Plus explicit additions to:

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

Do not prescribe unnecessary exact filenames if repo conventions materially differ.

Do not change global `McpCallResultFactory` behavior.

No generic tool/capability framework.

## Automated validation requirements

Later implementation automated coverage must include at least:

1. `tools/list` exposes exactly five accepted tools;
2. exact CAP-0005 tool name/title/description/annotations (`readOnlyHint = true`, `openWorldHint = false`);
3. closed top-level CAP-0005 input;
4. closed nested read-pair input;
5. optional `instance_id` accepts `string | null`;
6. `document_id` required but opaque, including empty/whitespace;
7. `reads` 1..50;
8. null/missing refs rejected as `INVALID_REQUEST`;
9. unexpected nested/top-level fields rejected as `INVALID_REQUEST`;
10. duplicate semantic pairs rejected before discovery, including property-order variation;
11. empty/whitespace `element_ref` and `parameter_ref` remain accepted MCP strings;
12. exact output top-level shape `{context, items}`;
13. exact failure item shape;
14. exact `ok` / no-value shape;
15. all four typed value output variants;
16. `data_type` variants aligned with CAP-0004;
17. no numeric ElementId output field;
18. one output item per input pair in request order;
19. protocol routing v6 only;
20. v1..v5 and v7 ineligible;
21. deterministic zero/one/many instance routing;
22. explicit empty/whitespace `instance_id` never auto-selects;
23. handshake sends `[6,5,4,3,2,1]`;
24. selected v6 invokes `GetParameterValuesAsync`;
25. selected v5 or v7 does not invoke it;
26. expected Bridge capability errors survive as MCP tool errors;
27. item statuses remain successful results;
28. capability timeout mapping;
29. client disposed after every invocation;
30. fresh discovery on every invocation;
31. malformed MCP input performs no discovery/Bridge work;
32. inherited CAP-0001..CAP-0004 routing still accepts v6;
33. process-level stdio server exposes exactly five public Revit tools;
34. Contracts/Bridge/Addin dependency boundaries remain unchanged.

Do not require live Revit for Server automated tests.

Also cover:

- aliases/casing such as `documentId` / `elementRef` / `parameterRef` rejected as `INVALID_REQUEST`;
- errors have no success-shaped structuredContent;
- full CAP-0001/CAP-0002/CAP-0003/CAP-0004 Server regression suites remain green.

## Process-level validation

Using official `ModelContextProtocol` 2.2.0 against built Release `RevitMCP.Server.exe` after implementation:

```text
tools/list count = 5
```

Exactly:

```text
revit_get_context
revit_query_elements
revit_get_elements
revit_describe_parameters
revit_get_parameter_values
```

No internal Bridge RPC methods.

Process-level strict-boundary smoke must also prove a malformed CAP-0005 input returns `INVALID_REQUEST` before Revit discovery or Bridge work.

## Official MCP live validation gate

After SERVER-0005 implementation and code review, use:

- built Release `RevitMCP.Server`;
- official ModelContextProtocol 2.2.0 client;
- stdio;
- real Revit 2026.5 / `26.5.0.55`;
- real production Addin / Bridge v6;
- disposable Autodesk Snowdon HVAC copy;
- no source-model write/save.

Required end-to-end path:

```text
official MCP client
-> RevitMCP.Server stdio
-> fresh discovery/routing
-> NamedPipeBridgeClient
-> handshake v6
-> revit.get_parameter_values
-> Addin CAP-0005
-> EXEC-0001
-> Revit API
```

Required evidence:

```text
tools/list count = 5
revit_get_context
revit_query_elements
revit_get_elements
revit_describe_parameters
revit_get_parameter_values
```

Then:

```text
revit_get_context PASS
-> query Air Terminals PASS
-> describe Flow PASS
-> use the exact returned parameter_ref
-> revit_get_parameter_values PASS
-> measurable Flow returns 100 CFM with explicit unit_type_id
```

Also validate through official MCP:

- a mixed successful CAP-0005 batch where naturally available;
- unknown `parameter_ref` remains successful `parameter_ref_not_found`;
- unknown `element_ref` + valid `parameter_ref` remains successful `element_not_found`;
- wrong `document_id` -> `DOCUMENT_CONTEXT_CHANGED`;
- malformed CAP-0005 input -> `INVALID_REQUEST` before discovery/Bridge;
- modern success has authoritative `structuredContent` and `content = []`;
- errors have `isError = true`, no success-shaped `structuredContent`, and compact JSON TextContent;
- no Revit transaction/model modification/save.

Typed Bridge live evidence from PR #38 does not need to be re-manufactured solely to observe naturally unavailable `unsupported_value`.

Record missing natural coverage honestly.

## Acceptance criteria

SERVER-0005 is accepted as a specification when:

1. It adds exactly one new MCP tool, `revit_get_parameter_values`, without claiming that five tools exist in the current runtime.
2. Tool name, title, description, and annotations match accepted CAP-0005 metadata exactly.
3. MCP input is a closed JSON Schema 2020-12 object with required `document_id` and `reads`, optional `instance_id` as `string | null`, and `additionalProperties = false`.
4. `reads` is 1..50 unique explicit pairs. Semantic pair uniqueness uses ordinal equality on the raw strings and must reject property-order duplicates before discovery/Bridge.
5. Opaque-id behavior is preserved: no `minLength` on `document_id`, `element_ref`, or `parameter_ref`; empty/whitespace present strings are not omitted; empty/whitespace `instance_id` never auto-selects.
6. Accepted MCP input maps to `GetParameterValuesRequest` / `GetParameterValueRead` with request order preserved and without `instance_id`.
7. Output is a closed `GetParameterValuesResult` with exact failure, `ok`/no-value, `ok`/valued, `data_type`, and typed-value variants. No numeric ElementId field.
8. Modern success uses `isError = false`, authoritative `structuredContent`, and `content = []`. Errors use `isError = true`, no success-shaped structured content, and one compact JSON TextContent block.
9. Routing is explicit `{6}` via `BridgeProtocol.SupportsGetParameterValues`. v1..v5 and unknown v7 are ineligible. Never numeric `>=`.
10. Every invocation performs fresh discovery, handshake with `[6,5,4,3,2,1]`, typed `GetParameterValuesAsync`, and client disposal. No retry/fallback.
11. Inherited tool compatibility remains `{2,3,4,5,6}`, `{3,4,5,6}`, `{4,5,6}`, `{5,6}`. CAP-0001..CAP-0004 semantics are unchanged.
12. Implementation remains explicit SERVER-0004-shaped components. No generic framework, capability registry, or MCP SDK upgrade.
13. Later automated tests cover the listed schema, uniqueness, routing, handshake, timeout, disposal, and inherited-regression cases without live Revit.
14. Official MCP-client-to-Revit-2026.5 live validation is required after implementation.
15. No writes, write authorization, transactions, caller-selected units, or persistent refs across reopen.

SERVER-0005 is accepted as implemented only after a later implementation PR satisfies those criteria, automated coverage, and the official MCP live gate.

## Explicitly deferred

SERVER-0005 must not introduce:

- writes;
- write authorization;
- write preview/approval;
- transaction design;
- caller-selected unit conversion;
- persistent parameter refs;
- linked-document parameter reads;
- hidden/API-only parameter reads;
- generic capability registry;
- dynamic tool-list exposure;
- Streamable HTTP;
- MCP Apps;
- APS/ACC/Forma;
- orchestration;
- WebMCP implementation;
- UI work;
- MCP SDK upgrade.

## References

- SERVER-0001: stdio `revit_get_context`
- SERVER-0002: stdio `revit_query_elements`
- SERVER-0003: stdio `revit_get_elements`
- SERVER-0004: stdio `revit_describe_parameters`
- CAP-0004: `revit_describe_parameters`
- CAP-0005: `revit_get_parameter_values`
- ADR-0005: Agent context and token efficiency
- ADR-0006: Document and element reference identity
- ADR-0007: Federated MCP boundaries and optional orchestration
- BRIDGE-0006: `revit.get_parameter_values` and protocol v6
