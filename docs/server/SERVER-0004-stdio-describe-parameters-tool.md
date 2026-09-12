# SERVER-0004: stdio `revit_describe_parameters` tool

- Status: Accepted
- Date: 2026-09-12

## Purpose

Expose accepted CAP-0004 `revit_describe_parameters` through the existing standalone `RevitMCP.Server` stdio MCP process while preserving CAP-0001, CAP-0002, CAP-0003, strict MCP input handling, and the established Server/Bridge/Addin boundaries.

SERVER-0004 extends SERVER-0001 through SERVER-0003. It does not create a new server process, transport, or generic capability framework.

## Scope

- expose exactly one additional MCP tool: `revit_describe_parameters`;
- preserve `revit_get_context`, `revit_query_elements`, and `revit_get_elements`;
- use existing fresh current-Windows-session discovery and deterministic instance routing;
- negotiate Bridge protocol v5 for CAP-0004;
- preserve typed Named Pipe clients and per-call fresh capability invocation;
- require CAP-0004 `document_id` context guarding end to end;
- advertise strict bounded input/output schemas, including mutually exclusive identity and data-type variants;
- enforce closed MCP input before discovery/Bridge work;
- align advertised `instance_id` schemas for all four Server tools as `string | null`;
- preserve ADR-0005 structured-content/token behavior;
- preserve explicit capability-version sets rather than numeric comparisons.

## Non-goals

- typed parameter values or unit conversion;
- parameter-value filtering;
- all-parameter/full-document catalogs;
- hidden/API-only parameter enumeration;
- writes, transactions, approvals, or authorization;
- preview/apply workflows;
- linked-document addressing;
- persistent `parameter_ref` reuse after document reopen;
- new MCP transports or remote HTTP hosting;
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

No MCP SDK dependency may be introduced into Contracts, Bridge, or Addin.

stdout remains reserved for MCP protocol traffic. Diagnostics must not contaminate stdout.

Tool registration remains explicit. Do not switch to assembly-wide scanning.

After SERVER-0004 implementation, `tools/list` exposes exactly:

```text
revit_get_context
revit_query_elements
revit_get_elements
revit_describe_parameters
```

No Bridge/internal JSON-RPC method is exposed directly as an MCP tool.

## Tool metadata

### Name

`revit_describe_parameters`

### Title

`Describe Revit Parameters`

### Description

Discover visible parameter definitions on a bounded set of known Revit element references and return opaque parameter identity plus data-type semantics, without parameter values.

### Annotations

```text
readOnlyHint = true
openWorldHint = false
```

## MCP input schema

The MCP schema is a closed JSON Schema 2020-12 object matching CAP-0004.

Top-level fields:

```text
instance_id?: string | null
document_id: string
element_refs: string[1..10]
source?: "instance" | "type" | "both"
name_contains?: string
limit?: integer
```

Required:

```text
document_id
element_refs
```

Top-level `additionalProperties` is false.

### Opaque ids

`instance_id`, `document_id`, and `element_ref` values remain opaque strings.

Do not advertise UUID/GUID formats or parse them in Server code.

`document_id` is required for CAP-0004 and is not defaulted from another MCP call or hidden Server state.

Required MCP fields that are missing, JSON `null`, or an uninterpretable JSON type are `INVALID_REQUEST` at the Server boundary.

### Accepted `instance_id` schema cleanup

SERVER-0004 records an accepted schema/contract alignment for all four Server tool input schemas.

Advertise optional `instance_id` as:

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

This cleanup does not change routing semantics. It aligns advertised JSON Schema with the already-accepted omitted/null meaning from SERVER-0001 through SERVER-0003.

Do not extend this cleanup beyond `instance_id`.

Do not apply `["string", "null"]` to required `document_id`, `element_refs`, result `context` ids, or `parameter_ref`.

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

Result `elements[]` order must preserve input order; Server must not sort refs.

Transport-neutral validation remains responsible for the accepted ordinal uniqueness/business semantics and maps violations to `INVALID_PARAMETER_DISCOVERY` where they reach the capability layer.

### `source`

Optional enum:

```text
instance
type
both
```

Default: `both`.

JSON Schema `default` is descriptive only. The MCP implementation must explicitly map omitted `source` to transport-neutral `DescribeParameterSource.Both` when constructing `DescribeParametersRequest`.

Result descriptors always contain `source = instance` or `source = type`. Never `both`.

### `name_contains`

Optional:

```text
type: string
minLength: 1
maxLength: 256
```

No trimming or normalization at the MCP boundary.

Empty string is invalid MCP input (`INVALID_REQUEST`), not an omit-to-match-all convention.

### `limit`

Optional:

```text
type: integer
minimum: 1
maximum: 100
default: 50
```

JSON Schema `default` is descriptive only. The MCP implementation must explicitly map omitted `limit` to `50` when constructing `DescribeParametersRequest`.

The limit applies to unique returned parameter descriptors after aggregation, not raw parameter occurrences.

## Closed input enforcement

The existing Server rule from SERVER-0002/SERVER-0003 remains mandatory: advertised closed schemas are enforced at runtime before discovery or Bridge invocation.

`revit_describe_parameters` must use the existing Server-local strict MCP tool wrapper/policy rather than relying on ModelContextProtocol 2.2.0 binder behavior.

The current `ClosedSchemaArgumentValidator` does not yet cover all advertised `enum`, range, `uniqueItems`, and optional-property type constraints. SERVER-0004 may extend that existing strict mechanism, with regression tests, so invalid MCP inputs are rejected before discovery/Bridge. Do not add a second validation framework. Do not change the behavior of valid CAP-0001/CAP-0002/CAP-0003 inputs.

Reject as `INVALID_REQUEST`, before discovery/Bridge work:

- unexpected top-level properties;
- casing/alias typos such as `documentId`, `elementRefs`, `instanceId`, `nameContains`;
- invalid enum values;
- invalid array shape, uniqueness, or bounds;
- invalid nullability/type, including required fields supplied as JSON `null`;
- binder/JSON shape failures that prevent interpretation of the MCP call.

Do not leak binder, STJ, stack, path, or implementation detail in the error.

This boundary error remains distinct from CAP-0004 `INVALID_PARAMETER_DISCOVERY`, which is transport-neutral capability validation.

## Transport-neutral mapping

The MCP adapter maps accepted input into existing:

```text
DescribeParametersRequest
{
    document_id
    element_refs
    source
    name_contains
    limit
}
```

`instance_id` remains a Server routing field and must not enter the transport-neutral capability request.

Do not add PID, pipe name, path, user, provider/model metadata, agent timeout, or MCP types to Contracts.

No Server-generated hidden document id, element-ref translation, or parameter-ref invention is allowed.

## Strict MCP output schema

Successful structured content top-level is closed and exactly:

```text
context
elements
matched_count
truncated
parameters
```

### `context`

Closed object:

```text
instance_id: string
document_id: string
```

### `elements`

```text
type: array
minItems: 1
maxItems: 10
```

Exactly one item per requested ref, request order preserved.

Each item is a closed object:

```text
element_ref: string
status: "ok" | "not_found"
```

`not_found` is a normal successful item status.

### `matched_count`

```text
type: integer
minimum: 0
```

Exact count of unique parameter descriptors after filtering and before `limit`.

### `truncated`

```text
type: boolean
```

True when `matched_count` exceeds the number of returned `parameters`.

### `parameters`

```text
type: array
maxItems: 100
```

Each descriptor is closed and requires:

```text
parameter_ref: string
name: string
source: "instance" | "type"
identity: object
data_type: object
present_on_count: integer 1..10
read_only_on_count: integer 0..10
```

Do not expose parameter values, `value_text`, raw doubles, storage type, units, formulas, editability flags, or write authorization.

`parameter_ref` remains an opaque string. Do not advertise UUID/GUID format.

## Identity schema

`identity` uses explicit mutually exclusive JSON Schema 2020-12 variants (`oneOf` / equivalent composition).

Never model this as one loose object where unrelated fields are optional.

All variants are closed (`additionalProperties = false`).

### `built_in`

```text
kind = "built_in"
parameter_type_id = string
```

Required: `kind`, `parameter_type_id`.

No `guid`.

### `shared`

```text
kind = "shared"
guid = string
```

Required: `kind`, `guid`.

No `parameter_type_id`.

Do not require JSON Schema `format: uuid`. Canonical shared-GUID formatting remains a CAP-0004 semantic rule, not a Server schema format annotation.

### `local`

```text
kind = "local"
```

Required: `kind`.

No `parameter_type_id` and no `guid`.

Transport-neutral Contracts already use a single `DescribeParameterIdentity` class with a kind discriminator plus optional portable fields. SERVER-0004 does not change that contract type. The MCP output schema is the stricter advertised shape; Server mapping must serialize only the fields that belong to the selected kind.

## Data type schema

`data_type` uses explicit mutually exclusive variants.

All variants are closed.

### `measurable_spec`

```text
kind = "measurable_spec"
forge_type_id = string
```

### `spec`

```text
kind = "spec"
forge_type_id = string
```

### `category`

```text
kind = "category"
forge_type_id = string
```

### `unknown`

```text
kind = "unknown"
forge_type_id? = string
```

`unknown` may omit `forge_type_id` when Revit reports an empty data type.

`measurable_spec`, `spec`, and `category` require `forge_type_id`.

Do not advertise UUID/GUID format on `forge_type_id`.

## Modern success result

Preserve the existing Server convention for this slice.

For MCP revisions supporting structured output:

```text
isError = false
structuredContent = DescribeParametersResult
content = []
```

The structured result is authoritative and is not duplicated as text.

Do not change global `McpCallResultFactory` behavior in SERVER-0004.

Legacy fallback follows the official SDK compatibility behavior already used by SERVER-0001 through SERVER-0003. Do not create custom protocol-version state.

Broader MCP backward-compatibility review for duplicated text plus structured content is deferred to a separate compatibility audit.

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

### Explicit capability sets after BRIDGE-0005

SERVER-0004 updates current capability eligibility to:

```text
revit_get_context          -> {2,3,4,5}
revit_query_elements       -> {3,4,5}
revit_get_elements         -> {4,5}
revit_describe_parameters  -> {5}
```

Never use numeric `>=` comparisons.

Unknown future v6 supports none of these tools unless a future accepted Bridge specification explicitly guarantees it.

### Unspecified `instance_id`

Filter discovered instances to `Ready` instances whose negotiated protocol explicitly supports CAP-0004.

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
- known but unavailable/incompatible/not CAP-0004 eligible, including an explicit v4 target -> `INSTANCE_UNAVAILABLE`;
- eligible exact v5 target -> select;
- never fall back to another instance after explicit-target failure.

Empty/whitespace explicit ids remain explicit.

### Resolver design

Reuse the existing shared 0/1/many selection algorithm and add a small explicit CAP-0004 entry point:

```text
ResolveForDescribeParameters(...)
IsDescribeParametersEligible(...)
BridgeProtocol.SupportsDescribeParameters(...)
```

`BridgeProtocol.SupportsDescribeParameters` already exists and remains the explicit `{5}` helper. Do not replace it with a generic capability registry/descriptor system.

Eligibility should remain supplied by explicit code/tests and current Bridge support helpers.

## Fresh Bridge invocation

After selection, create a fresh typed Bridge client for the capability call.

Required sequence:

```text
fresh discovery
-> select CAP-0004-compatible instance
-> fresh typed Bridge client
-> bridge.handshake
     expected_instance_id = selected id
     supported_versions = [5,4,3,2,1]
-> require BridgeProtocol.SupportsDescribeParameters(selectedVersion)
-> DescribeParametersAsync(...)
-> dispose client
```

Use the existing `BridgeProtocol.SupportedVersions` list rather than constructing an ad-hoc version array.

Use the RevitMCP-owned Bridge abstraction. Server application logic must not call StreamJsonRpc directly.

Connection/handshake/identity/protocol races map to the established `INSTANCE_UNAVAILABLE` model where appropriate.

No retries.

## CAP-0001, CAP-0002, and CAP-0003 compatibility after protocol v5

SERVER-0004 must not regress existing tools when a current Addin negotiates v5.

Mandatory explicit behavior:

```text
get-context after {2,3,4,5} -> allowed
query-elements after {3,4,5} -> allowed
get-elements after {4,5} -> allowed
describe-parameters after {5} only -> allowed
```

v1 and unknown future v6 remain unsupported unless explicitly documented.

An unchanged describe check using numeric `>= 5` would incorrectly accept a future v6 host. An unchanged get-elements check requiring exactly `4` would break CAP-0003 against the current v5 host; that inherited `{4,5}` set is already implemented and must remain green.

Regression tests for CAP-0001, CAP-0002, and CAP-0003 are mandatory.

## Document context guard

Server does not resolve document identity.

CAP-0004 `document_id` is required and passed unchanged to the Addin through the transport-neutral request.

The Addin validates it against the active open-document identity inside EXEC-0001.

On `DOCUMENT_CONTEXT_CHANGED`:

- preserve the stable capability code/message;
- do not retry another document;
- do not retry another Revit instance;
- do not reinterpret element refs or invent parameter refs.

## Item partial success

`DescribeParametersResult` may contain both `ok` and `not_found` items.

Server must preserve this normal structured success exactly.

Do not:

- convert `not_found` items into tool errors;
- omit missing-item placeholders;
- reorder results;
- retry missing refs;
- enrich missing refs with diagnostics.

One output item corresponds to each input ref in the same order.

Descriptors are produced only from `ok` elements.

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
INVALID_PARAMETER_DISCOVERY
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

CAP-0004 does not silently increase global timeout values. Live evidence should inform any later policy change.

## Security and scope boundaries

SERVER-0004 must expose no:

- parameter values;
- raw doubles;
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

Built-in ForgeTypeIds and shared parameter GUIDs are intentional semantic metadata.

`read_only_on_count` remains observational. `0` does not authorize a future write.

## Dependency boundaries

Required direction remains:

```text
Contracts <- Bridge <- Server
Contracts <- Bridge <- Addin -> Revit API
```

Server must not reference Autodesk Revit API assemblies.

Contracts/Bridge must not reference MCP SDK types.

Four explicit tools do not justify a new generic framework.

## Expected implementation shape

Follow the current explicit Server pattern. Approximate filenames:

```text
Cap0004JsonSchemas
DescribeParametersToolMetadata
DescribeParametersMcpTools
DescribeParametersApplicationService
DescribeParametersOutcome
DescribeParametersToolRegistration
```

Plus explicit additions to `InstanceTargetResolver` and explicit registration in `ServerHost`.

Do not prescribe unnecessary exact filenames if repo conventions materially differ.

Reuse `StrictInputMcpServerTool` / `ClosedSchemaArgumentValidator` rather than adding a second closed-input mechanism. Extending the existing validator is allowed so CAP-0004 advertised `enum`, range, `uniqueItems`, and optional-property type constraints are enforced before discovery/Bridge. Valid CAP-0001/CAP-0002/CAP-0003 inputs must keep their current accepted behavior.

Do not change global `McpCallResultFactory` behavior.

No generic tool/capability framework.

## Automated validation requirements

At minimum cover:

1. `tools/list` exposes exactly four accepted tools;
2. exact CAP-0004 tool name/title/description/annotations;
3. strict closed top-level input;
4. `instance_id` accepts `string | null` and remains optional;
5. required `document_id`;
6. refs 1..10 and uniqueness;
7. source enum and default `both` semantics;
8. `name_contains` 1..256 bounds;
9. `limit` 1..100 and default `50` semantics;
10. closed output with exactly `context`, `elements`, `matched_count`, `truncated`, `parameters`;
11. exact mutually exclusive identity variants;
12. exact mutually exclusive data-type variants, including `unknown` omitting `forge_type_id`;
13. no parameter-value fields;
14. raw unexpected properties rejected before discovery/Bridge;
15. CAP-0004 routing only on v5;
16. inherited capability sets remain `{2,3,4,5}`, `{3,4,5}`, and `{4,5}`;
17. unknown v6 incompatible;
18. deterministic 0/1/many routing and minimal candidates;
19. explicit v4 describe target -> `INSTANCE_UNAVAILABLE`;
20. fresh discovery/client/handshake each call;
21. handshake sends `[5,4,3,2,1]` and expected identity;
22. `instance_id` absent from the transport-neutral request;
23. mixed `ok`/`not_found` structured success preserved in order;
24. `DOCUMENT_CONTEXT_CHANGED` preserved without retry/fallback;
25. modern success structuredContent + empty content;
26. dependency boundaries remain intact;
27. full CAP-0001/CAP-0002/CAP-0003 Server regression suites remain green.

Also cover:

- the accepted `instance_id` `string | null` advertisement on the four Server tool schemas;
- malformed alias/casing such as `documentId`/`elementRefs` rejected as `INVALID_REQUEST`;
- errors have no success-shaped structuredContent.

## Process-level validation

Using official `ModelContextProtocol` 2.2.0 against built Release `RevitMCP.Server.exe`:

```text
tools/list count = 4
```

Exactly:

```text
revit_get_context
revit_query_elements
revit_get_elements
revit_describe_parameters
```

No internal Bridge RPC methods.

Process-level strict-boundary smoke must also prove a malformed CAP-0004 input returns `INVALID_REQUEST` before Revit discovery or Bridge work.

## Live validation

Before SERVER-0004 is considered end-to-end complete, validate on Autodesk Revit 2026.5 using a modeled Autodesk sample such as Snowdon Towers Sample HVAC.

Required path:

```text
official MCP client
-> stdio RevitMCP.Server
-> revit_query_elements
-> obtain real document_id + element_refs
-> revit_describe_parameters
-> fresh discovery
-> Named Pipe handshake [5,4,3,2,1]
-> protocol 5
-> revit.describe_parameters
-> EXEC-0001 / ExternalEvent
-> Revit API
-> bounded structuredContent
```

Required evidence:

- `tools/list` exactly four tools;
- CAP-0001 official MCP regression PASS on the same v5 host;
- CAP-0002 official MCP regression PASS on the same v5 host;
- CAP-0003 official MCP regression PASS on the same v5 host;
- CAP-0004 MCP call PASS;
- use `revit_query_elements` to obtain real refs rather than inventing UniqueIds;
- opaque `parameter_ref` observed;
- repeated MCP call returns the same `parameter_ref` values for the same definition/source during the same document lifetime;
- built-in identity + `parameter_type_id` observed;
- measurable MEP data type observed, preferably `Flow` / HVAC airflow;
- `name_contains=Flow` PASS where applicable;
- mixed valid + fake element ref -> `ok` + `not_found`;
- old/wrong `document_id` -> `DOCUMENT_CONTEXT_CHANGED` with no retry/fallback;
- modern success has structuredContent and `content=[]`;
- representative UTF-8 payload sizes recorded for standard / filtered / partial-not-found describe results;
- no model writes/transactions;
- no production/client model used or committed.

Shared/local identity may be recorded if naturally available. Do not modify the model solely to manufacture those variants when automated tests already prove them.

## Acceptance criteria

SERVER-0004 is accepted as implemented when:

1. exactly four MCP tools are exposed;
2. `revit_describe_parameters` matches CAP-0004 semantics;
3. strict MCP input is enforced before discovery/Bridge;
4. Server schema accurately supports optional `instance_id` as `string | null`;
5. `instance_id` remains routing-only;
6. output, identity, and data-type schemas are closed and exact;
7. CAP-0004 routing is explicit `{5}`;
8. inherited tool compatibility sets remain explicit and correct;
9. unknown future versions are not accepted numerically;
10. every invocation performs fresh discovery and typed Bridge invocation;
11. document guard is preserved without retry/fallback;
12. mixed `not_found` remains successful structured output;
13. no parameter values or prohibited Revit identifiers leak;
14. no generic framework or lower-layer MCP coupling is added;
15. automated suites/builds remain green;
16. official MCP-client-to-Revit-2026.5 CAP-0004 live validation passes.

## Explicitly deferred

- typed parameter values;
- unit conversion;
- parameter-value filtering;
- writes;
- authorization/approval;
- preview/apply;
- hidden/API-only parameters;
- linked documents;
- persistent refs across reopen;
- HTTP/Azure/cloud;
- WebMCP;
- MCP Apps;
- Revit UI;
- global MCP structured-output compatibility redesign.

## References

- SERVER-0001: stdio `revit_get_context`
- SERVER-0002: stdio `revit_query_elements`
- SERVER-0003: stdio `revit_get_elements`
- CAP-0004: `revit_describe_parameters`
- ADR-0005: Agent context and token efficiency
- ADR-0006: Document and element reference identity
- BRIDGE-0005: `revit.describe_parameters` and protocol v5
