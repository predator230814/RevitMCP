# BRIDGE-0011: `revit.get_warnings` capability RPC

- Status: Implemented. Typed-Bridge and official MCP-client live validation on Revit 2026.5 build `26.5.0.55` at SHA `47b2784ae90b8ae08f3ffb64fc8345f779647184` is **PASS**. `tools/list` was exactly 10 and the selected protocol was 11. Induced Revit failure-dialog behavior was not executed and remains deferred validation debt.
- Date: 2026-10-09
- Introduced bridge protocol version: 11

## Purpose

Define the local JSON-RPC Bridge operation that implements Accepted CAP-0009 `revit_get_warnings` and introduce bridge protocol version 11.

This specification preserves the explicit typed RPC approach established by BRIDGE-0002 through BRIDGE-0010. The RPC, protocol sets, Contracts, Addin, Bridge, tests, and the Server tool are implemented. It does not introduce a generic capability registry, command envelope, or reflection-driven dispatch system. Typed-Bridge and official MCP live validation on Revit 2026.5 are **PASS**.

## Related accepted decisions

- ADR-0001: out-of-process MCP server and in-process Revit capability host
- ADR-0002: Named Pipes + JSON-RPC local bridge
- ADR-0003: Revit instance registration/discovery/addressing
- ADR-0005: agent context and token efficiency
- ADR-0006: document and element reference identity
- BRIDGE-0001: handshake and protocol negotiation
- BRIDGE-0002 through BRIDGE-0010: the current capability RPCs
- EXEC-0001: serialized Revit execution dispatcher
- CAP-0009: `revit_get_warnings`

## Bridge protocol version 11

Protocol version 11 adds `revit.get_warnings` and explicitly preserves every version-10 guarantee.

The capability matrix is explicit, never numeric `>=`:

```text
get_context                      {2,3,4,5,6,7,8,9,10,11}
query_elements                   {3,4,5,6,7,8,9,10,11}
get_elements                     {4,5,6,7,8,9,10,11}
describe_parameters              {5,6,7,8,9,10,11}
get_parameter_values             {6,7,8,9,10,11}
get_mep_topology                 {7,8,9,10,11}
preview_parameter_updates        {8,9,10,11}
request_parameter_update_review  {9,10,11}
apply_parameter_updates          {10,11}
get_warnings                     {11}
```

A full host advertises:

```text
[11,10,9,8,7,6,5,4,3,2,1]
```

only when get-warnings is composed with the complete v10 prefix, including apply, request-review, and preview.

A complete v10 host without get-warnings continues to advertise:

```text
[10,9,8,7,6,5,4,3,2,1]
```

Lower prefixes remain unchanged, including a request-review host without apply at v9, a preview host without request-review at v8, and the earlier read-only prefixes down to handshake-only `[1]`.

Get-warnings alone, or get-warnings without the complete v10 prefix, does not advertise v11. It falls back to the highest complete prefix that is actually present. With no inherited capabilities, that fallback is handshake-only v1.

Protocol v11 is implemented. Unknown v12 supports nothing until a later specification says otherwise.

## Explicit capability-version rule

Do not implement capability checks as `version >= N`.

Unknown future versions must not implicitly support any capability. A future version must explicitly document which guarantees it preserves before Server or Bridge code treats it as compatible.

## RPC method

The JSON-RPC method is:

```text
revit.get_warnings
```

Request/result types come from the CAP-0009 transport-neutral Contracts:

```text
GetWarningsRequest
GetWarningsResult
```

Those types live in `RevitMCP.Contracts`.

The Bridge must not add:

- `instance_id` on the request;
- MCP metadata;
- transport metadata;
- caller-selected timeout;
- resolution identifiers;
- numeric `ElementId` values.

`instance_id` remains resolved by the Server before opening the selected Named Pipe.

`document_id`, `failure_key`, and `element_ref` pass through unchanged as opaque values.

The Bridge must not trim, parse, normalize, manufacture, or reinterpret those strings.

## CAP-0009 ownership

CAP-0009 owns the business contract:

- request validation and defaults;
- exact document guard;
- severity mapping;
- failure-key identity;
- definition rollup and the 50-row cap;
- warning order and the `max_warnings` prefix;
- element admission, unresolved ids, and description truncation;
- `unmatched_element_refs` presence rules;
- truncation reasons;
- the rule that the source is `Document.GetWarnings()` and that no resolution, deletion, suppression, dialog, or transaction runs.

BRIDGE-0011 must not duplicate or redefine those rules.

The Addin owns all Revit API interpretation and execution.

ADR-0006 remains authoritative for `document_id` and `element_ref`.

Result `context.instance_id` is produced by the capability implementation for the process that executed the read. The Bridge must not invent it from the pipe name, process id, or MCP routing input.

## Transport-neutral request

Conceptually:

```text
GetWarningsRequest
{
    document_id: string
    severity?: warning | error | document_corruption | other
    failure_key?: string
    element_refs?: string[1..10]
    max_warnings?: integer
    max_elements_per_warning?: integer
}
```

The exact validation and opaque-id semantics are owned by CAP-0009.

The request must not contain:

- `instance_id`;
- MCP metadata;
- model-provider metadata;
- pipe/process/session identifiers;
- file paths;
- an agent-supplied execution timeout.

`document_id` remains in the capability request because it is a correctness guard that must be verified against the active Revit `Document` inside EXEC-0001.

Request/result/status contract types belong in `RevitMCP.Contracts` and must not reference StreamJsonRpc, Named Pipes, MCP SDK types, or Autodesk Revit API types.

## Result contract

Conceptually:

```text
GetWarningsResult
{
    context
      instance_id
      document_id
    matched_count
    counts_by_severity
    definitions[]
    warnings[]
    unmatched_element_refs[]    // only when the request included element_refs
    truncated
    truncation_reasons[]
}
```

CAP-0009 item fields, severity strings, element roles, and truncation reasons remain structured result data. They are not converted into Bridge exceptions.

An empty warning set and unknown element refs remain a successful structured result.

The Bridge must not expand, reinterpret, parse, or enrich `document_id`, `failure_key`, or `element_ref`.

The Bridge must not sort, rewrite, or re-derive definitions, warnings, counts, or truncation reasons. CAP-0009 / Addin shaping is authoritative.

The Bridge must not insert `unmatched_element_refs` when the request omitted `element_refs`, and must not drop it when the request included `element_refs`.

## Bridge service composition

Explicit typed service composition remains the rule.

Introduce a future Bridge service interface conceptually:

```text
IRevitGetWarningsService
  GetWarningsAsync(
      GetWarningsRequest request,
      CancellationToken cancellationToken)
```

The host remains explicitly composed. A v11 host adds `IRevitGetWarningsService` beside the existing v10 services:

```text
IRevitCapabilityService
IRevitQueryElementsService
IRevitGetElementsService
IRevitDescribeParametersService
IRevitGetParameterValuesService
IRevitGetMepTopologyService
IRevitPreviewParameterUpdatesService
IRevitRequestParameterUpdateReviewService
IRevitApplyParameterUpdatesService
IRevitGetWarningsService
```

`IRevitGetWarningsService` is implemented by `RevitMCP.Addin` and uses EXEC-0001.

Do not introduce:

- a generic capability registry;
- reflection-driven dispatch;
- generic `InvokeCapability`;
- a dynamic command dictionary;
- MCP SDK dependencies below `RevitMCP.Server`;
- Autodesk Revit API dependencies in `RevitMCP.Bridge` or `RevitMCP.Contracts`.

## Protocol-advertisement invariant

A host may advertise protocol version 11 only when the complete inherited v10 prefix plus get-warnings is functional.

Any incomplete or non-prefix combination must fall back to the highest actually guaranteed accepted prefix.

Registration metadata must publish v11 only after all v11-required services are functional and the listener is ready, consistent with ADR-0003 and LIFECYCLE-0001.

## Host gating

The per-connection StreamJsonRpc adapter must enforce negotiated support independently of the typed client.

Rules:

```text
no handshake
  -> reject

v1..v10
  -> reject revit.get_warnings

v11
  -> allow

unknown v12
  -> reject until explicitly documented
```

Inherited methods keep their current gates and gain `11` in each explicit support set above.

The underlying Addin CAP-0009 service must not execute when protocol gating fails.

Raw JSON-RPC peers must not bypass this gate.

## Typed client

Future `IRevitBridgeClient` addition:

```text
Task<GetWarningsResult> GetWarningsAsync(
    GetWarningsRequest request,
    TimeSpan timeout,
    CancellationToken cancellationToken)
```

Typed client behavior:

- require a successful handshake;
- require a positive local timeout;
- allow each inherited method only on the explicit set listed above;
- allow get-warnings only on `{11}`;
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

CAP-0009 exposes no agent-supplied timeout.

## Errors

Transport CAP-0009 capability errors unchanged:

```text
NO_ACTIVE_DOCUMENT
DOCUMENT_CONTEXT_CHANGED
INVALID_WARNINGS
REVIT_EXECUTION_TIMEOUT
REVIT_EXECUTION_FAILED
```

Empty results and item-level truncation remain normal successful structured results and are not converted into Bridge exceptions.

Handshake/protocol failures remain existing Bridge errors.

Server routing errors stay in the Server. They are not Bridge results.

## Execution path

Intended path:

```text
NamedPipeBridgeClient
  -> handshake negotiate v11
  -> revit.get_warnings
  -> StreamJsonRpc host adapter
  -> IRevitGetWarningsService
  -> Addin CAP-0009 implementation
  -> EXEC-0001
  -> active-document guard
  -> Document.GetWarnings()
  -> GetWarningsResult
```

The Bridge itself must not execute Revit API work.

The Named Pipe/background thread must not directly access the active document, warnings, or elements.

No Revit transaction may be created.

## Contract dependency boundaries

`RevitMCP.Contracts` may contain only transport-neutral CAP-0009 request/result types.

It must not reference:

- Autodesk Revit API;
- StreamJsonRpc;
- Named Pipes;
- MCP SDK;
- Server routing types.

`RevitMCP.Bridge` may reference Contracts and StreamJsonRpc but must remain Revit-API independent.

`RevitMCP.Addin` owns `Document.GetWarnings()`, severity mapping, and element-ref resolution.

`RevitMCP.Server` exposes `revit_get_warnings`. Official MCP live validation on Revit 2026.5 is **PASS**.

## Handshake advertisement

A full host advertises:

```text
SupportedVersions = [11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1]
CurrentVersion = 11
```

Handshake itself remains cached metadata only. It must not inspect the Revit model or invoke EXEC-0001.

## Acceptance criteria

BRIDGE-0011 is acceptable as a specification when:

1. Protocol 11 adds exactly `revit.get_warnings` and preserves the explicit inherited sets above.
2. Capability support is explicit set membership, never numeric `>=`.
3. Unknown v12 supports none until explicitly accepted.
4. A host may advertise `[11,10,9,8,7,6,5,4,3,2,1]` only when the complete v10 prefix plus get-warnings is functional.
5. Get-warnings without that prefix does not advertise v11.
6. The RPC uses transport-neutral `GetWarningsRequest` / `GetWarningsResult` with no request `instance_id`.
7. Host and typed-client gating reject get-warnings on v1..v10 and unknown v12.
8. Inherited methods explicitly include v11 after implementation.
9. Timeouts, cancellation, and no-retry behavior reuse the existing capability model.
10. Empty warnings and unknown element refs remain successful structured results.
11. The Bridge does not reshape CAP-0009 results, including the optional `unmatched_element_refs` field.
12. No generic registry, reflection dispatch, or Revit API leakage into Contracts/Bridge.
13. Later automated tests cover advertisement, gating, inherited v11 eligibility, unknown v12 rejection, and capability-error survival. Addin coverage required by CAP-0009 stays in the Addin tests. Bridge tests do not redefine those rules.
14. Typed-Bridge live validation and the later official MCP live validation on Revit 2026.5 are **PASS** at SHA `47b2784ae90b8ae08f3ffb64fc8345f779647184`.
15. Bridge, Addin, and Server code for this RPC are implemented. The live result is recorded separately from the mere existence of that code.

## Explicitly deferred

- warning resolution, deletion, or suppression;
- transaction failure dialogs;
- generic capability registry;
- numeric protocol compatibility.

## References

- BRIDGE-0001 through BRIDGE-0010
- CAP-0009: `revit_get_warnings`
- ADR-0002: Named Pipes + JSON-RPC local bridge
- ADR-0005: agent context and token efficiency
- ADR-0006: document and element reference identity
- EXEC-0001: serialized Revit execution dispatcher
