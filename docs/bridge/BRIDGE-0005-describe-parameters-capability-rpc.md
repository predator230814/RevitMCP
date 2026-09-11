# BRIDGE-0005: `revit.describe_parameters` capability RPC

- Status: Accepted
- Date: 2026-09-11
- Introduced bridge protocol version: 5

## Purpose

Define the local Bridge RPC that implements CAP-0004 `revit_describe_parameters` and introduce bridge protocol version 5.

This specification preserves the explicit typed RPC approach established by BRIDGE-0002 through BRIDGE-0004. It does not introduce a generic capability registry, command envelope, or reflection-driven dispatch system.

## Related accepted decisions

- ADR-0001: out-of-process MCP server and in-process Revit capability host
- ADR-0002: Named Pipes + JSON-RPC local bridge
- ADR-0003: Revit instance registration/discovery/addressing
- ADR-0005: agent context and token efficiency
- ADR-0006: document and element reference identity
- BRIDGE-0001: handshake and protocol negotiation
- BRIDGE-0002: `revit.get_context`
- BRIDGE-0003: `revit.query_elements`
- BRIDGE-0004: `revit.get_elements`
- EXEC-0001: serialized Revit execution dispatcher
- CAP-0004: `revit_describe_parameters`

## Bridge protocol version 5

Protocol version 5 adds `revit.describe_parameters` and explicitly preserves the version-2, version-3, and version-4 capability guarantees.

The capability matrix is:

```text
protocol 1
  bridge.handshake

protocol 2
  bridge.handshake
  revit.get_context

protocol 3
  bridge.handshake
  revit.get_context
  revit.query_elements

protocol 4
  bridge.handshake
  revit.get_context
  revit.query_elements
  revit.get_elements

protocol 5
  bridge.handshake
  revit.get_context
  revit.query_elements
  revit.get_elements
  revit.describe_parameters
```

A fully capable current host should advertise:

```text
[5, 4, 3, 2, 1]
```

A host implementing CAP-0001 + CAP-0002 + CAP-0003 but not CAP-0004 continues to advertise:

```text
[4, 3, 2, 1]
```

A host implementing CAP-0001 + CAP-0002 but not CAP-0003 continues to advertise:

```text
[3, 2, 1]
```

A CAP-0001-only host continues to advertise:

```text
[2, 1]
```

A handshake-only host continues to advertise:

```text
[1]
```

## Explicit capability-version rule

Protocol integers are not an automatic numeric compatibility ladder.

For the accepted versions defined today:

```text
revit.get_context           guaranteed by {2, 3, 4, 5}
revit.query_elements        guaranteed by {3, 4, 5}
revit.get_elements          guaranteed by {4, 5}
revit.describe_parameters   guaranteed by {5}
```

Do not implement capability checks as:

```text
version >= 2
version >= 3
version >= 4
version >= 5
```

A future version 6 must explicitly document which guarantees it preserves before Server or Bridge code treats it as compatible.

## RPC method

The JSON-RPC method is:

```text
revit.describe_parameters
```

A peer must not call this method unless bridge protocol version 5 was explicitly negotiated on that connection.

The host-side per-connection adapter must enforce this gate independently of the typed client so a raw JSON-RPC peer cannot bypass negotiation.

Required host behavior:

```text
no handshake -> reject capability call
v1 -> reject get-context/query/get-elements/describe-parameters
v2 -> allow get-context only
v3 -> allow get-context + query-elements
v4 -> allow get-context + query-elements + get-elements
v5 -> allow get-context + query-elements + get-elements + describe-parameters
unknown future version -> no capability becomes implicitly allowed
```

The underlying Addin capability service must not be invoked when the negotiated protocol does not explicitly support the RPC.

## Transport-neutral request

Conceptually:

```text
DescribeParametersRequest
{
    document_id: string
    element_refs: string[1..10]
    source: instance | type | both
    name_contains?: string
    limit: int
}
```

The exact validation, identity, aggregation, ordering, and result semantics are owned by CAP-0004.

The request must not contain:

- `instance_id`;
- MCP metadata;
- model-provider metadata;
- pipe/process/session identifiers;
- file paths;
- an agent-supplied execution timeout.

`instance_id` is resolved by the Server before opening the selected Named Pipe. `document_id` remains in the capability request because it is a correctness guard that must be verified against the active Revit `Document` inside EXEC-0001.

Request/result contract types belong in `RevitMCP.Contracts` and must not reference StreamJsonRpc, Named Pipes, MCP SDK types, or Autodesk Revit API types.

## Result contract

Conceptually:

```text
DescribeParametersResult
{
    context
      instance_id
      document_id
    elements[]
    matched_count
    truncated
    parameters[]
}
```

Each result element is either `status = ok` or `status = not_found` with only `element_ref` and status.

The Bridge must not expand, reinterpret, parse, or enrich `element_ref` or `parameter_ref` values.

## Identity ownership

ADR-0006 remains authoritative for `document_id` and `element_ref`.

The Addin also owns `parameter_ref` generation and resolution for the current open-document lifetime.

Bridge and Contracts treat all three ids as opaque strings.

Required behavior:

- `document_id` is required;
- no active document -> `NO_ACTIVE_DOCUMENT`;
- matching active document -> describe refs;
- non-matching document -> `DOCUMENT_CONTEXT_CHANGED`;
- no fallback to another document;
- missing/deleted/unresolvable element refs become CAP-0004 item-level `not_found` statuses, not Bridge failures.

## Bridge service composition

Explicit service composition remains the rule.

Conceptually:

```text
NamedPipeBridgeHost
    |
    +-- BridgeHandshakeService
    |     cached process metadata only
    |
    +-- IRevitCapabilityService
    |     GetContextAsync(...)
    |
    +-- IRevitQueryElementsService
    |     QueryElementsAsync(...)
    |
    +-- IRevitGetElementsService
    |     GetElementsAsync(...)
    |
    +-- IRevitDescribeParametersService
          DescribeParametersAsync(...)
          implemented by RevitMCP.Addin
          uses EXEC-0001
```

Do not introduce:

- reflection-driven capability registration;
- generic `InvokeCapability` RPCs;
- a command bus;
- a capability dictionary/registry merely to avoid four explicit dependencies;
- MCP SDK dependencies below `RevitMCP.Server`;
- Autodesk Revit API dependencies in `RevitMCP.Bridge` or `RevitMCP.Contracts`.

## Protocol-advertisement invariant

A host may advertise protocol version 5 only when all guarantees below are functional:

```text
revit.get_context
revit.query_elements
revit.get_elements
revit.describe_parameters
```

This means v5 inherits v2/v3/v4 capabilities rather than replacing them.

Examples:

```text
get-context only                                                       -> highest protocol 2
get-context + query-elements                                           -> highest protocol 3
get-context + query-elements + get-elements                            -> highest protocol 4
get-context + query-elements + get-elements + describe-parameters      -> highest protocol 5
describe-parameters without the inherited prefix                       -> handshake-only protocol 1
get-elements + describe-parameters, no get-context                     -> handshake-only protocol 1
```

Registration metadata publishes the actual highest supported bridge protocol only after all services required by that protocol are functional and the listener is ready, consistent with ADR-0003 and LIFECYCLE-0001.

## Revit execution path

Required path:

```text
Named Pipe background thread
    -> JSON-RPC revit.describe_parameters
    -> Addin CAP-0004 service
    -> RevitExecutionDispatcher.EnqueueAsync
    -> ExternalEvent.Raise
    -> ExternalEvent.Execute
    -> UIApplication / active Document
    -> validate document_id
    -> resolve element refs
    -> collect visible instance/type parameters
    -> classify identity and data type
    -> aggregate, filter, sort, limit
    -> structured DescribeParametersResult
    -> JSON-RPC response
```

The Named Pipe/background thread must not directly access:

- active document/view;
- elements or ElementTypes;
- parameters;
- `Element.UniqueId`;
- parameter definitions or ForgeTypeIds.

No Revit transaction may be created.

## CAP-0004 behavior at the Bridge boundary

Bridge transports the CAP-0004 request/result and stable error model only.

The Addin capability service owns deterministic Revit-side behavior including:

- request validation that is meaningful below MCP;
- active-document identity guard;
- `element_ref` resolution;
- `ok` / `not_found` item status;
- visible instance/type parameter discovery;
- identity and data-type classification;
- `parameter_ref` assignment;
- aggregation, filtering, ordering, and truncation.

No Revit API object may escape through the Bridge.

## Typed Bridge client

The RevitMCP-owned Bridge client abstraction adds an explicit typed operation conceptually like:

```text
DescribeParametersAsync(
    DescribeParametersRequest request,
    TimeSpan timeout,
    CancellationToken cancellationToken)
```

The client must:

- require a successful handshake first;
- allow `GetContextAsync` only for explicitly supported `{2,3,4,5}`;
- allow `QueryElementsAsync` only for explicitly supported `{3,4,5}`;
- allow `GetElementsAsync` only for explicitly supported `{4,5}`;
- allow `DescribeParametersAsync` only for explicitly supported `{5}`;
- reject v1/v2/v3/v4 describe-parameters locally without sending unsupported RPC traffic;
- reject unknown future versions until explicitly documented;
- preserve the established unusable-after-capability-timeout behavior.

## Cancellation and timeout semantics

Existing EXEC-0001 / BRIDGE-0002 / BRIDGE-0003 / BRIDGE-0004 semantics remain authoritative:

- positive capability timeout required by typed Bridge client;
- caller cancellation before queued work begins prevents execution;
- local capability waits are bounded;
- timeout maps to `REVIT_EXECUTION_TIMEOUT`;
- timeout cancels the in-flight RPC so still-queued work can be skipped;
- the caller must not wait indefinitely for a silent peer to acknowledge cancellation;
- once Revit API execution starts, cancellation/timeout does not forcibly abort the Revit thread;
- no automatic retries.

CAP-0004 does not expose an agent-supplied timeout.

## Errors

Capability-level errors transported through the Bridge include:

```text
NO_ACTIVE_DOCUMENT
DOCUMENT_CONTEXT_CHANGED
INVALID_PARAMETER_DISCOVERY
REVIT_EXECUTION_TIMEOUT
REVIT_EXECUTION_FAILED
```

`not_found` remains an item-level success status and is not converted to a Bridge exception.

Handshake/protocol errors remain existing BRIDGE-* errors.

## Context-size requirements

Bridge payloads preserve the accepted CAP-0004 bounded shape even though the transport is local:

- at most 10 element status items;
- at most 100 returned parameter descriptors;
- default 50 descriptors;
- no parameter values;
- no hidden/API-only parameters;
- no internal diagnostics or Revit objects.

Do not expand the local result with hidden metadata merely because the MCP adapter could later remove it.

## Automated validation requirements

At minimum cover:

- v5 full host advertises `[5,4,3,2,1]`;
- v4 host remains `[4,3,2,1]`;
- v3 host remains `[3,2,1]`;
- CAP-0001-only remains `[2,1]`;
- handshake-only remains `[1]`;
- incomplete service combinations never advertise v5;
- explicit support sets are get-context `{2,3,4,5}`, query-elements `{3,4,5}`, get-elements `{4,5}`, describe-parameters `{5}`;
- unknown v6 does not automatically support any capability;
- raw JSON-RPC describe-parameters before handshake is rejected without invoking the service;
- raw JSON-RPC describe-parameters after v1/v2/v3/v4 is rejected without invoking the service;
- describe-parameters after v5 invokes the service;
- get-context/query-elements/get-elements regressions work after v5;
- real Named Pipe typed describe-parameters round trip using a fake capability service;
- CAP-0004 capability errors survive StreamJsonRpc mapping;
- timeout, queued remote cancellation, caller cancellation, and client disposal semantics remain correct.

## Live validation

Before BRIDGE-0005 is considered implemented, validate on Autodesk Revit 2026.5 with a modeled Autodesk sample:

```text
registration.bridge_protocol_version = 5
handshake [5,4,3,2,1] -> selected 5
GetContextAsync -> success
QueryElementsAsync -> success
GetElementsAsync -> success
DescribeParametersAsync -> success
```

The describe-parameters proof must travel through the real typed `NamedPipeBridgeClient`, not invoke the Addin capability service directly.

The live data cases are owned by CAP-0004 and SERVER-0004; use public/disposable Autodesk sample content and do not commit an `.rvt`.

## Acceptance criteria

1. protocol version 5 is explicitly introduced;
2. v5 guarantees handshake + get-context + query-elements + get-elements + describe-parameters;
3. capability sets are explicit `{2,3,4,5}`, `{3,4,5}`, `{4,5}`, `{5}` rather than numeric comparisons;
4. a v4-only host remains valid and does not advertise v5;
5. v5 is advertised only when all inherited/current capability services are functional;
6. `revit.describe_parameters` is host-gated by the negotiated per-connection protocol;
7. raw peers cannot invoke describe-parameters without negotiating v5;
8. request/result contracts remain transport-neutral;
9. document identity, element-ref interpretation, and parameter-ref assignment remain Addin-owned;
10. item-level `not_found` survives the Bridge as a normal result;
11. all discovery Revit API work uses EXEC-0001;
12. no transaction is created;
13. timeout/cancellation semantics remain consistent with existing capabilities;
14. automated tests cover negotiation, service-combination advertisement, server/client gating, round trip, errors, timeout, and cancellation;
15. live typed-Bridge validation proves v5 plus regressions for inherited capabilities.

## Explicitly deferred

- generic capability discovery/registry;
- SERVER-0004 stdio MCP tool registration;
- inactive-document targeting;
- linked-document addressing;
- typed parameter values;
- parameter writes;
- accepting GUID or ForgeTypeId as a caller-supplied parameter target.

## References

- ADR-0005: Agent context and token efficiency
- ADR-0006: Document and element reference identity
- CAP-0004: `revit_describe_parameters`
- BRIDGE-0001: handshake and version negotiation
- BRIDGE-0002: `revit.get_context`
- BRIDGE-0003: `revit.query_elements`
- BRIDGE-0004: `revit.get_elements`
- EXEC-0001: serialized Revit execution dispatcher
