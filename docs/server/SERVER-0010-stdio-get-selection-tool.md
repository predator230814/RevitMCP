# SERVER-0010: stdio `revit_get_selection` tool

- Status: Accepted. Implemented. Official MCP-client live validation is pending.
- Date: 2026-10-14

## Purpose

Expose accepted CAP-0010 `revit_get_selection` through the existing standalone `BimIt.Revit.Server` stdio MCP process.

SERVER-0010 applies the existing read-tool pattern to accepted CAP-0010 / BRIDGE-0013. It does not create a new architecture decision, server process, transport, or generic capability framework.

This specification is implemented. Production `tools/list` contains exactly 11 tools, including `revit_get_selection`. Official MCP-client live validation in Revit 2026.5 is pending. The checklist is [CAP_0010_SELECTION_MCP_VALIDATION.md](../CAP_0010_SELECTION_MCP_VALIDATION.md). That checklist is not a pass.

The implemented `revit_get_warnings` tool has no `SERVER-00xx` specification file. This specification does not add one. SERVER-0010 is the get-selection tool.

SERVER-0010 must not change CAP-0010 or BRIDGE-0013 semantics.

## Scope

- expose exactly one additional MCP tool: `revit_get_selection`;
- preserve the ten current tools;
- use existing fresh current-Windows-session discovery and deterministic optional instance routing;
- negotiate Bridge protocol v13 for CAP-0010;
- preserve typed Named Pipe clients and per-call fresh capability invocation;
- pass the optional CAP-0010 `document_id` guard through unchanged;
- advertise a closed input schema;
- advertise the closed CAP-0010 result;
- enforce closed MCP input before discovery or Bridge work;
- preserve ADR-0005 structured-content behavior;
- preserve explicit capability-version sets rather than numeric comparisons.

## Non-goals

- changing CAP-0010 or BRIDGE-0013 while exposing this tool;
- changing CAP-0001 so that it returns selected elements;
- changing BRIDGE-0013 protocol sets or RPC method semantics;
- requiring callers to send `instance_id`;
- `Selection.GetReferences`, linked-document contents, or subelements;
- selection modification or Revit pick UI;
- parameters, geometry, or MEP topology inside this tool;
- Assistant `bimit_inspect_selection` or `bimit_explore_selected_mep`;
- writes, transactions, approvals, or authorization;
- a retroactive warnings Server specification;
- new MCP transports or remote HTTP hosting;
- an MCP SDK upgrade;
- generic tool or capability registries;
- changing global success/error policy;
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

Current production `tools/list` remains exactly:

```text
revit_get_context
revit_query_elements
revit_get_elements
revit_describe_parameters
revit_get_parameter_values
revit_get_mep_topology
revit_preview_parameter_updates
revit_request_parameter_update_review
revit_apply_parameter_updates
revit_get_warnings
```

After later SERVER-0010 implementation, `tools/list` is expected to contain those ten tools plus:

```text
revit_get_selection
```

That is 11 tools. This specification does not claim that the eleventh tool exists now.

No Bridge/internal JSON-RPC method is exposed directly as an MCP tool. `revit.get_selection` remains a Bridge RPC, not an MCP tool name.

## Tool metadata

Use the CAP-0010 accepted metadata.

### Name

`revit_get_selection`

### Title

`Get Revit Selection`

### Description

Return a bounded deterministic snapshot of elements currently selected in the active Revit document as opaque element references.

### Annotations

```text
readOnlyHint = true
openWorldHint = false
```

Annotations are descriptive hints, not security boundaries.

## MCP input schema

The MCP schema is a closed JSON Schema 2020-12 object matching CAP-0010.

Top-level fields:

```text
instance_id?: string | null
document_id?: string | null
limit?: integer
```

No field is required. Top-level `additionalProperties` is false.

### `instance_id`

Optional Server routing only. Same deterministic optional instance routing used by the ordinary read tools. Callers are not required to send it.

Advertise:

```json
{
  "type": ["string", "null"]
}
```

Semantics stay:

- omitted or JSON `null` means unspecified;
- a present string is routed exactly;
- empty or whitespace present strings never auto-select;
- `instance_id` never enters `GetSelectionRequest`.

No UUID format.

### `document_id`

Optional opaque string. No `minLength`. No UUID/GUID format.

Omitted or JSON `null` means the active document. Pass that omission through.

A present string, including empty or whitespace, is valid MCP syntax and must reach the CAP-0010 document guard unchanged. The Server must not trim, normalize, parse, or treat it as omitted.

### `limit`

Optional integer.

```text
default: 25
minimum: 1
maximum: 100
```

Advertise the closed range. Omitted `limit` stays omitted on `GetSelectionRequest` so CAP-0010 applies 25 in one place. The schema may document that default. It must not apply a different default.

A present value outside 1..100, or a present value that is not an integer, is `INVALID_REQUEST` at the MCP boundary.

A transport-neutral present value outside 1..100 remains `INVALID_SELECTION`.

Do not advertise a cursor, offset, unbounded flag, category filter, or selection-mutation field.

## Closed input enforcement

Advertised closed schemas are enforced at runtime before discovery or Bridge invocation.

`revit_get_selection` must use the existing `StrictInputMcpServerTool` / `ClosedSchemaArgumentValidator` rather than relying on ModelContextProtocol 2.2.0 binder behavior.

Reject as `INVALID_REQUEST`, before discovery or Bridge work, at minimum:

- unexpected top-level property;
- `instance_id` or `document_id` present with a non-string, non-null type;
- `limit` present and not an integer, or outside 1..100;
- aliases or different casing such as `instanceId`, `documentId`, or `maxElements`.

Do not leak binder, serializer, stack, path, or implementation detail in the error.

Do not convert malformed MCP input into `INVALID_SELECTION`.

`INVALID_SELECTION` remains the transport-neutral capability error when the Addin or Bridge returns it after a valid MCP-boundary mapping.

## Transport-neutral mapping

Accepted MCP input maps to:

```text
GetSelectionRequest
{
    DocumentId = document_id or omitted
    Limit = limit or omitted
}
```

`instance_id` is not included.

Do not sort or manufacture refs in the Server.

Do not add:

- PID;
- pipe name;
- paths;
- usernames;
- agent or model metadata;
- timeout input.

No Server-generated hidden document id or element-ref translation is allowed.

## Strict MCP output schema

Successful structured content is exactly the transport-neutral `GetSelectionResult` serialized with the existing MCP JSON options.

Top-level closed object:

```text
context
selected_count
truncated
element_refs
```

All required. Top-level `additionalProperties` is false.

### `context`

Closed object:

```text
instance_id: string
document_id: string
```

Both required. Both opaque. No UUID format.

### Counts and refs

```text
selected_count: integer >= 0
truncated: boolean
element_refs: array of opaque strings
```

The output schema caps `element_refs` at 100 items, the hard maximum of `limit`. The runtime array length is at most the effective limit for that call. `element_refs` items have no `minLength` and no format keyword.

The Server must not sort, uniquify, recount, truncate again, or rewrite CAP-0010 refs.

## Routing

When implemented, add an explicit resolver and eligibility predicate for get-selection, following the current per-tool routing pattern.

Use an explicit protocol predicate for get-selection. Eligibility is protocol `{13}` only.

```text
v1 through v12    false
v13               true
unknown v14       false
```

Never numeric `>=`.

Preserve existing routing behavior for ordinary reads:

- 0 eligible instances -> `NO_REVIT_INSTANCE`;
- 1 eligible instance -> auto-select;
- more than 1 eligible instance -> `INSTANCE_REQUIRED`.

Candidates remain deterministic by ordinal opaque `instance_id`.

Explicit id:

- unknown -> `INSTANCE_NOT_FOUND`;
- known but unavailable or ineligible -> `INSTANCE_UNAVAILABLE`;
- eligible v13 -> selected.

Empty or whitespace explicit `instance_id` never auto-selects.

On a v13 host, inherited eligibility is the BRIDGE-0013 sets, including 13 by explicit membership. A v12 host remains eligible for the current tools and ineligible for `revit_get_selection`.

CAP-0001 through CAP-0009 semantics stay unchanged. CAP-0001 continues to return selection count only.

## Application service

When implemented, add a get-selection application service following the current read-tool services.

Accepted capability errors:

```text
NO_ACTIVE_DOCUMENT
DOCUMENT_CONTEXT_CHANGED
INVALID_SELECTION
REVIT_EXECUTION_TIMEOUT
REVIT_EXECUTION_FAILED
```

Execution, after typed Bridge live validation has already passed:

```text
fresh discovery
-> resolve for get-selection
-> fresh Bridge client
-> connect
-> handshake with the supported version list
-> exact instance_id verification
-> explicit v13 get-selection support check
-> GetSelectionAsync(request, ServerTimeouts.CapabilityTimeout, cancellationToken)
-> dispose
```

No retries.

Caller cancellation semantics remain unchanged.

Other Bridge, IO, or bootstrap failures map through the existing `INSTANCE_UNAVAILABLE` behavior.

Do not convert an empty selection into a failure.

Server implementation is not complete, and must not start, before typed Bridge live validation of BRIDGE-0013 has passed. CAP-0010 is not implementation-complete before official MCP-client live validation of this tool has passed.

## Outcome

Success contains `GetSelectionResult`.

Failure contains the existing error code and message. Routing failures keep the current candidate payload where that payload already exists. Do not add a new error envelope.

## Registration

When implemented, register the application service and the tool explicitly.

Register exactly one new MCP tool. After that implementation, `tools/list` must expose exactly 11 tools.

Do not use assembly scanning.

This specification does not change `ServerHost` and does not register the tool.

## Success and error MCP behavior

Reuse `McpCallResultFactory`.

Modern success, matching the current read tools:

```text
isError = false
structuredContent = GetSelectionResult
content = []
```

Errors:

```text
isError = true
structuredContent absent
one compact JSON TextContent error
```

Conceptually:

```json
{
  "code": "NO_ACTIVE_DOCUMENT",
  "message": "..."
}
```

Do not place error objects in success-shaped `structuredContent`.

Do not change the global structured-output policy.

## Timeouts / cancellation

- no agent-facing timeout input;
- bootstrap and handshake waits stay on the existing `ServerTimeouts`;
- the capability wait uses the existing bounded capability timeout;
- no automatic retries;
- caller cancellation remains cancellation;
- a timeout after Revit execution has begun does not forcibly abort Revit API execution.

CAP-0010 does not change global timeout values.

## Security and scope boundaries

SERVER-0010 exposes no:

- numeric Revit `ElementId`;
- element names, categories, families, types, or levels;
- parameters or parameter refs;
- geometry, bounding boxes, or coordinates;
- MEP topology or connector information;
- linked-model metadata;
- file paths, cloud ids, or usernames;
- writeability or authorization decisions;
- selection edits or pick UI.

`readOnlyHint` is not a write-authorization substitute.

## Dependency boundaries

Required direction remains:

```text
Contracts <- Bridge <- Server
Contracts <- Bridge <- Addin -> Revit API
```

Server must not reference Autodesk Revit API assemblies.

Contracts and Bridge must not reference MCP SDK types.

Eleven explicit tools do not justify a new generic framework.

If a later implementation discovers that a lower-layer production change appears necessary, stop and report the conflict rather than expanding Server scope silently.

## Acceptance criteria

SERVER-0010 is acceptable as a specification when:

1. It specifies one additional MCP tool, `revit_get_selection`, and does not register it.
2. Production `tools/list` remains exactly the ten current tools. The expected count after implementation is 11.
3. Bridge eligibility is protocol `{13}` only, by explicit membership, never `version >= 13`.
4. Input is the closed CAP-0010 object. `instance_id` stays routing-only. `document_id` is an optional exact guard. `limit` is 1..100 with default 25.
5. Malformed MCP input and unknown properties return `INVALID_REQUEST` before discovery. Transport-neutral limit failures remain `INVALID_SELECTION`.
6. Routing matches the ordinary read tools and does not require `instance_id`.
7. Success is `isError = false`, `structuredContent = GetSelectionResult`, and `content = []`.
8. Errors are `isError = true`, with `structuredContent` absent and one compact JSON text error.
9. Annotations are `readOnlyHint: true` and `openWorldHint: false`.
10. The Server does not reshape CAP-0010 results and does not call Revit APIs.
11. Typed Bridge live validation remains a prerequisite for considering the Server implementation complete.
12. This specification does not implement the tool and does not add a retroactive warnings Server specification.

## Explicitly deferred

- tool registration and Server implementation;
- linked-document selection contents and link-document addressing;
- subelements, faces, edges, and geometry references;
- `GetReferences`-based selection;
- selection modification and Revit pick UI;
- parameters or details inside get-selection;
- automatic MEP topology expansion;
- Assistant `bimit_inspect_selection` and `bimit_explore_selected_mep`;
- writes and arbitrary Revit API execution;
- pagination and cursors.

## References

- CAP-0010: `revit_get_selection`
- BRIDGE-0013: `revit.get_selection`
- SERVER-0001 through SERVER-0009
- ADR-0003: Revit instance registration, discovery, and addressing
- ADR-0005: agent context and token efficiency
- ADR-0006: document and element reference identity
