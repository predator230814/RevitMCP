# BRIDGE-0006: `revit.get_parameter_values` capability RPC

- Status: Accepted
- Date: 2026-09-21
- Introduced bridge protocol version: 6

## Purpose

Define the local JSON-RPC Bridge operation that implements CAP-0005 `revit_get_parameter_values` and introduce bridge protocol version 6.

This specification preserves the explicit typed RPC approach established by BRIDGE-0002 through BRIDGE-0005. It does not implement Contracts, Bridge code, Addin code, tests, Server registration, or MCP schemas. It does not introduce a generic capability registry, command envelope, or reflection-driven dispatch system.

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
- BRIDGE-0005: `revit.describe_parameters`
- EXEC-0001: serialized Revit execution dispatcher
- CAP-0004: `revit_describe_parameters`
- CAP-0005: `revit_get_parameter_values`

## Bridge protocol version 6

Protocol version 6 adds `revit.get_parameter_values` and explicitly preserves all version-5 guarantees.

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

protocol 6
  bridge.handshake
  revit.get_context
  revit.query_elements
  revit.get_elements
  revit.describe_parameters
  revit.get_parameter_values
```

A fully capable future host should advertise:

```text
[6, 5, 4, 3, 2, 1]
```

A host implementing the current v5 surface but not CAP-0005 continues to advertise:

```text
[5, 4, 3, 2, 1]
```

Lower prefixes remain unchanged:

```text
[4, 3, 2, 1]
[3, 2, 1]
[2, 1]
[1]
```

This specification does not claim that protocol v6 is implemented.

## Explicit capability-version rule

Protocol integers are not an automatic numeric compatibility ladder.

Accepted support sets:

```text
revit.get_context              {2,3,4,5,6}
revit.query_elements           {3,4,5,6}
revit.get_elements             {4,5,6}
revit.describe_parameters      {5,6}
revit.get_parameter_values     {6}
```

Do not implement capability checks as:

```text
version >= 2
version >= 3
version >= 4
version >= 5
version >= 6
```

Unknown future v7 must not implicitly support any capability. A future version must explicitly document which guarantees it preserves before Server or Bridge code treats it as compatible.

## RPC method

The JSON-RPC method is:

```text
revit.get_parameter_values
```

Request/result types come from the CAP-0005 transport-neutral Contracts:

```text
GetParameterValuesRequest
GetParameterValuesResult
```

Those types do not exist in the current repository. This specification names them; it does not add them.

The Bridge must not add:

- `instance_id`;
- MCP metadata;
- transport metadata;
- caller-selected timeout;
- display-name parameter lookup.

`instance_id` remains resolved by the Server before opening the selected Named Pipe.

`document_id`, `element_ref`, and `parameter_ref` pass through unchanged as opaque values.

The Bridge must not trim, parse, normalize, manufacture, or reinterpret those strings.

## CAP-0005 ownership

CAP-0005 owns the business contract:

- request validation;
- exact document guard;
- explicit pair ordering;
- item statuses;
- parameter-ref resolution semantics;
- typed value semantics;
- quantity/unit conversion;
- no-value semantics;
- result shaping.

BRIDGE-0006 must not duplicate or redefine those rules.

The Addin owns all Revit API interpretation and execution.

## Parameter identity ownership

Recorded ownership:

- CAP-0004 already owns `parameter_ref` creation;
- CAP-0005 requires Addin-owned reverse resolution from `parameter_ref` to its document-lifetime source plus parameter identity binding;
- Bridge Contracts must never expose internal stable keys or Revit API objects;
- the implementation may extend `OpenDocumentParameterIdentityService` or an equivalent Addin-owned identity component;
- no `Parameter`, `Definition`, `ElementId`, or other Revit API object may be retained in transport-neutral Contracts or exposed through the Bridge;
- document-close cleanup must invalidate both forward and reverse parameter-ref mappings together.

Do not prescribe the internal dictionary shape in this specification.

ADR-0006 remains authoritative for `document_id` and `element_ref`.

## Transport-neutral request

Conceptually:

```text
GetParameterValuesRequest
{
    document_id: string
    reads[1..50]
      element_ref: string
      parameter_ref: string
}
```

The exact validation and opaque-id semantics are owned by CAP-0005.

The request must not contain:

- `instance_id`;
- MCP metadata;
- model-provider metadata;
- pipe/process/session identifiers;
- file paths;
- an agent-supplied execution timeout;
- a caller-selected output unit;
- display names used as identity.

`document_id` remains in the capability request because it is a correctness guard that must be verified against the active Revit `Document` inside EXEC-0001.

Request/result/value/status contract types belong in `RevitMCP.Contracts` and must not reference StreamJsonRpc, Named Pipes, MCP SDK types, or Autodesk Revit API types.

## Result contract

Conceptually:

```text
GetParameterValuesResult
{
    context
      instance_id
      document_id
    items[]
}
```

Each result item is one requested pair in request order. Per-item CAP-0005 statuses remain structured result items:

```text
ok
element_not_found
parameter_ref_not_found
parameter_not_present
unsupported_value
```

Those statuses are not converted into Bridge exceptions.

The Bridge must not expand, reinterpret, parse, or enrich `document_id`, `element_ref`, or `parameter_ref`.

## Bridge service composition

Explicit typed service composition remains the rule.

Introduce a future Bridge service interface conceptually:

```text
IRevitGetParameterValuesService
  GetParameterValuesAsync(
      GetParameterValuesRequest request,
      CancellationToken cancellationToken)
```

The host remains explicitly composed.

Conceptual v6 composition:

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
    |     DescribeParametersAsync(...)
    |
    +-- IRevitGetParameterValuesService
          GetParameterValuesAsync(...)
          implemented by RevitMCP.Addin
          uses EXEC-0001
```

Do not introduce:

- a generic capability registry;
- reflection-driven dispatch;
- generic `InvokeCapability`;
- a dynamic command dictionary;
- MCP SDK dependencies below `RevitMCP.Server`;
- Autodesk Revit API dependencies in `RevitMCP.Bridge` or `RevitMCP.Contracts`.

## Protocol-advertisement invariant

A host may advertise protocol version 6 only when all inherited and new services are functional:

```text
get-context
+ query-elements
+ get-elements
+ describe-parameters
+ get-parameter-values
```

Examples:

```text
get-context only                                                         -> highest 2
get-context + query                                                      -> highest 3
get-context + query + get-elements                                       -> highest 4
get-context + query + get-elements + describe                            -> highest 5
get-context + query + get-elements + describe + get-parameter-values     -> highest 6
```

Any incomplete or non-prefix combination must fall back to the highest actually guaranteed accepted prefix.

Examples:

```text
get-parameter-values only                                                -> handshake-only v1
describe + get-parameter-values without inherited capabilities           -> handshake-only v1
v5 services without get-parameter-values                                 -> highest v5
```

Registration metadata must publish v6 only after all v6-required services are functional and the listener is ready, consistent with ADR-0003 and LIFECYCLE-0001.

## Host gating

The per-connection StreamJsonRpc adapter must enforce negotiated support independently of the typed client.

Rules:

```text
no handshake
  -> reject

v1..v5
  -> reject revit.get_parameter_values

v6
  -> allow

unknown v7
  -> reject until explicitly documented
```

Inherited v5 host gating remains in force for the older methods, now including v6 in their explicit support sets.

The underlying Addin CAP-0005 service must not execute when protocol gating fails.

Raw JSON-RPC peers must not bypass this gate.

## Typed client

Future `IRevitBridgeClient` addition:

```text
Task<GetParameterValuesResult> GetParameterValuesAsync(
    GetParameterValuesRequest request,
    TimeSpan timeout,
    CancellationToken cancellationToken)
```

Typed client behavior:

- require a successful handshake;
- require a positive local timeout;
- allow get-context only on `{2,3,4,5,6}`;
- allow query only on `{3,4,5,6}`;
- allow get-elements only on `{4,5,6}`;
- allow describe-parameters only on `{5,6}`;
- allow get-parameter-values only on `{6}`;
- reject unsupported versions locally before RPC transmission;
- reject unknown future protocol versions until explicitly documented;
- preserve unusable-after-capability-timeout behavior.

## Timeout / cancellation

Reuse the existing capability behavior. No new timeout model.

Requirements:

- positive typed-Bridge capability timeout;
- local wait bounded;
- timeout maps to `REVIT_EXECUTION_TIMEOUT`;
- in-flight RPC cancellation is requested;
- queued EXEC-0001 work may be skipped if cancellation arrives before execution;
- once Revit API execution has started, do not forcibly abort the Revit thread;
- caller cancellation remains distinct from local timeout behavior;
- a capability timeout leaves that Bridge client connection unusable, consistent with the current implementation;
- no automatic retries.

CAP-0005 exposes no agent-supplied timeout.

## Errors

Transport CAP-0005 capability errors unchanged:

```text
NO_ACTIVE_DOCUMENT
DOCUMENT_CONTEXT_CHANGED
INVALID_PARAMETER_READ
REVIT_EXECUTION_TIMEOUT
REVIT_EXECUTION_FAILED
```

Per-item CAP-0005 statuses remain normal successful structured results and are not converted into Bridge exceptions.

Handshake/protocol failures remain existing Bridge errors.

## Execution path

Intended path:

```text
NamedPipeBridgeClient
  -> handshake negotiate v6
  -> revit.get_parameter_values
  -> StreamJsonRpc host adapter
  -> IRevitGetParameterValuesService
  -> Addin CAP-0005 implementation
  -> EXEC-0001
  -> active-document guard
  -> resolve element_ref
  -> reverse-resolve parameter_ref
  -> re-find current visible Parameter
  -> typed read / safe quantity conversion
  -> GetParameterValuesResult
```

The Bridge itself must not execute Revit API work.

The Named Pipe/background thread must not directly access the active document, elements, parameters, UniqueIds, definitions, or ForgeTypeIds.

No Revit transaction may be created.

## Contract dependency boundaries

`RevitMCP.Contracts` may contain only transport-neutral CAP-0005 request/result/value/status types.

It must not reference:

- Autodesk Revit API;
- StreamJsonRpc;
- Named Pipes;
- MCP SDK;
- Server routing types.

`RevitMCP.Bridge` may reference Contracts and StreamJsonRpc but must remain Revit-API independent.

Actual parameter resolution and value extraction remain Addin-only.

## Context-size requirements

Bridge payloads preserve the accepted CAP-0005 bounded shape even though the transport is local:

- 1..50 explicit read pairs;
- exactly one result item per pair;
- no Cartesian expansion;
- no all-parameter mode;
- no whole-document scan;
- no raw internal-unit doubles;
- no numeric `ElementId` exposure.

Do not expand the local result with hidden metadata merely because the MCP adapter could later remove it.

## Automated test expectations for later implementation

Specify future coverage for at least:

1. `[6,5,4,3,2,1]` negotiation selects 6 when both peers support it.
2. Existing v5 host remains `[5,4,3,2,1]`.
3. Existing capability support sets include v6 explicitly.
4. get-parameter-values is supported only on v6.
5. unknown v7 gives no implicit support.
6. incomplete/non-prefix service composition does not advertise v6.
7. v6 registration only when all five capabilities are attached.
8. raw JSON-RPC call before handshake does not invoke the service.
9. raw call after v1..v5 does not invoke the service.
10. raw call after v6 invokes the service.
11. typed Named Pipe CAP-0005 request/result round-trip.
12. opaque strings are passed unchanged.
13. CAP-0005 Bridge exceptions survive StreamJsonRpc mapping.
14. per-item statuses remain normal successful structured results.
15. timeout/cancellation/unusable-after-timeout behavior matches existing capabilities.
16. inherited CAP-0001..CAP-0004 calls continue to work on a v6 host.

Do not require live Revit in automated Bridge tests.

## Live validation gate

After implementation, before SERVER-0005:

Use real Revit 2026.5 through the real typed `NamedPipeBridgeClient`.

Expected evidence:

```text
registration.bridge_protocol_version = 6
handshake [6,5,4,3,2,1] -> selected 6

CAP-0001 regression PASS
CAP-0002 regression PASS
CAP-0003 regression PASS
CAP-0004 regression PASS
CAP-0005 GetParameterValuesAsync PASS
```

CAP-0005 evidence should include, where safely available in the Autodesk sample:

- string;
- integer;
- measurable quantity such as Flow;
- ElementId-backed reference;
- no-value;
- unknown `element_ref`;
- unknown `parameter_ref`;
- valid `parameter_ref` absent on one target;
- opaque `parameter_ref` stable from CAP-0004 into CAP-0005;
- wrong document -> `DOCUMENT_CONTEXT_CHANGED`;
- no transaction/model modification.

If a naturally available sample does not exercise one value type, record the limitation rather than modifying the model merely to manufacture coverage.

Use a public/disposable Autodesk sample. Do not commit an `.rvt`.

## Acceptance criteria

1. Protocol version 6 is explicitly introduced and is not claimed implemented by this specification.
2. v6 guarantees handshake + get-context + query-elements + get-elements + describe-parameters + get-parameter-values.
3. Capability sets are exactly `{2,3,4,5,6}`, `{3,4,5,6}`, `{4,5,6}`, `{5,6}`, `{6}`; never numeric `>=`.
4. Unknown v7 has no implicit capability support.
5. Host and typed-client gating allow `revit.get_parameter_values` only after negotiated v6.
6. Raw peers cannot invoke the Addin service without that gate.
7. v6 is advertised only for the complete five-capability prefix; incomplete combinations fall back to the highest guaranteed prefix.
8. Service composition remains explicit typed interfaces, not a registry.
9. Request/result types are the CAP-0005 transport-neutral Contracts types.
10. CAP-0005 owns validation, document guard, pair order, item statuses, identity resolution, typed values, quantity conversion, and result shaping.
11. Addin owns reverse `parameter_ref` resolution and all Revit API value extraction.
12. Contracts never retain or expose Revit API objects or internal identity keys.
13. `document_id`, `element_ref`, and `parameter_ref` pass through unchanged as opaque strings.
14. Document-close cleanup invalidates forward and reverse parameter-ref mappings together.
15. Timeout/cancellation/unusable-after-timeout behavior matches existing capabilities.
16. Dependency boundaries keep Revit API out of Contracts/Bridge and MCP SDK out of Contracts/Bridge/Addin.
17. Later automated tests cover the sixteen listed negotiation, gating, round-trip, error, and regression cases without live Revit.
18. Typed Bridge live validation on Revit 2026.5 is required before SERVER-0005.

## Explicitly deferred

BRIDGE-0006 must not introduce:

- SERVER-0005;
- MCP tool registration;
- writes;
- write authorization;
- write transactions;
- a generic capability registry;
- dynamic Bridge capability discovery;
- cloud/remote Bridge transport;
- caller-selected quantity units;
- persistent parameter refs across document reopen.

## References

- ADR-0005: Agent context and token efficiency
- ADR-0006: Document and element reference identity
- CAP-0004: `revit_describe_parameters`
- CAP-0005: `revit_get_parameter_values`
- BRIDGE-0001: handshake and version negotiation
- BRIDGE-0005: `revit.describe_parameters`
- EXEC-0001: serialized Revit execution dispatcher
