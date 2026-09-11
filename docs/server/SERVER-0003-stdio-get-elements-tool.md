# SERVER-0003: stdio `revit_get_elements` tool

- Status: Accepted
- Date: 2026-09-10

## Purpose

Expose accepted CAP-0003 `revit_get_elements` through the existing standalone `RevitMCP.Server` stdio MCP process while preserving CAP-0001, CAP-0002, strict MCP input handling, and the established Server/Bridge/Addin boundaries.

SERVER-0003 extends SERVER-0001 and SERVER-0002. It does not create a new server process, transport, or generic capability framework.

## Scope

- expose exactly one additional MCP tool: `revit_get_elements`;
- preserve `revit_get_context` and `revit_query_elements`;
- use existing fresh current-Windows-session discovery and deterministic instance routing;
- negotiate Bridge protocol v4 for CAP-0003;
- preserve typed Named Pipe clients and per-call fresh capability invocation;
- require CAP-0003 `document_id` context guarding end to end;
- advertise strict bounded projection/input/output schemas;
- enforce closed MCP input before discovery/Bridge work;
- preserve ADR-0005 structured-content/token behavior;
- preserve explicit capability-version sets rather than numeric comparisons.

## Non-goals

- CAP-0004 or another new capability;
- all-parameter/full element dumps;
- parameter discovery/list-all names;
- language-independent parameter identity;
- parameter ids/GUIDs for future writes;
- machine-readable raw quantities/unit conversion;
- geometry, bounding boxes, connectors, or MEP topology;
- new MCP transports or remote HTTP hosting;
- generic tool/capability registries;
- inactive-document addressing;
- write operations, transactions, approvals, or authorization;
- Revit product UI.

## MCP SDK and hosting

Continue the accepted Server stack:

```text
ModelContextProtocol 2.2.0
.NET 10
stdio transport
```

No MCP SDK dependency may be introduced into Contracts, Bridge, or Addin.

stdout remains reserved for MCP protocol traffic. Diagnostics must not contaminate stdout.

Tool registration remains explicit. Do not switch to assembly-wide scanning.

After SERVER-0003 implementation, `tools/list` exposes exactly:

```text
revit_get_context
revit_query_elements
revit_get_elements
```

No Bridge/internal JSON-RPC method is exposed directly as an MCP tool.

## Tool metadata

### Name

`revit_get_elements`

### Title

`Get Revit Elements`

### Description

Inspect a bounded set of Revit element references in the active document and return only explicitly requested fields and named parameters.

### Annotations

```text
readOnlyHint = true
openWorldHint = false
```

## MCP input schema

The MCP schema is a closed JSON Schema 2020-12 object matching CAP-0003.

Top-level fields:

```text
instance_id?: string
document_id: string
element_refs: string[1..10]
projection: object
```

Required:

```text
document_id
element_refs
projection
```

Top-level `additionalProperties` is false.

### Opaque ids

`instance_id`, `document_id`, and `element_ref` values remain opaque strings.

Do not advertise UUID/GUID formats or parse them in Server code.

For `instance_id` and `document_id`, empty/whitespace values remain explicit values. Optional `instance_id` omitted or JSON `null` both mean unspecified, matching CAP-0003 routing. Required MCP fields that are missing, JSON `null`, or an uninterpretable JSON type are `INVALID_REQUEST` at the Server boundary.

`document_id` is required for CAP-0003 and is not defaulted from another MCP call or hidden Server state.

### `element_refs`

Schema:

```text
type: array
minItems: 1
maxItems: 10
uniqueItems: true
items:
  type: string
```

Do not add numeric `ElementId` alternatives.

Result order must preserve input order; Server must not sort refs.

Transport-neutral validation remains responsible for the accepted ordinal uniqueness/business semantics and maps violations to `INVALID_INSPECTION` where they reach the capability layer.

### `projection`

Closed object with only:

```text
fields
parameter_names
```

At least one of the two properties must be present, and any supplied array must be non-empty.

Schema should use JSON Schema 2020-12 composition such as `anyOf`/required clauses rather than inventing placeholder values.

#### `fields`

Optional:

```text
type: array
minItems: 1
maxItems: 5
uniqueItems: true
items enum:
  name
  category_name
  family_name
  type_name
  level_name
```

No aliases or extra field selectors.

#### `parameter_names`

Optional:

```text
type: array
minItems: 1
maxItems: 10
items:
  type: string
  minLength: 1
  maxLength: 256
```

Exact duplicate strings should be rejected by schema where practical. The stricter CAP-0003 runtime rule also rejects duplicates under `OrdinalIgnoreCase`.

No wildcard, regex, `all`, empty-name convention, or omit-to-return-all behavior is permitted.

## Closed input enforcement

The existing Server rule from SERVER-0002 remains mandatory: advertised closed schemas are enforced at runtime before discovery or Bridge invocation.

`revit_get_elements` must use the existing Server-local strict MCP tool wrapper/policy rather than relying on ModelContextProtocol 2.2.0 binder behavior.

Reject as `INVALID_REQUEST`, before discovery/Bridge work:

- unexpected top-level properties;
- casing/alias typos such as `documentId`, `elementRefs`, or `instanceId`;
- unexpected nested projection properties;
- binder/JSON shape failures that prevent interpretation of the MCP call.

Do not leak binder, STJ, stack, path, or implementation detail in the error.

This boundary error remains distinct from CAP-0003 `INVALID_INSPECTION`, which is transport-neutral capability validation.

## Transport-neutral mapping

The MCP adapter maps accepted input into:

```text
GetElementsRequest
{
    document_id
    element_refs
    projection
}
```

`instance_id` remains a Server routing field and must not enter the transport-neutral capability request.

No Server-generated hidden document id or element-ref translation is allowed.

## Strict MCP output schema

Successful structured content top-level:

```text
context
  instance_id: string
  document_id: string
elements: array[1..10]
```

Top-level and nested objects are closed.

Each element result is one of two shapes.

### `not_found` result item

Exact conceptual shape:

```json
{
  "element_ref": "opaque-ref",
  "status": "not_found"
}
```

No projected fields or parameter fields.

### `ok` result item

Required:

```text
element_ref: string
status: const "ok"
```

Optional projection-dependent properties:

```text
name: string | null
category_name: string | null
family_name: string | null
type_name: string | null
level_name: string | null
parameters: parameter[]
parameters_truncated: boolean
```

Output schema cannot know the call's requested projection, so these are schema-optional. Runtime CAP-0003 semantics remain stricter:

- requested fields are present, nullable if unavailable;
- unrequested basic fields are absent;
- `parameters` and `parameters_truncated` are present together only when `parameter_names` was requested.

Where useful, JSON Schema 2020-12 `dependentRequired` may enforce the pairwise presence of `parameters` and `parameters_truncated`.

### Parameter output schema

At most 20 entries per `ok` item.

Each parameter object is closed and exactly:

```text
name: string
source: "instance" | "type"
value_text: string | null, maxLength 512 when string
value_truncated: boolean
```

Do not add:

- storage type;
- raw value;
- unit id;
- parameter id/GUID;
- definition metadata;
- editability/writeability;
- group metadata.

The output schema must not expose any numeric Revit `ElementId` as a durable handle or parameter fallback.

## Modern success result

For MCP revisions supporting structured output:

```text
isError = false
structuredContent = GetElementsResult
content = []
```

The structured result is authoritative and is not duplicated as text.

Legacy fallback follows the official SDK compatibility behavior already used by SERVER-0001/0002. Do not create custom protocol-version state.

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

`not_found` is a success item status and must not be rewritten as an MCP tool error.

## Instance discovery and routing

Every invocation performs fresh current-session discovery.

No selected instance or discovery snapshot is cached across MCP calls.

### Explicit capability sets after v4

SERVER-0003 updates current capability eligibility to:

```text
revit_get_context      -> protocol {2, 3, 4}
revit_query_elements   -> protocol {3, 4}
revit_get_elements     -> protocol {4}
```

Never use numeric `>=` comparisons.

Unknown future v5 is ineligible for all capability checks until an accepted Bridge specification explicitly defines its guarantees.

### Unspecified `instance_id`

Filter discovered instances to `Ready` instances whose negotiated protocol explicitly supports CAP-0003.

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
- known but unavailable/incompatible/v1/v2/v3 target -> `INSTANCE_UNAVAILABLE`;
- eligible exact v4 target -> select;
- never fall back to another instance after explicit-target failure.

Empty/whitespace explicit ids remain explicit.

### Resolver design

Reuse the existing shared 0/1/many selection algorithm and add a small explicit CAP-0003 entry point such as `ResolveForGetElements`.

Do not add a generic capability registry/descriptor system merely because three tools now share routing mechanics.

Capability eligibility should remain supplied by explicit code/tests and current Bridge support helpers.

## Fresh Bridge invocation

After selection, create a fresh typed Bridge client for the capability call.

Required sequence:

```text
fresh discovery
-> select exact v4-capable instance
-> connect selected registered pipe
-> bridge.handshake
     expected_instance_id = selected id
     supported_versions = [4,3,2,1]
-> require BridgeProtocol.SupportsGetElements(selectedVersion)
-> GetElementsAsync(...)
-> dispose client
```

Use the RevitMCP-owned Bridge abstraction. Do not use StreamJsonRpc directly in Server application logic.

Connection/handshake/identity/protocol races map to the established `INSTANCE_UNAVAILABLE` model where appropriate.

No retries.

## CAP-0001 and CAP-0002 compatibility after protocol v4

SERVER-0003 must not regress existing tools when a current Addin negotiates v4.

Mandatory explicit behavior:

```text
get-context after v2 -> allowed
get-context after v3 -> allowed
get-context after v4 -> allowed

query-elements after v3 -> allowed
query-elements after v4 -> allowed

get-elements only after v4 -> allowed
```

v1 and unknown future v5 remain unsupported unless explicitly documented.

This means implementation must update existing Server v3-only assumptions. An unchanged query check requiring exactly `3` would break CAP-0002 against the new v4 host.

Regression tests for CAP-0001 and CAP-0002 are mandatory.

## Document context guard

Server does not resolve document identity.

CAP-0003 `document_id` is required and passed unchanged to the Addin through the transport-neutral request.

The Addin validates it against the active open-document identity inside EXEC-0001.

On `DOCUMENT_CONTEXT_CHANGED`:

- preserve the stable capability code/message;
- do not retry another document;
- do not retry another Revit instance;
- do not reinterpret element refs.

## Item partial success

`GetElementsResult` may contain both `ok` and `not_found` items.

Server must preserve this normal structured success exactly.

Do not:

- convert `not_found` items into tool errors;
- omit missing-item placeholders;
- reorder results;
- retry missing refs;
- enrich missing refs with diagnostics.

One output item corresponds to each input ref in the same order.

## Agent-facing error mapping

Routing:

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
INVALID_INSPECTION
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
- bootstrap/handshake wait bounded by Server policy;
- capability wait uses existing bounded capability timeout policy;
- no automatic retries;
- caller cancellation remains cancellation;
- timeout after Revit execution has begun does not forcibly abort Revit API execution.

CAP-0003 does not silently increase global timeout values. Live evidence should inform any later policy change.

## Dependency boundaries

Required direction remains:

```text
Contracts <- Bridge <- Server
Contracts <- Bridge <- Addin -> Revit API
```

Server must not reference Autodesk Revit API assemblies.

Contracts/Bridge must not reference MCP SDK types.

Three explicit tools do not justify a new generic framework.

## Automated validation requirements

At minimum cover:

- `tools/list` exposes exactly three accepted tools;
- exact CAP-0003 tool name/title/description/annotations;
- strict closed top-level input;
- required `document_id`, `element_refs`, `projection`;
- refs 1..10 and `uniqueItems`;
- projection only exposes `fields` and `parameter_names`;
- at least one projection property is required;
- fields enum/min/max/unique rules;
- parameter names 1..10 and string length 1..256;
- no all/wildcard/full parameter mode;
- exact closed output/result-item/parameter schemas;
- parameter output max 20 and value text max 512;
- raw MCP unexpected top-level/nested properties rejected before discovery/Bridge;
- malformed alias/casing such as `documentId`/`elementRefs` rejected as `INVALID_REQUEST`;
- CAP-0003 v4-only routing;
- CAP-0001 eligibility `{2,3,4}`;
- CAP-0002 eligibility `{3,4}`;
- unknown v5 incompatible;
- deterministic 0/1/many routing and minimal candidates;
- explicit v3 CAP-0003 target -> `INSTANCE_UNAVAILABLE`;
- fresh discovery/client/handshake each call;
- handshake sends `[4,3,2,1]` and expected identity;
- exact transport-neutral request mapping with no `instance_id`;
- mixed `ok`/`not_found` structured success preserved in order;
- no unrequested projection fields added by Server;
- capability errors preserved/mapped deterministically;
- modern success structuredContent + empty content;
- errors no success-shaped structuredContent;
- legacy compatibility remains current SDK behavior;
- CAP-0001/CAP-0002 full Server regression suites remain green;
- dependency boundaries remain intact.

## Process-level validation

Using official `ModelContextProtocol` 2.2.0 against built Release `RevitMCP.Server.exe`:

```text
tools/list count = 3
```

Exactly:

```text
revit_get_context
revit_query_elements
revit_get_elements
```

No internal Bridge RPC methods.

Process-level strict-boundary smoke should also prove a CAP-0003 unexpected property returns `INVALID_REQUEST` before Revit discovery work.

## Live validation

Before CAP-0003 is considered end-to-end implemented, validate on Autodesk Revit 2026.5 using a modeled Autodesk sample such as Snowdon Towers Sample HVAC.

Required path:

```text
official MCP client
-> stdio RevitMCP.Server
-> revit_query_elements
-> obtain current document_id + element_refs
-> revit_get_elements
-> fresh discovery
-> Named Pipe handshake [4,3,2,1]
-> protocol 4
-> revit.get_elements
-> EXEC-0001 / ExternalEvent
-> Revit API projection
-> bounded structuredContent
```

Required evidence:

- `tools/list` exactly three tools;
- use `revit_query_elements` to obtain real refs rather than inventing UniqueIds;
- basic-only inspection of at least two refs;
- parameter-only inspection with parameter names observed in the Autodesk sample;
- mixed basic + parameter inspection;
- input order preserved;
- one mixed batch containing an unknown/deleted ref returns item-level `not_found` while valid refs remain `ok`;
- unavailable requested basic field is represented by explicit `null` where a safe sample case exists, otherwise automated coverage;
- named parameters show `instance` / `type` source correctly;
- duplicate visible display-name handling live if a convenient sample case exists, otherwise automated coverage;
- parameter value truncation automated, with live demonstration only if naturally available;
- explicit required `document_id` succeeds on the same active document;
- active-document switch causes old id -> `DOCUMENT_CONTEXT_CHANGED`;
- modern success has structuredContent and `content=[]`;
- representative basic-only / parameter-only / mixed / partial-not-found UTF-8 sizes recorded;
- CAP-0001 and CAP-0002 both succeed against the same v4 host;
- no model writes/transactions;
- no production/client model used or committed.

## Acceptance criteria

SERVER-0003 is accepted as implemented when:

1. exactly three MCP tools are exposed;
2. `revit_get_elements` matches CAP-0003 input/output semantics;
3. strict MCP input is enforced before discovery/Bridge;
4. CAP-0003 routing requires explicit protocol 4;
5. CAP-0001 recognizes `{2,3,4}` and CAP-0002 recognizes `{3,4}`;
6. unknown future versions are not accepted numerically;
7. every invocation uses fresh discovery and a fresh typed Bridge capability call;
8. required document guard passes end to end without Server-side identity invention;
9. partial `not_found` results remain normal structured success;
10. result projection remains bounded and contains no unrequested element body/full parameter dump;
11. no new generic framework or lower-layer coupling is introduced;
12. automated tests and full existing suites pass;
13. Server Release and Revit 2025/2026/2027 Addin builds pass;
14. real official-MCP-client-to-Revit-2026.5 CAP-0003 validation passes with CAP-0001/CAP-0002 regressions.

## Explicitly deferred

- CAP-0004;
- all-parameter/full element modes;
- parameter discovery and language-independent identity;
- write-target parameter identity;
- raw machine quantities/unit conversion;
- geometry/connectors/MEP topology;
- linked-document addressing;
- write operations/approval/authorization;
- Streamable HTTP/Azure/cloud/WebMCP/MCP Apps implementation;
- Revit UI.

## References

- SERVER-0001: stdio `revit_get_context`
- SERVER-0002: stdio `revit_query_elements`
- CAP-0003: `revit_get_elements`
- ADR-0005: Agent context and token efficiency
- ADR-0006: Document and element reference identity
- BRIDGE-0004: `revit.get_elements` and protocol v4
