# BRIDGE-0013: `revit.get_selection` capability RPC

- Status: Accepted specification; not implemented.
- Date: 2026-10-14
- Introduced bridge protocol version: 13

## Purpose

Define the local JSON-RPC Bridge operation that implements Accepted CAP-0010 `revit_get_selection`, and introduce bridge protocol version 13.

This specification preserves the explicit typed RPC approach established by BRIDGE-0002 through BRIDGE-0012. It does not introduce a generic capability registry, command envelope, or reflection-driven dispatch system.

Protocol v13, `revit.get_selection`, the Contracts types, the Addin service, and the typed client are not implemented by this specification. Production `BridgeProtocol.CurrentVersion` remains 12. Production still rejects protocol 13.

[BRIDGE-0012](BRIDGE-0012-context-document-identity.md) recorded that unknown protocol version 13 supports nothing until a later specification lists it. This specification is that later specification. It does not rewrite the v12 record.

## Related accepted decisions

- ADR-0001: out-of-process MCP server and in-process Revit capability host
- ADR-0002: Named Pipes + JSON-RPC local bridge
- ADR-0003: Revit instance registration/discovery/addressing
- ADR-0005: agent context and token efficiency
- ADR-0006: document and element reference identity
- BRIDGE-0001: handshake and protocol negotiation
- BRIDGE-0002 through BRIDGE-0012: the current capability RPCs and the v12 context amendments
- EXEC-0001: serialized Revit execution dispatcher
- CAP-0010: `revit_get_selection`

## Bridge protocol version 13

Protocol version 13 adds `revit.get_selection` and explicitly preserves the complete v12 guarantees.

The capability matrix is explicit set membership. Numeric `version >= N` checks are not a compatibility rule.

```text
get_context                      {2,3,4,5,6,7,8,9,10,11,12,13}
query_elements                   {3,4,5,6,7,8,9,10,11,12,13}
get_elements                     {4,5,6,7,8,9,10,11,12,13}
describe_parameters              {5,6,7,8,9,10,11,12,13}
get_parameter_values             {6,7,8,9,10,11,12,13}
get_mep_topology                 {7,8,9,10,11,12,13}
preview_parameter_updates        {8,9,10,11,12,13}
request_parameter_update_review  {9,10,11,12,13}
apply_parameter_updates          {10,11,12,13}
get_warnings                     {11,12,13}

CAP-0001 document_id guarantee   {12,13}
active_view query with filters={} {12,13}

get_selection                    {13}
```

A full v13 host advertises:

```text
[13,12,11,10,9,8,7,6,5,4,3,2,1]
```

A host may advertise v13 only when:

- the complete v12 prefix is present and functional;
- get-selection is present and functional.

The complete v12 prefix is the full host `[12,11,10,9,8,7,6,5,4,3,2,1]`, including get-warnings composed with the complete v10 prefix, the CAP-0001 `document_id` guarantee, and the CAP-0002 `active_view` empty-filter guarantee.

A complete v12 host without get-selection continues to advertise:

```text
[12,11,10,9,8,7,6,5,4,3,2,1]
```

Lower prefixes remain unchanged, including an apply host without get-warnings at v10, a request-review host without apply at v9, a preview host without request-review at v8, and the earlier prefixes down to handshake-only `[1]`.

Partial and non-prefix combinations fall back to the highest complete accepted prefix. Get-selection alone, or get-selection without the complete v12 prefix, does not advertise v13. With no inherited capabilities, that fallback is handshake-only v1.

Unknown protocol version 14 supports nothing. A future version must explicitly document which guarantees it preserves before Server or Bridge code treats it as compatible.

## Explicit capability-version rule

Do not implement capability checks as `version >= N`.

Every capability/version relationship in this specification is the set membership listed above.

Unknown future versions must not implicitly support any capability, including get-selection and the v12 document-identity guarantees.

## RPC method

The JSON-RPC method is:

```text
revit.get_selection
```

Request and result use the CAP-0010 transport-neutral contracts:

```text
GetSelectionRequest
GetSelectionResult
```

Those types live in `BimIt.Revit.Contracts`.

The Bridge request has no `instance_id`.

The Bridge must not add:

- `instance_id` on the request;
- MCP metadata;
- transport metadata;
- caller-selected timeout;
- numeric `ElementId` values;
- selection-mutation arguments.

`instance_id` remains resolved by the Server before opening the selected Named Pipe.

`document_id` and `element_ref` pass through unchanged as opaque values.

The Bridge must not trim, parse, normalize, manufacture, or reinterpret those strings.

## CAP-0010 ownership

CAP-0010 owns the business contract:

- request validation and the `limit` default;
- the optional exact document guard;
- the `GetElementIds()` source;
- resolution, ordinal uniqueness, ordinal sort, `selected_count`, and `truncated`;
- the rule that no transaction, selection mutation, or pick operation runs;
- the v1 exclusion of `GetReferences`, linked contents, and subelements.

BRIDGE-0013 must not duplicate or redefine those rules.

`BimIt.Revit.Addin` owns all Revit API interpretation and execution.

ADR-0006 remains authoritative for `document_id` and `element_ref`.

Result `context.instance_id` is produced by the capability implementation for the process that executed the read. The Bridge must not invent it from the pipe name, process id, or MCP routing input.

## Transport-neutral request

Conceptually:

```text
GetSelectionRequest
{
    document_id?: string
    limit?: integer
}
```

The exact validation and opaque-id semantics are owned by CAP-0010.

The request must not contain:

- `instance_id`;
- MCP metadata;
- model-provider metadata;
- pipe/process/session identifiers;
- file paths;
- an agent-supplied execution timeout.

`document_id`, when present, remains in the capability request because it is a correctness guard verified against the active Revit document inside EXEC-0001.

Request and result contract types belong in `BimIt.Revit.Contracts`. They must not reference StreamJsonRpc, Named Pipes, MCP SDK types, or Autodesk Revit API types.

## Result contract

Conceptually:

```text
GetSelectionResult
{
    context
      instance_id
      document_id
    selected_count
    truncated
    element_refs[]
}
```

An empty selection remains a successful structured result.

`truncated` remains structured result data. It is not converted into a Bridge exception.

The Bridge must not expand, reinterpret, parse, or enrich `document_id` or `element_ref`.

The Bridge must not sort, uniquify, recount, or re-derive `element_refs`, `selected_count`, or `truncated`. CAP-0010 / Addin shaping is authoritative.

## Bridge service composition

Explicit typed service composition remains the rule.

The typed service is:

```text
IRevitGetSelectionService
  GetSelectionAsync(
      GetSelectionRequest request,
      CancellationToken cancellationToken)
```

The host remains explicitly composed. A v13 host adds `IRevitGetSelectionService` beside the existing v12 services:

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
IRevitGetSelectionService
```

`IRevitGetSelectionService` is implemented by `BimIt.Revit.Addin` and uses EXEC-0001. It uses the existing open-document identity service. It does not create a second document-identity system.

Do not introduce:

- a generic capability registry;
- reflection-driven dispatch;
- generic `InvokeCapability`;
- a dynamic command dictionary;
- MCP SDK dependencies below `BimIt.Revit.Server`;
- Autodesk Revit API dependencies in `BimIt.Revit.Bridge` or `BimIt.Revit.Contracts`.

## Protocol-advertisement invariant

A host may advertise protocol version 13 only when the complete v12 prefix is present and functional and get-selection is present and functional.

Any incomplete or non-prefix combination must fall back to the highest actually guaranteed accepted prefix.

Registration metadata must publish v13 only after all v13-required services are functional and the listener is ready, consistent with ADR-0003 and LIFECYCLE-0001.

This specification does not change the production advertisement `[12,11,10,9,8,7,6,5,4,3,2,1]`.

## Host gating

The per-connection StreamJsonRpc adapter must enforce negotiated support independently of the typed client.

Rules:

```text
no handshake
  -> reject

v1..v12
  -> reject revit.get_selection

v13
  -> allow

unknown v14
  -> reject
```

Inherited methods keep their current gates and gain `13` only in the explicit support sets listed above.

The v12-only guarantees gain `13` by the same explicit membership:

```text
CAP-0001 document_id guarantee    {12,13}
active_view query with filters={} {12,13}
```

A negotiated v11 connection still omits CAP-0001 `document_id` and still rejects an empty filter object. A negotiated v12 connection still has no `revit.get_selection`.

The underlying Addin CAP-0010 service must not execute when protocol gating fails.

Raw JSON-RPC peers must not bypass this gate.

## Typed client

`IRevitBridgeClient` addition, when implemented:

```text
Task<GetSelectionResult> GetSelectionAsync(
    GetSelectionRequest request,
    TimeSpan timeout,
    CancellationToken cancellationToken)
```

Typed client behavior:

- require a successful handshake;
- require a positive local timeout;
- allow each inherited method only on the explicit set listed above;
- allow get-selection only on `{13}`;
- reject unsupported versions locally before RPC transmission;
- reject unknown future protocol versions, including v14, until a later specification lists them;
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

CAP-0010 exposes no agent-supplied timeout.

## Errors

Transport CAP-0010 capability errors unchanged:

```text
NO_ACTIVE_DOCUMENT
DOCUMENT_CONTEXT_CHANGED
INVALID_SELECTION
REVIT_EXECUTION_TIMEOUT
REVIT_EXECUTION_FAILED
```

An empty selection remains a successful structured result and is not converted into a Bridge exception.

Handshake and protocol failures remain existing Bridge errors.

Server routing errors stay in the Server. They are not Bridge results.

## Execution path

Intended path, after implementation:

```text
NamedPipeBridgeClient
  -> handshake negotiate v13
  -> revit.get_selection
  -> StreamJsonRpc host adapter
  -> IRevitGetSelectionService
  -> Addin CAP-0010 implementation
  -> EXEC-0001
  -> active-document guard
  -> UIDocument.Selection.GetElementIds()
  -> GetSelectionResult
```

The Bridge itself must not execute Revit API work.

The Named Pipe or background thread must not directly access the active document or the selection.

No Revit transaction may be created. `Selection.SetElementIds` and pick APIs stay out of this path.

## Contract dependency boundaries

`BimIt.Revit.Contracts` may contain only transport-neutral CAP-0010 request and result types.

It must not reference:

- Autodesk Revit API;
- StreamJsonRpc;
- Named Pipes;
- MCP SDK;
- Server routing types.

`BimIt.Revit.Bridge` may reference Contracts and StreamJsonRpc and must remain Revit-API independent.

`BimIt.Revit.Addin` owns `UIDocument.Selection.GetElementIds()` and element-ref resolution.

`BimIt.Revit.Server` does not gain `revit_get_selection` in this specification. [SERVER-0010](../server/SERVER-0010-stdio-get-selection-tool.md) defines that later tool. Server implementation waits until typed Bridge live validation of this RPC has passed.

## Handshake advertisement

When implemented, a full host advertises:

```text
SupportedVersions = [13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1]
CurrentVersion = 13
```

That block is the future v13 full-host advertisement. It is not the production advertisement.

Handshake itself remains cached metadata only. It must not inspect the Revit model, read the selection, or invoke EXEC-0001.

## Acceptance criteria

BRIDGE-0013 is acceptable as a specification when:

1. Protocol 13 adds `revit.get_selection` and preserves the complete v12 guarantees by the explicit sets above.
2. Capability support is explicit set membership. No numeric `version >= N` check is authorized.
3. Unknown v14 supports nothing.
4. A host may advertise `[13,12,11,10,9,8,7,6,5,4,3,2,1]` only when the complete v12 prefix is present and functional and get-selection is present and functional.
5. Get-selection without that prefix does not advertise v13.
6. The RPC uses transport-neutral `GetSelectionRequest` / `GetSelectionResult` with no request `instance_id`.
7. The typed service is `IRevitGetSelectionService.GetSelectionAsync`, and the typed client method is `GetSelectionAsync` with the existing timeout and cancellation arguments.
8. Host and typed-client gating allow get-selection only on `{13}` and reject it on v1..v12 and on unknown v14.
9. Inherited methods, the CAP-0001 `document_id` guarantee, and the active-view empty-filter guarantee include 13 only by explicit membership.
10. Timeouts, cancellation, and no-retry behavior reuse the existing capability model.
11. An empty selection remains a successful structured result.
12. The Bridge does not reshape CAP-0010 results.
13. There is no generic capability registry, no reflection dispatch, and no Revit API dependency in Contracts or Bridge.
14. This specification does not implement the RPC and does not change production protocol advertisement.
15. Later automated tests, when the RPC is implemented, cover advertisement, gating, inherited v13 eligibility, unknown v14 rejection, and capability-error survival. Addin coverage required by CAP-0010 stays in the Addin tests.

## Explicitly deferred

- implementation of protocol v13;
- `Selection.GetReferences` and linked or subelement selection;
- selection modification and Revit pick UI;
- generic capability registry;
- numeric protocol compatibility;
- Assistant `bimit_inspect_selection` and `bimit_explore_selected_mep`.

## References

- BRIDGE-0001 through BRIDGE-0012
- CAP-0010: `revit_get_selection`
- ADR-0002: Named Pipes + JSON-RPC local bridge
- ADR-0005: agent context and token efficiency
- ADR-0006: document and element reference identity
- EXEC-0001: serialized Revit execution dispatcher
