# BRIDGE-0007: `revit.get_mep_topology` capability RPC

- Status: Accepted
- Date: 2026-09-22
- Introduced bridge protocol version: 7

## Purpose

Define the local JSON-RPC Bridge operation that implements CAP-0006 `revit_get_mep_topology` and introduce bridge protocol version 7.

This specification preserves the explicit typed RPC approach established by BRIDGE-0002 through BRIDGE-0006. It does not implement Contracts, Bridge code, Addin code, tests, Server registration, or MCP schemas. It does not introduce a generic capability registry, command envelope, or reflection-driven dispatch system.

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
- BRIDGE-0006: `revit.get_parameter_values`
- EXEC-0001: serialized Revit execution dispatcher
- CAP-0006: `revit_get_mep_topology`

## Bridge protocol version 7

Protocol version 7 adds `revit.get_mep_topology` and explicitly preserves all version-6 guarantees.

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

protocol 7
  bridge.handshake
  revit.get_context
  revit.query_elements
  revit.get_elements
  revit.describe_parameters
  revit.get_parameter_values
  revit.get_mep_topology
```

A fully capable future host should advertise:

```text
[7, 6, 5, 4, 3, 2, 1]
```

A host implementing the current v6 surface but not CAP-0006 continues to advertise:

```text
[6, 5, 4, 3, 2, 1]
```

Lower prefixes remain unchanged:

```text
[5, 4, 3, 2, 1]
[4, 3, 2, 1]
[3, 2, 1]
[2, 1]
[1]
```

This specification does not claim that protocol v7 is implemented.

## Explicit capability-version rule

Protocol integers are not an automatic numeric compatibility ladder.

Accepted support sets after BRIDGE-0007 implementation:

```text
revit.get_context              {2,3,4,5,6,7}
revit.query_elements           {3,4,5,6,7}
revit.get_elements             {4,5,6,7}
revit.describe_parameters      {5,6,7}
revit.get_parameter_values     {6,7}
revit.get_mep_topology         {7}
```

Do not implement capability checks as:

```text
version >= 2
version >= 3
version >= 4
version >= 5
version >= 6
version >= 7
```

Unknown future v8 must not implicitly support any capability. A future version must explicitly document which guarantees it preserves before Server or Bridge code treats it as compatible.

## RPC method

The JSON-RPC method is:

```text
revit.get_mep_topology
```

Request/result types come from the CAP-0006 transport-neutral Contracts:

```text
GetMepTopologyRequest
GetMepTopologyResult
```

Those types do not exist in the current repository. This specification names them; it does not add them.

The Bridge must not add:

- `instance_id`;
- MCP metadata;
- transport metadata;
- caller-selected timeout;
- connector identifiers;
- system identifiers;
- direction flags.

`instance_id` remains resolved by the Server before opening the selected Named Pipe.

`document_id` and `element_ref` pass through unchanged as opaque values.

The Bridge must not trim, parse, normalize, manufacture, or reinterpret those strings.

## CAP-0006 ownership

CAP-0006 owns the business contract:

- request validation;
- exact document guard;
- seed statuses;
- physical-connection semantics, including the requirement that an edge comes from a Revit physical connection rather than a merely non-logical reference;
- domain normalization;
- deterministic multi-source BFS, including the rule that a non-seed node is admitted only through an emitted edge and that an edge omitted for `max_edges` is not a hidden traversal path;
- node/edge ordering and edge canonicalization;
- truncation reasons;
- result shaping.

BRIDGE-0007 must not duplicate or redefine those rules.

The Addin owns all Revit API interpretation and execution.

ADR-0006 remains authoritative for `document_id` and `element_ref`.

## Transport-neutral request

Conceptually:

```text
GetMepTopologyRequest
{
    document_id: string
    seed_element_refs[1..10]
    domain?: hvac | piping | electrical | cable_tray_conduit
    max_depth?: integer
    max_elements?: integer
    max_edges?: integer
}
```

The exact validation and opaque-id semantics are owned by CAP-0006.

The request must not contain:

- `instance_id`;
- MCP metadata;
- model-provider metadata;
- pipe/process/session identifiers;
- file paths;
- an agent-supplied execution timeout;
- connector or system identity.

`document_id` remains in the capability request because it is a correctness guard that must be verified against the active Revit `Document` inside EXEC-0001.

Request/result/status contract types belong in `RevitMCP.Contracts` and must not reference StreamJsonRpc, Named Pipes, MCP SDK types, or Autodesk Revit API types.

## Result contract

Conceptually:

```text
GetMepTopologyResult
{
    context
      instance_id
      document_id
    seeds[]
    nodes[]
    edges[]
    truncated
    truncation_reasons[]
}
```

Per-seed CAP-0006 statuses remain structured result items:

```text
ok
not_found
no_connectors
```

Those statuses are not converted into Bridge exceptions.

The Bridge must not expand, reinterpret, parse, or enrich `document_id` or `element_ref`.

The Bridge must not sort, rewrite, or re-derive nodes, edges, or truncation reasons. CAP-0006 / Addin shaping is authoritative.

## Bridge service composition

Explicit typed service composition remains the rule.

Introduce a future Bridge service interface conceptually:

```text
IRevitGetMepTopologyService
  GetMepTopologyAsync(
      GetMepTopologyRequest request,
      CancellationToken cancellationToken)
```

The host remains explicitly composed.

Conceptual v7 composition:

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
    |     GetParameterValuesAsync(...)
    |
    +-- IRevitGetMepTopologyService
          GetMepTopologyAsync(...)
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

A host may advertise protocol version 7 only when the complete inherited prefix plus get-mep-topology is functional:

```text
get-context
+ query-elements
+ get-elements
+ describe-parameters
+ get-parameter-values
+ get-mep-topology
```

Examples:

```text
get-context only                                                                              -> highest 2
get-context + query                                                                           -> highest 3
get-context + query + get-elements                                                            -> highest 4
get-context + query + get-elements + describe                                                 -> highest 5
get-context + query + get-elements + describe + get-parameter-values                          -> highest 6
get-context + query + get-elements + describe + get-parameter-values + get-mep-topology       -> highest 7
```

Any incomplete or non-prefix combination must fall back to the highest actually guaranteed accepted prefix.

Examples:

```text
get-mep-topology only                                                                         -> handshake-only v1
v6 services without get-mep-topology                                                          -> highest v6
get-mep-topology without inherited capabilities                                               -> handshake-only v1
```

Registration metadata must publish v7 only after all v7-required services are functional and the listener is ready, consistent with ADR-0003 and LIFECYCLE-0001.

## Host gating

The per-connection StreamJsonRpc adapter must enforce negotiated support independently of the typed client.

Rules:

```text
no handshake
  -> reject

v1..v6
  -> reject revit.get_mep_topology

v7
  -> allow

unknown v8
  -> reject until explicitly documented
```

Inherited v6 host gating remains in force for the older methods, now including v7 in their explicit support sets.

The underlying Addin CAP-0006 service must not execute when protocol gating fails.

Raw JSON-RPC peers must not bypass this gate.

## Typed client

Future `IRevitBridgeClient` addition:

```text
Task<GetMepTopologyResult> GetMepTopologyAsync(
    GetMepTopologyRequest request,
    TimeSpan timeout,
    CancellationToken cancellationToken)
```

Typed client behavior:

- require a successful handshake;
- require a positive local timeout;
- allow get-context only on `{2,3,4,5,6,7}`;
- allow query only on `{3,4,5,6,7}`;
- allow get-elements only on `{4,5,6,7}`;
- allow describe-parameters only on `{5,6,7}`;
- allow get-parameter-values only on `{6,7}`;
- allow get-mep-topology only on `{7}`;
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

CAP-0006 exposes no agent-supplied timeout.

## Errors

Transport CAP-0006 capability errors unchanged:

```text
NO_ACTIVE_DOCUMENT
DOCUMENT_CONTEXT_CHANGED
INVALID_MEP_TOPOLOGY
REVIT_EXECUTION_TIMEOUT
REVIT_EXECUTION_FAILED
```

Per-seed CAP-0006 statuses remain normal successful structured results and are not converted into Bridge exceptions.

Handshake/protocol failures remain existing Bridge errors.

## Execution path

Intended path:

```text
NamedPipeBridgeClient
  -> handshake negotiate v7
  -> revit.get_mep_topology
  -> StreamJsonRpc host adapter
  -> IRevitGetMepTopologyService
  -> Addin CAP-0006 implementation
  -> EXEC-0001
  -> active-document guard
  -> resolve seed_element_refs
  -> Revit physical-connection neighborhood
  -> deterministic multi-source BFS with emitted-edge node admission
  -> GetMepTopologyResult
```

The Bridge itself must not execute Revit API work.

The Named Pipe/background thread must not directly access the active document, elements, connectors, UniqueIds, or MEP systems.

No Revit transaction may be created.

## Contract dependency boundaries

`RevitMCP.Contracts` may contain only transport-neutral CAP-0006 request/result/status/domain types.

It must not reference:

- Autodesk Revit API;
- StreamJsonRpc;
- Named Pipes;
- MCP SDK;
- Server routing types.

`RevitMCP.Bridge` may reference Contracts and StreamJsonRpc but must remain Revit-API independent.

`RevitMCP.Addin` owns connector-manager access, domain mapping, and BFS execution.

## Handshake advertisement

Current implemented runtime remains protocol v6 until a later implementation PR.

After BRIDGE-0007 implementation, a full CAP-0001..CAP-0006 host advertises:

```text
SupportedVersions = [7, 6, 5, 4, 3, 2, 1]
CurrentVersion = 7
```

Handshake itself remains cached metadata only. It must not inspect the Revit model or invoke EXEC-0001.

## Acceptance criteria

BRIDGE-0007 is acceptable as a specification when:

1. Protocol 7 adds exactly `revit.get_mep_topology` and preserves the explicit inherited sets above.
2. Capability support is explicit set membership, never numeric `>=`.
3. Unknown v8 supports none until explicitly accepted.
4. A host may advertise `[7,6,5,4,3,2,1]` only when the complete inherited prefix plus get-mep-topology is functional.
5. Incomplete compositions fall back to the highest valid accepted prefix.
6. The RPC uses transport-neutral `GetMepTopologyRequest` / `GetMepTopologyResult` with no `instance_id`.
7. Host and typed-client gating reject get-mep-topology on v1..v6 and unknown v8.
8. Inherited methods explicitly include v7 after implementation.
9. Timeouts, cancellation, and no-retry behavior reuse the existing capability model.
10. Per-seed statuses remain successful structured results.
11. No generic registry, reflection dispatch, or Revit API leakage into Contracts/Bridge.
12. Later automated tests cover advertisement, gating, inherited v7 eligibility, unknown v8 rejection, and capability-error survival. Addin coverage required by CAP-0006 proves that non-physical references do not become edges and that `max_edges` interacts with node admission. Bridge tests do not redefine those rules.
13. Later typed-Bridge live validation is required before SERVER-0006 official MCP live validation. That live gate exercises real physical adjacency under the CAP-0006 physical-connection contract.
14. This specification PR does not implement Bridge or Addin code.

## Explicitly deferred

- logical connector RPC;
- connector-level graph;
- linked-document topology;
- flow-direction metadata;
- generic capability registry;
- numeric protocol compatibility.

## References

- BRIDGE-0001 through BRIDGE-0006
- CAP-0006: `revit_get_mep_topology`
- ADR-0002: Named Pipes + JSON-RPC local bridge
- ADR-0003: instance registration/discovery
- EXEC-0001: serialized Revit execution dispatcher
