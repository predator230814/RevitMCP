# BRIDGE-0008: `revit.preview_parameter_updates` capability RPC

- Status: Proposed
- Date: 2026-09-28
- Introduced bridge protocol version: 8

## Purpose

Define the local typed JSON-RPC Bridge operation that implements accepted CAP-0007 `revit_preview_parameter_updates` and introduce bridge protocol version 8.

The JSON-RPC method is:

```text
revit.preview_parameter_updates
```

The transport-neutral types are named here only. This specification does not add them:

```text
PreviewParameterUpdatesRequest
PreviewParameterUpdatesResult
```

This specification preserves the explicit typed RPC approach established by BRIDGE-0002 through BRIDGE-0007. It does not implement Contracts, Bridge code, Addin code, tests, Server registration, or MCP schemas. It does not introduce a generic capability registry, command envelope, reflection-driven dispatch, or dynamic tool dictionary. It does not place the MCP SDK below `RevitMCP.Server`.

Current runtime protocol remains v7. Current code still treats protocol v8 as unsupported. This document does not change that.

## Related accepted decisions

- ADR-0001: out-of-process MCP server and in-process Revit capability host
- ADR-0002: Named Pipes + JSON-RPC local bridge
- ADR-0003: Revit instance registration/discovery/addressing
- ADR-0006: document and element reference identity
- ADR-0008: controlled write safety model
- BRIDGE-0001: handshake and protocol negotiation
- BRIDGE-0002 through BRIDGE-0007: inherited capability RPCs
- EXEC-0001: serialized Revit execution dispatcher
- LIFECYCLE-0002: ephemeral write-intent store
- CAP-0007: `revit_preview_parameter_updates`

## Bridge protocol version 8

Protocol version 8 adds exactly `revit.preview_parameter_updates` and explicitly preserves the complete version-7 capability prefix.

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

protocol 8
  bridge.handshake
  revit.get_context
  revit.query_elements
  revit.get_elements
  revit.describe_parameters
  revit.get_parameter_values
  revit.get_mep_topology
  revit.preview_parameter_updates
```

A complete future v8 host advertises:

```text
[8, 7, 6, 5, 4, 3, 2, 1]
```

A host with the existing complete v7 implementation and no CAP-0007 preview continues to advertise:

```text
[7, 6, 5, 4, 3, 2, 1]
```

Lower prefixes remain unchanged:

```text
[6, 5, 4, 3, 2, 1]
[5, 4, 3, 2, 1]
[4, 3, 2, 1]
[3, 2, 1]
[2, 1]
[1]
```

Any incomplete composition falls back to the highest complete accepted prefix. The presence of a preview service without the complete inherited v7 surface must not advertise v8.

This specification does not claim that protocol v8 is implemented.

## Explicit capability-version rule

Protocol integers are not an automatic numeric compatibility ladder.

Accepted support sets after a future BRIDGE-0008 implementation:

```text
revit.get_context                  {2,3,4,5,6,7,8}
revit.query_elements               {3,4,5,6,7,8}
revit.get_elements                 {4,5,6,7,8}
revit.describe_parameters          {5,6,7,8}
revit.get_parameter_values         {6,7,8}
revit.get_mep_topology             {7,8}
revit.preview_parameter_updates    {8}
```

Do not implement capability checks as:

```text
version >= 2
version >= 3
version >= 4
version >= 5
version >= 6
version >= 7
version >= 8
```

Unknown protocol v9 must not implicitly support any capability, including preview and every inherited method. A later version must explicitly document which guarantees it preserves before Server or Bridge code treats it as compatible.

## RPC method

The JSON-RPC method is:

```text
revit.preview_parameter_updates
```

Request and result types are the CAP-0007 transport-neutral contracts:

```text
PreviewParameterUpdatesRequest
PreviewParameterUpdatesResult
```

Those types do not exist in the current repository. This specification names them; it does not add them.

## Transport-neutral request

The Bridge request is exactly the CAP-0007 request:

```text
PreviewParameterUpdatesRequest
{
  document_id
  updates[1..20]
    element_ref
    parameter_ref
    value
}
```

The Bridge must not add:

- `instance_id`;
- process, session, or pipe identifiers;
- MCP metadata;
- LLM or provider identity;
- approval information;
- confirmation fields;
- timeout;
- TTL override;
- display names supplied by the caller.

`instance_id` remains Server routing-only. It is resolved before the Server opens the selected Named Pipe. It does not enter the Bridge request.

`document_id`, `element_ref`, and `parameter_ref` pass unchanged as opaque strings. The Bridge does not trim, parse, normalize, manufacture, or reinterpret them.

CAP-0007 owns validation and value grammar, including the closed `value` variants, the 1..20 bound, ordinal pair uniqueness, and `INVALID_PARAMETER_UPDATE_PREVIEW`. BRIDGE-0008 must not duplicate or redefine those rules.

`document_id` remains in the capability request because it is a correctness guard that must be verified against the active Revit document inside EXEC-0001.

Request and result types belong in `RevitMCP.Contracts`. They must not reference StreamJsonRpc, Named Pipes, MCP SDK types, or Autodesk Revit API types.

## Result contract

Preserve the CAP-0007 result exactly:

```text
PreviewParameterUpdatesResult
{
  context
    instance_id
    document_id
  ready
  items[]
  intent_ref?           // ready=true only
  intent_fingerprint?   // ready=true only
  expires_at?           // ready=true only
}
```

Item statuses and the `ok` / `no_change` shapes remain CAP-0007 business semantics. Return one item per requested update, in request order. CAP-0007 / Addin shaping is authoritative.

The Bridge must not:

- recreate the fingerprint;
- inspect or parse `intent_ref`;
- reorder items;
- rewrite `before` or `proposed` values;
- add approval state;
- convert item misses into Bridge exceptions.

`ready=false` is a successful typed result. Expected item-level misses stay successful structured result statuses. Omit `intent_ref`, `intent_fingerprint`, and `expires_at` unless `ready=true`.

`INTENT_CAPACITY_REACHED` remains a capability error. It is not a `ready=false` result.

## CAP-0007 and store ownership

CAP-0007 owns the business contract: document gates, parameter resolution, write eligibility, item evaluation order, before-state, no-change, and intent creation.

The future Addin service owns Revit execution. It uses the single LIFECYCLE-0002 store already owned by `RevitExecutionDispatcherLifetime`. This Bridge specification does not redefine store ownership, capacity, fingerprint schema, document-close cleanup, or shutdown cleanup.

ADR-0006 remains authoritative for `document_id` and `element_ref`. ADR-0008 remains authoritative for later approval and apply behavior.

## Bridge service composition

Explicit typed service composition remains the rule.

Introduce a future Bridge service interface conceptually:

```text
IRevitPreviewParameterUpdatesService
  PreviewParameterUpdatesAsync(
      PreviewParameterUpdatesRequest request,
      CancellationToken cancellationToken)
```

The host remains explicitly composed.

Conceptual v8 host:

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
    |     GetMepTopologyAsync(...)
    |
    +-- IRevitPreviewParameterUpdatesService
          PreviewParameterUpdatesAsync(...)
          implemented by RevitMCP.Addin
          uses EXEC-0001 and the lifetime-owned LIFECYCLE-0002 store
```

Do not introduce:

- generic command envelopes;
- a generic capability registry;
- reflection-driven dispatch;
- generic `InvokeCapability`;
- dynamic tool dictionaries;
- MCP SDK dependencies below `RevitMCP.Server`;
- Autodesk Revit API dependencies in `RevitMCP.Bridge` or `RevitMCP.Contracts`.

## Protocol-advertisement invariant

A host may advertise protocol version 8 only when the complete inherited v7 prefix plus preview is functional:

```text
get-context
+ query-elements
+ get-elements
+ describe-parameters
+ get-parameter-values
+ get-mep-topology
+ preview-parameter-updates
```

Examples:

```text
complete v7 surface without preview                                                         -> highest 7
complete v7 surface + preview                                                              -> highest 8
preview only                                                                               -> handshake-only v1
preview without the complete inherited v7 surface                                          -> highest complete accepted prefix, never v8
```

Any incomplete or non-prefix combination falls back to the highest actually guaranteed accepted prefix.

Registration metadata must publish v8 only after all v8-required services are functional and the listener is ready, consistent with ADR-0003 and LIFECYCLE-0001. Until that implementation exists, registration continues to describe the current v7 host.

## Host gating

The per-connection StreamJsonRpc adapter must enforce negotiated support independently of the typed client. Raw StreamJsonRpc callers must not bypass this gate.

Rules for `revit.preview_parameter_updates`:

```text
no handshake
  -> reject

v1..v7
  -> reject

v8
  -> allow

unknown v9
  -> reject
```

The underlying Addin preview service must not execute when protocol gating fails.

Older inherited methods explicitly add v8 to their support sets. Their existing gates remain in force:

```text
revit.get_context               {2,3,4,5,6,7,8}
revit.query_elements            {3,4,5,6,7,8}
revit.get_elements              {4,5,6,7,8}
revit.describe_parameters       {5,6,7,8}
revit.get_parameter_values      {6,7,8}
revit.get_mep_topology          {7,8}
```

Unknown v9 rejects every capability, including the inherited methods.

## Typed client

Future `IRevitBridgeClient` method:

```text
Task<PreviewParameterUpdatesResult> PreviewParameterUpdatesAsync(
    PreviewParameterUpdatesRequest request,
    TimeSpan timeout,
    CancellationToken cancellationToken)
```

Typed client behavior:

- require a completed successful handshake;
- require a positive local timeout;
- require the negotiated version to be exactly within the explicit preview support set `{8}`;
- reject locally before RPC transmission when preview is unsupported, including v1..v7 and unknown v9;
- no automatic retry;
- preserve the existing unusable-after-local-capability-timeout behavior.

Inherited typed-client gates add v8 to their explicit sets and still reject unknown v9. They do not use numeric `>=`.

## Timeout and orphan-intent semantics

This section preserves CAP-0007 and EXEC-0001. It does not fix the race.

Current Bridge behavior is a bounded local wait around the RPC. A positive typed-client capability timeout maps to `REVIT_EXECUTION_TIMEOUT`. The in-flight RPC cancellation is requested. There is no automatic retry.

State the achievable invariant explicitly:

- timeout or cancellation before the EXEC-0001 item starts can prevent preview execution and intent creation;
- after EXEC-0001 work has begun, cancellation must not forcibly abort the Revit work;
- a caller may time out or cancel while that preview subsequently completes and stores a valid intent;
- therefore `REVIT_EXECUTION_TIMEOUT` is not proof that no intent exists;
- the Bridge cannot know whether response delivery occurred;
- no acknowledgement, claim, lookup, listing, recovery, or retry protocol is introduced;
- an unreported intent expires through normal LIFECYCLE-0002 rules: the 10-minute monotonic lifetime, successful document close, and process shutdown;
- possession of `intent_ref` is not authorization or approval;
- a timed-out Bridge connection remains unusable under the current connection policy.

Store an intent only after the full preview is known and every item is `ok`. Do not store a partial intent. Do not store an intent when the preview itself fails. Those rules belong to CAP-0007 and the Addin. The Bridge does not invent a second store decision.

## Errors

Preserve CAP-0007 capability errors unchanged:

```text
NO_ACTIVE_DOCUMENT
DOCUMENT_CONTEXT_CHANGED
UNSUPPORTED_DOCUMENT_KIND
DOCUMENT_NOT_WRITABLE
INVALID_PARAMETER_UPDATE_PREVIEW
INTENT_CAPACITY_REACHED
REVIT_EXECUTION_TIMEOUT
REVIT_EXECUTION_FAILED
```

Existing Bridge handshake and protocol errors remain unchanged.

Expected item-level misses remain successful structured result statuses. They are not converted into Bridge exceptions.

Server routing errors, including `NO_REVIT_INSTANCE`, `INSTANCE_REQUIRED`, `INSTANCE_NOT_FOUND`, `INSTANCE_UNAVAILABLE`, and future Server `INVALID_REQUEST`, are outside this local Bridge contract.

## Execution boundary

Intended future path:

```text
NamedPipeBridgeClient
  -> handshake v8
  -> revit.preview_parameter_updates
  -> host protocol gate
  -> IRevitPreviewParameterUpdatesService
  -> Addin
  -> EXEC-0001
  -> CAP-0007 validation/resolution
  -> LIFECYCLE-0002 intent store when every item is ok
  -> PreviewParameterUpdatesResult
```

Bridge and background threads perform no Revit API access. The Named Pipe thread must not touch the active document, elements, parameters, or the intent store.

No Revit transaction is created. Preview is not apply. This RPC does not call `Parameter.Set`, save, or synchronize.

## Security / approval boundary

Preview and intent creation do not grant authorization.

`intent_ref`, `intent_fingerprint`, client identity, request fields, JSON-RPC success, and protocol version are not approval.

No MRTR or approval-provider semantics belong in this RPC.

ADR-0008 remains authoritative for later approval and apply behavior. A later apply must revalidate immediately before mutation. BRIDGE-0008 does not define that apply path.

## Contract dependency boundaries

`RevitMCP.Contracts` may later contain only transport-neutral CAP-0007 request, result, item, and value types.

It must not reference:

- Autodesk Revit API;
- StreamJsonRpc;
- Named Pipes;
- MCP SDK;
- Server routing types.

`RevitMCP.Bridge` may reference Contracts and StreamJsonRpc but must remain Revit-API independent.

`RevitMCP.Addin` owns Revit resolution, EXEC-0001 dispatch, and use of the lifetime-owned intent store.

## Handshake advertisement

Current implemented runtime remains protocol v7:

```text
SupportedVersions = [7, 6, 5, 4, 3, 2, 1]
CurrentVersion = 7
```

Unknown v8 is still unsupported by current code.

After a future BRIDGE-0008 implementation, a full CAP-0001..CAP-0007 host advertises:

```text
SupportedVersions = [8, 7, 6, 5, 4, 3, 2, 1]
CurrentVersion = 8
```

Handshake itself remains cached metadata only. It must not inspect the Revit model, invoke EXEC-0001, or create an intent.

## Acceptance criteria

BRIDGE-0008 is acceptable as a Proposed specification when:

1. Protocol 8 adds exactly `revit.preview_parameter_updates` and preserves the v7 guarantees.
2. Explicit capability sets include v8 for inherited capabilities.
3. Preview is supported on `{8}` only.
4. Unknown v9 is unsupported.
5. Full v8 advertisement requires the complete inherited prefix plus the preview service.
6. Request and result remain the CAP-0007 transport-neutral contracts, with no `instance_id` on the request.
7. The Bridge does not interpret refs, values, the fingerprint, or preview ordering.
8. `ready=false` and item statuses remain successful typed results.
9. CAP-0007 capability errors survive unchanged.
10. Timeout semantics explicitly permit an orphan intent after caller timeout or cancel once EXEC work has started.
11. No automatic retry, recovery, claim, or list API is introduced.
12. No approval semantics are introduced.
13. No generic registry or reflection architecture is introduced.
14. Later implementation tests must cover protocol advertisement, typed-client gating, raw-host gating, inherited v8 capability eligibility, unknown-v9 rejection, timeout behavior, and capability-error survival.
15. This specification PR contains no production implementation.

## Explicitly excluded

- Contracts implementation;
- Addin CAP-0007 service;
- Bridge v8 implementation;
- Server specification or MCP tool registration;
- CAP-0008 and every apply path;
- approval provider;
- MRTR;
- MCP Apps and Revit product UI;
- Revit transactions;
- `Parameter.Set` or any equivalent mutation;
- save and synchronize;
- an MCP SDK or other package upgrade;
- an acknowledgement, claim, lookup, listing, or recovery protocol for the timeout race.

`tools/list` remains exactly the six accepted read tools.

## References

- BRIDGE-0001 through BRIDGE-0007
- CAP-0007: `revit_preview_parameter_updates`
- LIFECYCLE-0002: ephemeral write-intent store
- ADR-0008: controlled write safety model
- EXEC-0001: serialized Revit execution dispatcher
