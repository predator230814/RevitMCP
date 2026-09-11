# SERVER-0001: stdio MCP server and `revit_get_context`

- Status: Accepted
- Date: 2026-09-09

## Purpose

Define the first concrete MCP Server slice for RevitMCP: expose CAP-0001 `revit_get_context` over the initial `stdio` MCP transport and route it deterministically to one validated local Revit instance through the existing Bridge.

This specification completes the MCP-facing half of CAP-0001. It does not add new Revit capabilities.

## Scope

SERVER-0001 covers:

- process bootstrap for `RevitMCP.Server`;
- the initial `stdio` MCP transport;
- one MCP tool: `revit_get_context`;
- local instance discovery and deterministic 0/1/many instance routing;
- BRIDGE-0001 handshake and protocol-v2 gating before CAP-0001 invocation;
- tool-result error mapping;
- strict structured output;
- context/token-efficiency behavior required by ADR-0005;
- end-to-end validation from an MCP client to real Revit.

## Non-goals

SERVER-0001 does not define or implement:

- `revit_list_instances`;
- additional Revit tools;
- writes or transactions;
- MCP resources or prompts;
- Streamable HTTP;
- MCP Apps;
- WebMCP;
- remote/cloud routing;
- authentication for remote transports;
- document identity/addressing;
- installer/deployment packaging;
- generic capability registries;
- generic tool-generation frameworks.

## MCP SDK and dependency boundary

The initial Server implementation should use the official C# MCP SDK package:

```text
ModelContextProtocol 2.2.0
```

The version is centrally pinned through `Directory.Packages.props`.

`ModelContextProtocol` is referenced only by `RevitMCP.Server` and server tests. MCP SDK types must not leak into `RevitMCP.Contracts`, `RevitMCP.Bridge`, or `RevitMCP.Addin`.

The Server continues to target `net10.0`.

The initial transport is `stdio`. `ModelContextProtocol.AspNetCore` is not required for this slice.

## Process model

Conceptually:

```text
MCP client
    |
    | stdio
    v
RevitMCP.Server
    |
    +-- revit_get_context
            |
            +-- local instance discovery
            +-- deterministic target resolution
            +-- Named Pipe connection
            +-- bridge.handshake [2,1]
            +-- require negotiated bridge protocol >= 2
            +-- revit.get_context
                    |
                    v
               Revit Addin
               EXEC-0001
               ExternalEvent
               Revit API
```

The MCP process owns MCP protocol handling and routing. It never accesses Autodesk Revit API assemblies.

## stdio requirements

`stdin` and `stdout` are reserved for MCP protocol traffic.

The Server must not write diagnostics, banners, startup text, stack traces, or other non-MCP output to `stdout`.

Diagnostics, if any, must use SDK-supported logging routed away from protocol stdout, normally stderr.

A normal EOF / client disconnect should allow the Server process to shut down cleanly.

## Initial MCP surface

Exactly one Revit capability tool is exposed by this slice:

```text
revit_get_context
```

Do not expose internal bridge methods such as `bridge.handshake` or `revit.get_context` as separate MCP tools.

Do not use assembly-wide automatic tool scanning if that risks exposing unintended methods. Prefer explicit registration of the accepted tool surface.

## Tool definition

### Name

`revit_get_context`

### Title

`Get Revit Context`

### Description

Return the current application, active document, active view, and selection summary for a Revit instance. Use this when the target Revit context is unknown or may have changed.

The description should remain concise because tool descriptions consume agent context.

### Annotations

```text
readOnlyHint: true
openWorldHint: false
```

No destructive/write annotation is introduced.

## Input schema

The MCP input is:

```json
{
  "instance_id": "optional opaque RevitMCP instance identifier"
}
```

`instance_id` is optional.

It is opaque. The MCP schema must not require UUID/GUID formatting even though the current implementation generates GUID values.

No timeout parameter is exposed to the agent in SERVER-0001. Capability timeout is a Server policy, not an AI-facing argument.

No transport, pipe name, process ID, session ID, document ID, or model path is accepted as tool input.

## Instance discovery

The Server uses the existing local discovery/registration architecture from ADR-0003 and `LocalInstanceDiscovery`.

Discovery validation remains responsible for:

- current Windows-session scope;
- registration parsing;
- process existence and process-start identity;
- Named Pipe reachability;
- BRIDGE-0001 handshake identity validation;
- bridge protocol negotiation;
- classification as `Ready`, `Unavailable`, `Incompatible`, or `Stale`.

The MCP Server must not trust registration files without bridge validation.

## Capability eligibility

For `revit_get_context`, an instance is capability-eligible only when:

1. discovery classified it as `Ready`;
2. the validated handshake negotiated bridge protocol version `2` or a future version explicitly documented as guaranteeing `revit.get_context`.

A protocol-v1 Revit instance may remain handshake-compatible but is not eligible for CAP-0001.

Do not silently treat every numerically higher future protocol version as guaranteeing CAP-0001 unless the bridge compatibility table/specification says so.

## Deterministic target resolution

Targeting follows CAP-0001 and ADR-0003.

### No explicit `instance_id`

- zero capability-eligible instances -> `NO_REVIT_INSTANCE`;
- exactly one capability-eligible instance -> select it automatically;
- more than one capability-eligible instance -> `INSTANCE_REQUIRED`.

### Explicit `instance_id`

- matching capability-eligible instance -> select it;
- no registration/candidate with that identity -> `INSTANCE_NOT_FOUND`;
- known candidate exists but is unavailable, incompatible, stale at evaluation time, or otherwise not capability-eligible -> `INSTANCE_UNAVAILABLE`.

Target selection must be deterministic and must not depend on file enumeration order.

## `INSTANCE_REQUIRED` details

The error may include a bounded candidate list to let an agent retry explicitly.

Each candidate contains only:

```text
instance_id
revit_version
revit_build
```

Do not include:

- process ID;
- pipe name;
- registration path;
- Windows username/session details;
- file/model paths;
- speculative document titles obtained by calling every Revit instance.

Do not issue `revit.get_context` against every candidate merely to make the ambiguity error richer.

The candidate list is bounded by the number of validated local Ready instances and contains no model enumeration.

## Bridge invocation

After selecting one instance, the Server establishes a typed bridge client connection to that instance.

The same connection must:

1. perform `bridge.handshake` with supported versions `[2,1]`;
2. validate the expected `instance_id`;
3. negotiate a version that guarantees `revit.get_context`;
4. invoke typed `GetContextAsync`.

The Server must not bypass the typed Bridge abstraction by emitting StreamJsonRpc calls itself.

Bridge connection timeout, handshake timeout, and capability timeout remain separate concepts.

## Timeout policy

SERVER-0001 owns a bounded capability wait duration for the MCP tool invocation.

The duration is implementation configuration, not part of the MCP tool schema.

Do not expose one universal timeout constant into Contracts or Addin.

A bridge capability timeout maps to:

```text
REVIT_EXECUTION_TIMEOUT
```

The Server should preserve caller/client cancellation separately from capability timeout.

No automatic retries are introduced.

## Success result

The canonical success value is the accepted transport-neutral `GetContextResult` from CAP-0001.

The MCP tool declares a strict `outputSchema` matching exactly:

```text
instance
  instance_id
  revit_version
  revit_build

document | null
  title
  kind
  is_workshared
  is_model_in_cloud
  is_read_only
  is_modified

active_view | null
  element_id
  name
  view_type

selection
  count
```

No additional Server/MCP routing metadata is added to the success payload.

The existing explicit `document: null` and `active_view: null` behavior is preserved.

## MCP structured output

For MCP protocol revisions supporting structured tool output, `structuredContent` is the canonical machine-readable result and must conform to the declared `outputSchema`.

The MCP specification defines `content` as required and recommends (`SHOULD`) duplicating structured JSON in a text block for backwards compatibility. RevitMCP intentionally prioritizes ADR-0005 context efficiency for conforming modern clients:

```text
structuredContent = GetContextResult
content = []
```

This is an explicit choice to avoid sending the same context twice to clients/LLMs.

If the SDK negotiates a protocol revision that does not support structured tool output, the Server must fall back to one compact JSON text content item containing the same result contract.

Do not emit both a verbose prose summary and the full structured result.

Do not add provider-specific token counters or model-specific response formats.

This compatibility behavior must be covered by tests at the MCP adapter boundary where practical.

## Tool errors

Capability/routing failures are normal tool-execution outcomes, not MCP protocol failures when MCP itself is functioning.

They return a tool result with:

```text
isError: true
```

and a compact machine-readable error shape containing at least:

```text
code
message
```

The initial accepted MCP-facing error codes are:

```text
NO_REVIT_INSTANCE
INSTANCE_REQUIRED
INSTANCE_NOT_FOUND
INSTANCE_UNAVAILABLE
REVIT_EXECUTION_TIMEOUT
REVIT_EXECUTION_FAILED
INVALID_REQUEST
```

`INVALID_REQUEST` is a Server MCP-boundary code. Unexpected top-level properties, including `instance_id` typos, are rejected before discovery or Bridge invocation. Do not leak binder or serializer exception detail.

For `INSTANCE_REQUIRED`, a bounded `candidates` field may be included as defined above.

Do not include stack traces, exception type names, local paths, pipe names, registration paths, or verbose inner-exception chains in normal tool results.

## Error mapping

Server routing/discovery states map deterministically:

```text
no eligible instance              -> NO_REVIT_INSTANCE
multiple eligible, no explicit ID -> INSTANCE_REQUIRED
explicit unknown ID               -> INSTANCE_NOT_FOUND
explicit non-ready/non-v2 ID      -> INSTANCE_UNAVAILABLE
bridge/capability timeout          -> REVIT_EXECUTION_TIMEOUT
capability execution failure       -> REVIT_EXECUTION_FAILED
```

Bridge identity mismatch, connection loss, handshake failure, or protocol incompatibility encountered after explicit targeting are represented to the agent as `INSTANCE_UNAVAILABLE` unless a more specific accepted CAP-level code already applies.

Do not leak BRIDGE implementation error codes as the primary agent-facing API unless a future capability specification accepts them.

## Agent context and token efficiency

ADR-0005 is normative.

SERVER-0001 must:

- expose only one tool in this slice;
- keep tool name/title/description concise;
- keep input schema to one optional field;
- return only CAP-0001 result fields;
- avoid duplicate JSON/prose on modern structured-output clients;
- keep ambiguity candidates minimal;
- avoid returning discovery diagnostics in successful results;
- avoid generic metadata/property bags;
- avoid tool-generated explanations that an LLM can derive from structured fields;
- avoid querying multiple Revit instances merely to enrich an ambiguity response.

The Server should perform deterministic routing/filtering itself rather than making an agent consume raw registrations and reason over them.

## Security and trust boundaries

The Server remains a local out-of-process process for this slice.

It must preserve:

- current-user/current-session discovery boundaries;
- registration-as-candidate semantics;
- handshake validation before trust;
- opaque `instance_id` targeting;
- no direct Named Pipe exposure to remote networks;
- no Revit API dependency in Server.

Tool annotations are descriptive hints only and are not authorization boundaries.

## Testability seams

The Server routing/application logic should be testable without launching Revit or an MCP client process.

Prefer small explicit abstractions around:

- instance discovery;
- target selection;
- bridge client creation/invocation;
- MCP result adaptation.

Do not introduce a generic command bus, service locator, reflection registry, or large DI architecture merely for tests.

The official MCP SDK hosting/DI model may be used at the process composition boundary.

## Automated validation requirements

Server tests should cover at minimum:

1. tool definition name/title/description and read-only/open-world annotations;
2. input schema has only optional opaque `instance_id`;
3. output schema matches CAP-0001 exactly;
4. zero eligible instances -> `NO_REVIT_INSTANCE`;
5. exactly one eligible instance -> deterministic auto-selection;
6. multiple eligible instances -> `INSTANCE_REQUIRED`;
7. `INSTANCE_REQUIRED` candidate fields are exactly `instance_id`, `revit_version`, `revit_build`;
8. explicit unknown ID -> `INSTANCE_NOT_FOUND`;
9. explicit unavailable/incompatible/v1 ID -> `INSTANCE_UNAVAILABLE`;
10. selection is independent of discovery enumeration order;
11. selected instance handshake requires CAP-0001-compatible bridge protocol;
12. successful bridge result becomes valid MCP structured output;
13. zero-document nulls survive MCP adaptation;
14. no success payload field is added beyond CAP-0001;
15. capability timeout -> `REVIT_EXECUTION_TIMEOUT` tool error;
16. capability failure -> `REVIT_EXECUTION_FAILED` tool error;
17. bridge availability/identity/protocol failures map to `INSTANCE_UNAVAILABLE`;
18. tool errors use `isError: true` and compact structured error data;
19. no stack trace/path/pipe/registration details leak in tool errors;
20. modern structured-output response does not duplicate full JSON in text content;
21. legacy/fallback response, if negotiated/supported by the SDK, emits one compact JSON text result;
22. `stdout` is not used for non-MCP application output;
23. Server project still has no Autodesk Revit API reference.

## Live end-to-end validation

Before CAP-0001 is marked complete, validate from a real MCP client against real Revit 2026.5 or another supported live Revit installation.

Required path:

```text
real MCP client
-> stdio RevitMCP.Server
-> tools/list contains revit_get_context
-> tools/call revit_get_context
-> instance discovery
-> Named Pipe
-> bridge.handshake selects v2
-> revit.get_context
-> EXEC-0001 / ExternalEvent
-> UIApplication
-> MCP tool result
```

### Zero-document validation

With one Revit instance open on Home and no document:

- no `instance_id` auto-selects the single eligible instance;
- tool call succeeds;
- structured result matches the live instance;
- `document` is null;
- `active_view` is null;
- `selection.count` is 0;
- no duplicate verbose text payload is returned;
- normal Revit shutdown makes a later call resolve deterministically as no instance/unavailable according to fresh discovery state.

### Multi-instance routing validation

If practical, run two disposable Revit instances:

- omitted `instance_id` -> `INSTANCE_REQUIRED`;
- candidate list is bounded/minimal;
- explicit returned `instance_id` retries successfully against exactly that instance.

If this cannot be safely run, automated routing tests are required and live multi-instance remains explicitly pending.

### Active-project validation

The existing BRIDGE-0002 active-project/selection validation remains pending until a disposable/sample model is available. It may be completed in the same validation session but should not require changing SERVER-0001 architecture.

## CAP-0001 completion rule

CAP-0001 may be marked implemented end-to-end when:

1. the MCP Server exposes the accepted tool contract;
2. 0/1/many instance routing is automated-tested;
3. bridge-v2 invocation is automated-tested;
4. MCP result/error adaptation is automated-tested;
5. the stdio MCP Server is validated with a real MCP client against live Revit;
6. zero-document behavior is validated end-to-end;
7. the remaining CAP-0001 acceptance criteria are either validated or explicitly recorded as pending live compatibility checks that do not invalidate the implemented contract.

Active-project/family/live-2025/live-2027 checks remain valuable compatibility validation, but they do not require expanding the MCP tool surface.

## Implementation task boundary

The implementation following this specification should remain one reviewable Server-focused slice.

Expected product-code areas:

```text
Directory.Packages.props
src/RevitMCP.Server/
tests/RevitMCP.Server.Tests/
docs/CURRENT_STATE.md
```

Bridge/Addin/Contracts changes should not be necessary unless implementation reveals a concrete accepted-contract gap. If such a gap appears, stop and report it rather than silently changing lower-layer contracts.

## Related decisions

- ADR-0001: out-of-process MCP Server and Revit capability host
- ADR-0002: Named Pipes + JSON-RPC local bridge
- ADR-0003: Revit instance registration, discovery, and addressing
- ADR-0004: solution structure and multi-version build
- ADR-0005: agent context and token efficiency
- BRIDGE-0001: handshake and version negotiation
- BRIDGE-0002: `revit.get_context` capability RPC
- EXEC-0001: Revit execution queue and ExternalEvent dispatch
- CAP-0001: `revit_get_context`

## External references

- MCP specification, Tools (`CallToolResult`, structured content, output schema, tool errors): https://modelcontextprotocol.io/specification/2025-06-18/server/tools
- Official C# MCP SDK / `ModelContextProtocol`: https://www.nuget.org/packages/ModelContextProtocol/2.2.0
