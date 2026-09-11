# SERVER-0002: stdio `revit_query_elements` tool

- Status: Accepted
- Date: 2026-09-10

## Purpose

Expose accepted CAP-0002 `revit_query_elements` through the existing standalone `RevitMCP.Server` stdio MCP process while preserving CAP-0001 behavior and the established Server/Bridge/Addin boundaries.

SERVER-0002 extends SERVER-0001; it does not replace the stdio host or create another MCP server process.

## Scope

- expose exactly one additional MCP tool: `revit_query_elements`;
- preserve `revit_get_context`;
- use existing current-Windows-session discovery and deterministic instance routing;
- negotiate Bridge protocol v3 for query-elements;
- preserve explicit typed Named Pipe clients and fresh per-call discovery;
- map capability/document-context errors to compact MCP tool errors;
- advertise strict CAP-0002 input/output schemas;
- preserve ADR-0005 structured-content/token behavior.

## Non-goals

- CAP-0003 element inspection;
- new MCP transports;
- HTTP/remote hosting;
- MCP resources/prompts;
- generic tool registries;
- parameter/bounding-box query expansion;
- inactive-document addressing;
- writes, transactions, approvals, or authorization;
- persistence of document identity;
- Revit UI commands.

## MCP SDK and hosting

Continue using the accepted Server stack:

```text
ModelContextProtocol 2.2.0
.NET 10
stdio transport
```

No MCP SDK dependency may be introduced into Contracts, Bridge, or Addin.

The Server must continue reserving stdout for MCP protocol traffic. Diagnostics must not contaminate stdout.

Tool registration remains explicit. Do not switch to assembly-wide tool scanning merely because a second tool exists.

After SERVER-0002 implementation, `tools/list` should expose exactly:

```text
revit_get_context
revit_query_elements
```

No Bridge/internal RPC method is directly exposed as an MCP tool.

## Tool metadata

### Name

`revit_query_elements`

### Title

`Query Revit Elements`

### Description

Find elements in the active Revit document using bounded filters and return opaque element references. Use this before requesting details about matching elements.

### Annotations

```text
readOnlyHint = true
openWorldHint = false
```

## MCP input schema

The schema must match CAP-0002 exactly and reject additional properties at every closed object level.

Top-level fields:

```text
instance_id?: string
document_id?: string
scope: "document" | "active_view"
filters: object with >= 1 accepted filter
limit?: integer 1..100, default 50
```

`instance_id`, `document_id`, and returned `element_ref` values are opaque strings. Do not advertise GUID/UUID formats.

The accepted v1 filters are only:

```text
category_names
family_names
type_names
level_names
text_contains
```

Do not silently add parameter, spatial, view-id, class, workset, phase, design-option, linked-model, or generic property filters.

## Strict MCP output schema

Successful structured content is exactly:

```text
context
  instance_id: string
  document_id: string
matched_count: integer >= 0
truncated: boolean
element_refs: string[]
```

No per-element metadata is returned.

The output schema should enforce closed objects and the CAP-0002 reference-count maximum.

## Modern success result

For MCP revisions supporting structured output:

```text
isError = false
structuredContent = QueryElementsResult
content = []
```

The structured result is authoritative.

Do not duplicate the same result as a text content block.

Legacy text fallback may be used only through official MCP SDK negotiated compatibility behavior consistent with SERVER-0001. Do not create a custom MCP protocol state machine.

## MCP tool errors

Tool execution errors use:

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

Do not place error objects in success-shaped `structuredContent` when the advertised output schema describes CAP-0002 success.

## Instance discovery and routing

Every invocation performs fresh discovery for the current Windows session.

No candidate result or selected instance is cached across MCP calls.

### Capability eligibility

After SERVER-0002, the Server understands these explicit bridge capability sets:

```text
revit_get_context      -> protocol {2, 3}
revit_query_elements   -> protocol {3}
```

Do not implement:

```text
selectedProtocol >= 2
selectedProtocol >= 3
```

Future versions are not capability-compatible until explicitly documented.

### Unspecified `instance_id`

Filter discovered instances to those that are `Ready` and whose negotiated protocol explicitly guarantees CAP-0002.

Then:

```text
0 eligible -> NO_REVIT_INSTANCE
1 eligible -> auto-select it
>1 eligible -> INSTANCE_REQUIRED
```

`INSTANCE_REQUIRED` candidates remain minimal and deterministic:

```text
instance_id
revit_version
revit_build
```

Sort candidates by opaque `instance_id` using ordinal ordering.

Do not call `revit.query_elements` merely to enrich ambiguity candidates.

### Explicit `instance_id`

- unknown registration/identity -> `INSTANCE_NOT_FOUND`;
- known but unavailable/incompatible/non-v3 target -> `INSTANCE_UNAVAILABLE`;
- eligible exact target -> select it;
- never fall back to another instance after an explicit target fails.

Empty or whitespace strings remain explicit opaque ids exactly as established by SERVER-0001 routing; only omitted/null means unspecified.

## Fresh Bridge invocation

Discovery validates candidate readiness but disposes its validation client. After target resolution, SERVER-0002 must establish a fresh typed Bridge client for the actual capability call.

Required invocation sequence:

```text
fresh discovery
-> select exact instance
-> connect to selected registered pipe
-> bridge.handshake
     expected_instance_id = selected id
     supported_versions = [3,2,1]
-> require selected protocol == 3 for query-elements
-> QueryElementsAsync(...)
-> dispose client
```

Use RevitMCP Bridge abstractions. Do not call StreamJsonRpc directly from Server application logic.

A race where the selected Revit process becomes unavailable between discovery and invocation maps to `INSTANCE_UNAVAILABLE` when the failure is connection/handshake/identity/protocol availability related.

## CAP-0001 compatibility after protocol v3

SERVER-0002 must not regress `revit_get_context`.

Once the Server offers `[3,2,1]`, a fully current Addin may negotiate protocol 3. The existing get-context routing/gating must therefore recognize protocol 3 as explicitly guaranteeing `revit.get_context`.

Required behavior:

```text
get_context after negotiated v2 -> allowed
get_context after negotiated v3 -> allowed
get_context after negotiated v1 -> unavailable
```

This is an explicit capability set from BRIDGE-0003, not a numeric comparison.

Regression tests are mandatory because an unchanged `SelectedProtocolVersion == 2` gate would make current v3 hosts invisible to CAP-0001.

## Document context guard

The Server does not invent or resolve document identity itself.

`document_id` is passed through the transport-neutral CAP-0002 request to the selected Addin.

The Addin validates it against the active document inside EXEC-0001.

Server behavior:

- omitted `document_id`: pass omission through and return the Addin-assigned current `document_id`;
- matching id: return normal result;
- `DOCUMENT_CONTEXT_CHANGED`: preserve the capability error code/message;
- do not retry against another document or instance.

## Timeouts and cancellation

Continue SERVER-0001 timeout ownership:

- no agent-facing timeout input;
- bootstrap/connection-handshake wait is bounded by Server policy;
- capability wait uses Server/Bridge capability timeout policy;
- no automatic retries;
- caller cancellation remains cancellation rather than being rewritten as an execution timeout;
- once Revit execution has started, the caller may stop waiting but the active Revit API operation is not forcibly aborted.

CAP-0002 may be more expensive than CAP-0001, but this specification does not silently increase global timeout values. Implementation/live evidence should inform any later policy change.

## Agent-facing error mapping

Routing:

```text
NO_REVIT_INSTANCE
INSTANCE_REQUIRED
INSTANCE_NOT_FOUND
INSTANCE_UNAVAILABLE
```

Document/query capability:

```text
NO_ACTIVE_DOCUMENT
DOCUMENT_CONTEXT_CHANGED
NO_ACTIVE_VIEW
INVALID_QUERY
REVIT_EXECUTION_TIMEOUT
REVIT_EXECUTION_FAILED
```

MCP input-boundary:

```text
INVALID_REQUEST
```

Advertised closed input schemas are enforced at the MCP tool boundary for both `revit_query_elements` and `revit_get_context`. Unexpected top-level names, `instance_id` / `document_id` typos, and unexpected nested `filters` properties are rejected before discovery or Bridge invocation. This is not `INVALID_QUERY`; that code remains CAP-0002 business validation. Do not leak binder or serializer exception detail.

Bridge bootstrap errors remain internal and map to the established agent-facing instance availability model where appropriate.

Unknown filter values returning zero matches are success, not errors.

## Dependency boundaries

Required dependency direction remains:

```text
Contracts <- Bridge <- Server
Contracts <- Bridge <- Addin -> Revit API
```

Server must not reference Autodesk Revit API assemblies.

Contracts and Bridge must not reference MCP SDK types.

No new architectural framework is justified by adding this second tool.

## Automated validation requirements

At minimum cover:

- `tools/list` exposes exactly the two accepted tools;
- CAP-0001 get-context remains usable after v3 support;
- query-elements requires explicit protocol 3;
- no numeric `>=` capability gating;
- 0/1/many query-capable routing;
- deterministic candidate ordering;
- explicit v1/v2 query target -> `INSTANCE_UNAVAILABLE`;
- explicit unknown target -> `INSTANCE_NOT_FOUND`;
- explicit empty/whitespace id remains explicit;
- fresh discovery on every call;
- fresh typed client after selection;
- handshake uses `[3,2,1]` and expected instance identity;
- no fallback after explicit target failure;
- strict input schema and filter set;
- unexpected top-level and nested input properties are rejected before discovery/Bridge;
- `documentId` / `instanceId` typos are rejected rather than treated as omitted identifiers;
- strict output schema;
- modern success has structuredContent and empty content;
- errors have `isError=true` and no success structuredContent;
- omitted/matching/mismatched document ids;
- document and active-view scopes;
- filter AND/OR semantics;
- exact matched count, deterministic ordering, limit and truncation;
- zero-match success;
- reference count never exceeds 100;
- no per-element detail fields;
- timeout and cancellation behavior;
- dependency-boundary assertions.

## Live validation

Before CAP-0002 is considered implemented, validate on Autodesk Revit 2026.5 with a disposable/sample project:

```text
official MCP client
-> stdio RevitMCP.Server
-> revit_query_elements
-> fresh discovery
-> Named Pipe handshake [3,2,1]
-> protocol 3
-> revit.query_elements
-> EXEC-0001 / ExternalEvent
-> Revit API
-> bounded structuredContent
```

Required live evidence should include:

- tools/list exactly two tools;
- one category-only query with known matches;
- one multi-filter query;
- one zero-match query;
- one truncated result where practical;
- exact returned/matched counts;
- no per-element metadata beyond opaque refs;
- repeated call with returned `document_id` succeeds;
- switching/opening another active document causes old `document_id` to return `DOCUMENT_CONTEXT_CHANGED` when practical;
- CAP-0001 still succeeds against the same v3 host;
- modern `content` remains empty;
- serialized structured-content sizes are recorded diagnostically.

Do not use a production/client model for validation.

## Acceptance criteria

SERVER-0002 is accepted as implemented when:

1. exactly two MCP tools are exposed;
2. `revit_query_elements` conforms exactly to CAP-0002 schemas;
3. v3 is required for query-elements;
4. v2 and v3 are both explicitly accepted for get-context;
5. discovery/routing remains fresh and deterministic;
6. invocation uses a fresh typed Bridge client and second handshake;
7. document context guard is preserved end to end;
8. output remains bounded and token-efficient;
9. no new framework or lower-layer coupling is introduced;
10. automated tests and existing suites pass;
11. Server Release and Revit 2025/2026/2027 Addin builds pass;
12. real MCP-client-to-Revit 2026.5 query validation passes.

## References

- SERVER-0001: stdio `revit_get_context`
- CAP-0002: `revit_query_elements`
- ADR-0005: Agent context and token efficiency
- ADR-0006: Document and element reference identity
- BRIDGE-0003: `revit.query_elements` and protocol v3
