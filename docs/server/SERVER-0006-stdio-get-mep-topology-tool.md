# SERVER-0006: stdio `revit_get_mep_topology` tool

- Status: Accepted
- Date: 2026-09-22

## Purpose

Expose accepted CAP-0006 `revit_get_mep_topology` through the existing standalone `RevitMCP.Server` stdio MCP process while preserving SERVER-0001 through SERVER-0005 behavior.

SERVER-0006 applies the existing SERVER-0001..0005 pattern to accepted CAP-0006 / BRIDGE-0007. It does not create a new architecture decision, server process, transport, or generic capability framework.

This specification does not implement Server code, schemas, tests, routing, or tool registration. After a later implementation PR, `tools/list` will expose six tools. Current runtime MCP tools remain exactly five.

SERVER-0006 must not change CAP-0006 or BRIDGE-0007 semantics.

## Scope

- expose exactly one additional MCP tool: `revit_get_mep_topology`;
- preserve `revit_get_context`, `revit_query_elements`, `revit_get_elements`, `revit_describe_parameters`, and `revit_get_parameter_values`;
- use existing fresh current-Windows-session discovery and deterministic instance routing;
- negotiate Bridge protocol v7 for CAP-0006;
- preserve typed Named Pipe clients and per-call fresh capability invocation;
- require CAP-0006 `document_id` context guarding end to end;
- advertise a closed input schema of explicit seed refs and hard bounds;
- advertise a closed deterministic graph output;
- enforce closed MCP input before discovery/Bridge work, including seed uniqueness;
- preserve ADR-0005 structured-content/token behavior;
- preserve explicit capability-version sets rather than numeric comparisons.

## Non-goals

- changing CAP-0006 physical-connection, BFS, truncation, or seed-status rules;
- changing BRIDGE-0007 protocol sets or RPC method semantics;
- logical topology;
- public connector identity;
- flow direction;
- linked-document addressing;
- writes, transactions, approvals, or authorization;
- preview/apply workflows;
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

After later SERVER-0006 implementation, `tools/list` will expose exactly:

```text
revit_get_context
revit_query_elements
revit_get_elements
revit_describe_parameters
revit_get_parameter_values
revit_get_mep_topology
```

This specification does not claim that six tools exist now.

No Bridge/internal JSON-RPC method is exposed directly as an MCP tool. In particular, `revit.get_mep_topology` remains a Bridge RPC, not an MCP tool name.

## Tool metadata

Use the CAP-0006 accepted metadata exactly.

### Name

`revit_get_mep_topology`

### Title

`Get Revit MEP Topology`

### Description

Traverse bounded physical MEP connectivity from known Revit elements and return a deterministic element-level graph.

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
seed_element_refs: array
domain?: hvac | piping | electrical | cable_tray_conduit
max_depth?: integer
max_elements?: integer
max_edges?: integer
```

Required:

```text
document_id
seed_element_refs
```

Top-level `additionalProperties` is false.

### Opaque ids

`instance_id`, `document_id`, and seed `element_ref` strings remain opaque.

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

- omitted or JSON `null` means unspecified;
- a present string is routed exactly;
- empty/whitespace present strings never auto-select;
- `instance_id` never enters `GetMepTopologyRequest`.

No UUID format.

### `document_id`

Required string. No `minLength`.

Empty/whitespace present strings are valid MCP syntax and must reach CAP-0006 document-guard semantics (`DOCUMENT_CONTEXT_CHANGED`).

### `seed_element_refs`

```text
type = array
minItems = 1
maxItems = 10
uniqueItems = true
items.type = string
```

No `minLength` on items.

Empty/whitespace seed strings remain accepted MCP syntax and normally become seed status `not_found`.

Null/missing/non-string items are `INVALID_REQUEST`.

Uniqueness is ordinal equality on raw strings. Because items are strings, advertised `uniqueItems` is sufficient; do not build a second general validation framework.

### `domain`

Optional closed enum. Omit to mean all supported physical domains.

Unexpected values are `INVALID_REQUEST` at the MCP boundary.

### Bounds

Advertise the accepted integer ranges. Omitted fields are not required and later map to CAP-0006 defaults.

```text
max_depth     default 3    minimum 1    maximum 10
max_elements  default 100  minimum 10   maximum 250
max_edges     default 200  minimum 10   maximum 500
```

Present values outside those ranges are `INVALID_REQUEST` at the MCP schema boundary. Transport-neutral out-of-range values after a valid mapping remain `INVALID_MEP_TOPOLOGY`.

Do not advertise a timeout, connector filter, system filter, or direction field.

## Closed input enforcement

The existing Server rule from SERVER-0002 through SERVER-0005 remains mandatory: advertised closed schemas are enforced at runtime before discovery or Bridge invocation.

`revit_get_mep_topology` must use the existing `StrictInputMcpServerTool` / `ClosedSchemaArgumentValidator` rather than relying on ModelContextProtocol 2.2.0 binder behavior.

Reject as `INVALID_REQUEST`, before discovery/Bridge work, at minimum:

- missing/null `document_id`;
- missing/null `seed_element_refs`;
- `seed_element_refs` count outside 1..10;
- null/non-string seed item;
- duplicate raw seed strings;
- unexpected top-level property;
- unexpected `domain` value;
- present bounds outside advertised ranges;
- aliases/casing such as `documentId`, `seedElementRefs`, `instanceId`, `maxDepth`.

Do not leak binder, STJ, stack, path, or implementation detail in the error.

Do not convert malformed MCP input into `INVALID_MEP_TOPOLOGY`.

`INVALID_MEP_TOPOLOGY` remains the transport-neutral capability error if it is returned by the Addin/Bridge after valid MCP-boundary mapping.

## Transport-neutral mapping

Accepted MCP input maps to future Contracts:

```text
GetMepTopologyRequest
{
    DocumentId = document_id,
    SeedElementRefs = seed_element_refs exactly in input order,
    Domain = domain or omitted,
    MaxDepth = max_depth or omitted,
    MaxElements = max_elements or omitted,
    MaxEdges = max_edges or omitted
}
```

`instance_id` is not included.

Preserve seed order. Do not sort seeds in the Server.

Omitted optional bounds/domain remain omitted so CAP-0006 defaults apply in one place.

Do not add:

- PID;
- pipe name;
- paths;
- usernames;
- agent/model metadata;
- timeout input;
- connector or system identity.

No Server-generated hidden document id or element-ref translation is allowed.

## Strict MCP output schema

Successful structured content is exactly the transport-neutral `GetMepTopologyResult` serialized with the existing MCP JSON options.

Top-level closed object:

```text
context
seeds
nodes
edges
truncated
truncation_reasons
```

All required. Top-level `additionalProperties` is false.

The Server must not sort, aggregate, retry, or rewrite CAP-0006 seeds, nodes, edges, or truncation reasons.

### `context`

Closed object:

```text
instance_id: string
document_id: string
```

Both required.

### `seeds`

Array length equals the request seed count.

Each item is closed and requires `element_ref` and `status`.

`status` enum:

```text
ok
not_found
no_connectors
```

### `nodes`

Closed items requiring `element_ref` and `depth`.

`depth` is an integer `>= 0`.

### `edges`

Closed items requiring `element_ref_a`, `element_ref_b`, and `domains`.

`domains` is a unique array of the four public domain strings.

### Truncation

```text
truncated: boolean
truncation_reasons: unique array of depth | elements | edges
```

## Routing

Add explicitly:

```text
ResolveForGetMepTopology(...)
IsGetMepTopologyEligible(...)
```

Use:

```text
BridgeProtocol.SupportsGetMepTopology
```

Exact eligibility after implementation:

```text
v1 false
v2 false
v3 false
v4 false
v5 false
v6 false
v7 true
v8 false
```

Never numeric `>=`.

Preserve existing routing behavior:

- 0 eligible -> `NO_REVIT_INSTANCE`;
- 1 eligible -> auto-select;
- >1 eligible -> `INSTANCE_REQUIRED`.

Candidates remain deterministic by ordinal opaque `instance_id`.

Explicit id:

- unknown -> `INSTANCE_NOT_FOUND`;
- known but unavailable/ineligible -> `INSTANCE_UNAVAILABLE`;
- eligible v7 -> selected.

Empty/whitespace explicit id never auto-selects.

Inherited eligibility on a v7 host remains:

```text
get-context           {2,3,4,5,6,7}
query-elements        {3,4,5,6,7}
get-elements          {4,5,6,7}
describe-parameters   {5,6,7}
get-parameter-values  {6,7}
```

CAP-0001 through CAP-0005 semantics are unchanged.

## Application service

Create `GetMepTopologyApplicationService` following the SERVER-0005 pattern.

Accepted capability errors:

```text
NO_ACTIVE_DOCUMENT
DOCUMENT_CONTEXT_CHANGED
INVALID_MEP_TOPOLOGY
REVIT_EXECUTION_TIMEOUT
REVIT_EXECUTION_FAILED
```

Execution:

```text
fresh discovery
-> ResolveForGetMepTopology
-> fresh Bridge client
-> connect
-> handshake with BridgeProtocol.SupportedVersions
-> exact instance_id verification
-> BridgeProtocol.SupportsGetMepTopology(selected version)
-> GetMepTopologyAsync(request, ServerTimeouts.CapabilityTimeout, cancellationToken)
-> dispose
```

After implementation, handshake advertises:

```text
[7, 6, 5, 4, 3, 2, 1]
```

No retries.

Caller cancellation semantics remain unchanged.

Other Bridge/IO/bootstrap failures map through existing `INSTANCE_UNAVAILABLE` behavior.

Do not convert per-seed CAP-0006 statuses into failures.

## Outcome

Add a SERVER-0005-style:

```text
GetMepTopologyOutcome
```

Success contains `GetMepTopologyResult`.

Failure contains error code, message, and optional candidates.

## Registration / ServerHost

Register the new application/tool services explicitly.

Register exactly one new MCP tool:

```text
.WithGetMepTopologyTool()
```

After implementation `tools/list` must expose exactly 6 tools.

Do not use assembly scanning.

## Success/error MCP behavior

Reuse `McpCallResultFactory`.

Modern success:

```text
IsError = false
StructuredContent = GetMepTopologyResult
Content = []
```

Errors:

```text
IsError = true
StructuredContent absent
one compact JSON TextContent payload
```

Do not change global structured-output policy.

## Timeouts / cancellation

- no agent-facing timeout input;
- bootstrap/handshake wait bounded by existing `ServerTimeouts`;
- capability wait uses existing bounded capability timeout policy;
- no automatic retries;
- caller cancellation remains cancellation;
- timeout after Revit execution has begun does not forcibly abort Revit API execution.

CAP-0006 does not silently increase global timeout values.

## Security and scope boundaries

SERVER-0006 must expose no:

- Autodesk `Connector` objects;
- public `connector_ref`;
- connector indexes or `Connector.Id`;
- numeric Revit ElementIds;
- coordinates or geometry;
- file paths;
- usernames;
- MEP system membership as identity;
- authorization decisions;
- writes;
- transactions.

`readOnlyHint` is not a write-authorization substitute.

## Dependency boundaries

Required direction remains:

```text
Contracts <- Bridge <- Server
Contracts <- Bridge <- Addin -> Revit API
```

Server must not reference Autodesk Revit API assemblies.

Contracts/Bridge must not reference MCP SDK types.

Six explicit tools do not justify a new generic framework.

If a later implementation discovers that a lower-layer production change appears necessary, stop and report the conflict rather than expanding Server scope silently.

## Expected implementation shape

This specification PR adds no source code.

Follow the current explicit Server pattern used by SERVER-0005. Expected later components:

```text
Cap0006JsonSchemas
GetMepTopologyToolMetadata
GetMepTopologyMcpTools
GetMepTopologyApplicationService
GetMepTopologyOutcome
GetMepTopologyToolRegistration
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

1. `tools/list` exposes exactly six accepted tools;
2. exact CAP-0006 tool name/title/description/annotations (`readOnlyHint = true`, `openWorldHint = false`);
3. closed top-level CAP-0006 input;
4. `seed_element_refs` 1..10 unique strings;
5. optional `instance_id` accepts `string | null`;
6. `document_id` required but opaque, including empty/whitespace;
7. empty/whitespace seed refs accepted syntactically;
8. null/missing/non-string seeds -> `INVALID_REQUEST`;
9. unexpected top-level props and aliases/casing -> `INVALID_REQUEST`;
10. invalid `domain` and out-of-range bounds -> `INVALID_REQUEST`;
11. omitted bounds/domain do not invent Server-side defaults that hide CAP-0006 defaults;
12. seed order preserved into the Bridge request;
13. `instance_id` excluded from the transport request;
14. exact closed output schema;
15. seed / node / edge / truncation schemas;
16. no numeric ElementId or connector identity output field;
17. protocol routing v7 only;
18. v1..v6 and unknown v8 ineligible;
19. deterministic zero/one/many instance routing;
20. explicit empty/whitespace `instance_id` never auto-selects;
21. handshake sends `[7,6,5,4,3,2,1]`;
22. selected v7 invokes `GetMepTopologyAsync`;
23. selected v6 or v8 does not invoke it;
24. expected Bridge capability errors survive as MCP tool errors;
25. seed-status results remain successful;
26. capability timeout mapping;
27. client disposed after every invocation;
28. fresh discovery on every invocation;
29. malformed MCP input performs no discovery/Bridge work;
30. inherited CAP-0001..CAP-0005 routing still accepts v7;
31. process-level stdio server exposes exactly six public Revit tools;
32. Contracts/Bridge/Addin dependency boundaries remain unchanged.

Do not require live Revit for Server automated tests.

Also cover:

- aliases/casing such as `documentId` / `seedElementRefs` rejected as `INVALID_REQUEST`;
- errors have no success-shaped structuredContent;
- full CAP-0001..CAP-0005 Server regression suites remain green.

## Process-level validation

Using official `ModelContextProtocol` 2.2.0 against built Release `RevitMCP.Server.exe` after implementation:

```text
tools/list count = 6
```

Exactly:

```text
revit_get_context
revit_query_elements
revit_get_elements
revit_describe_parameters
revit_get_parameter_values
revit_get_mep_topology
```

No internal Bridge RPC methods.

Process-level strict-boundary smoke must also prove a malformed CAP-0006 input returns `INVALID_REQUEST` before Revit discovery or Bridge work.

## Official MCP live validation gate

After SERVER-0006 implementation and code review, use:

- built Release `RevitMCP.Server`;
- official ModelContextProtocol 2.2.0 client;
- stdio;
- real Revit 2026.5 / `26.5.0.55`;
- real production Addin / Bridge v7;
- disposable Autodesk Snowdon HVAC copy or another Autodesk disposable MEP sample;
- no source-model write/save.

Required end-to-end path:

```text
official ModelContextProtocol client
-> stdio RevitMCP.Server
-> Bridge v7
-> Addin
-> EXEC-0001
-> Revit 2026.5
```

Required evidence:

```text
tools/list count = 6
revit_get_context
revit_query_elements
revit_get_elements
revit_describe_parameters
revit_get_parameter_values
revit_get_mep_topology
```

Then validate a real bounded HVAC topology:

```text
revit_get_context PASS
-> query a real HVAC seed PASS
-> revit_get_mep_topology PASS
-> physical adjacency, not system membership
-> multi-hop depth
-> deterministic repeat of the same request
-> truncation behavior when bounds omit known topology
-> missing seed -> not_found item, whole call remains success
-> no_connectors seed if naturally available
-> wrong document_id -> DOCUMENT_CONTEXT_CHANGED
-> malformed MCP input -> INVALID_REQUEST
-> modern structured success/error behavior
-> no model modification / no transaction / no save
```

Do not fabricate a naturally unavailable case solely for coverage. Record it and rely on automated coverage where appropriate.

## Acceptance criteria

SERVER-0006 is accepted as a specification when:

1. It adds exactly one new MCP tool, `revit_get_mep_topology`, without claiming that six tools exist in the current runtime.
2. Tool name, title, description, and annotations match accepted CAP-0006 metadata exactly.
3. MCP input is a closed JSON Schema 2020-12 object with required `document_id` and `seed_element_refs`, optional `instance_id` as `string | null`, optional `domain` and bounds, and `additionalProperties = false`.
4. `seed_element_refs` is 1..10 unique opaque strings. Empty/whitespace present strings are not omitted.
5. Opaque-id behavior is preserved: no `minLength` on `document_id` or seed refs; empty/whitespace `instance_id` never auto-selects.
6. Accepted MCP input maps to `GetMepTopologyRequest` with request seed order preserved and without `instance_id`.
7. Output is a closed `GetMepTopologyResult`. No numeric ElementId or connector identity field.
8. Modern success uses `isError = false`, authoritative `structuredContent`, and `content = []`. Errors use `isError = true`, no success-shaped structured content, and one compact JSON TextContent block.
9. Routing is explicit `{7}` via `BridgeProtocol.SupportsGetMepTopology`. v1..v6 and unknown v8 are ineligible. Never numeric `>=`.
10. Every invocation performs fresh discovery, a fresh typed Bridge client, handshake with `BridgeProtocol.SupportedVersions`, identity validation, protocol support check, capability call, and disposal. No retry/fallback.
11. Inherited tool compatibility remains `{2,3,4,5,6,7}`, `{3,4,5,6,7}`, `{4,5,6,7}`, `{5,6,7}`, `{6,7}`. CAP-0001..CAP-0005 semantics are unchanged.
12. Implementation remains explicit SERVER-0005-shaped components. No generic framework, capability registry, or MCP SDK upgrade.
13. Later automated tests cover the listed schema, uniqueness, routing, handshake, timeout, disposal, and inherited-regression cases without live Revit.
14. Official MCP-client-to-Revit-2026.5 live validation is required after implementation.
15. No writes, write authorization, transactions, logical topology, or public connector identity.

SERVER-0006 is accepted as implemented only after a later implementation PR satisfies those criteria, automated coverage, and the official MCP live gate.

## Explicitly deferred

SERVER-0006 must not introduce:

- writes;
- write authorization;
- write preview/approval;
- transaction design;
- logical topology;
- public connector identity;
- flow direction;
- linked-document topology;
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

- SERVER-0001 through SERVER-0005
- CAP-0006: `revit_get_mep_topology`
- ADR-0005: Agent context and token efficiency
- ADR-0006: Document and element reference identity
- ADR-0007: Federated MCP boundaries and optional orchestration
- BRIDGE-0007: `revit.get_mep_topology` and protocol v7
