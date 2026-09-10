# BRIDGE-0003: `revit.query_elements` capability RPC

- Status: Accepted
- Date: 2026-09-10
- Introduced bridge protocol version: 3

## Purpose

Define the local Bridge RPC that implements CAP-0002 `revit_query_elements` and introduce bridge protocol version 3.

This specification keeps the explicit typed RPC approach established by BRIDGE-0002. It does not introduce a generic capability registry or command envelope.

## Related accepted decisions

- ADR-0001: out-of-process MCP server and in-process Revit capability host
- ADR-0002: Named Pipes + JSON-RPC local bridge
- ADR-0003: Revit instance registration/discovery/addressing
- ADR-0005: agent context and token efficiency
- ADR-0006: document and element reference identity
- BRIDGE-0001: handshake and protocol negotiation
- BRIDGE-0002: `revit.get_context`
- EXEC-0001: serialized Revit execution dispatcher
- CAP-0002: `revit_query_elements`

## Bridge protocol version 3

Protocol version 3 adds `revit.query_elements` and explicitly preserves the version-2 `revit.get_context` guarantee.

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
```

A fully capable current host should advertise:

```text
[3, 2, 1]
```

A host that implements only CAP-0001 continues to advertise:

```text
[2, 1]
```

A handshake-only host continues to advertise:

```text
[1]
```

## Explicit capability-version rule

Protocol integers must not be interpreted as an automatic `>=` compatibility rule.

For the accepted versions defined today:

```text
revit.get_context      guaranteed by {2, 3}
revit.query_elements   guaranteed by {3}
```

A future protocol version 4 or higher must be explicitly documented as preserving or changing these guarantees before Server code treats it as compatible.

This corrects the ambiguity that would otherwise appear once the Server begins negotiating version 3: a version-3 connection must remain valid for CAP-0001 even though CAP-0001 was introduced in version 2.

## RPC method

The JSON-RPC method is:

```text
revit.query_elements
```

A peer must not call this method unless bridge protocol version 3 was explicitly negotiated.

## Transport-neutral request

Conceptually:

```text
QueryElementsRequest
{
    document_id?: string
    scope: "document" | "active_view"
    filters
    limit: integer
}
```

The exact filter semantics and bounds are owned by CAP-0002.

The request must not contain:

- `instance_id`;
- MCP metadata;
- model-provider metadata;
- pipe/process/session identifiers;
- file paths;
- an agent-supplied execution timeout.

`instance_id` is resolved by the Server before opening the selected Named Pipe. `document_id` remains in the capability request because it is an active-document context guard that must be verified inside the Revit process.

Request/result contract types belong in `RevitMCP.Contracts` and must not reference StreamJsonRpc, Named Pipes, MCP SDK types, or Autodesk Revit API types.

## Result contract

Conceptually:

```text
QueryElementsResult
{
    context
      instance_id
      document_id
    matched_count
    truncated
    element_refs[]
}
```

The result must conform to CAP-0002 exactly.

`element_ref` is opaque at the Bridge/Contracts boundary. The Addin's initial implementation derives it from Revit `Element.UniqueId`; Bridge code must not parse or reinterpret it.

## Document identity ownership

The Addin owns the process-lifetime mapping from live Revit `Document` objects to opaque `document_id` values required by ADR-0006.

The mapping must be consulted/created inside valid Revit execution context. Bridge background threads must not inspect Revit `Document` objects directly.

Required behavior:

- omitted `document_id`: the active document is assigned/resolved an id and queried;
- explicit matching `document_id`: query proceeds;
- explicit non-matching `document_id`: `DOCUMENT_CONTEXT_CHANGED`;
- no active document: `NO_ACTIVE_DOCUMENT`.

A context mismatch must not silently fall back to another document.

## Bridge service composition

The existing explicit capability composition remains.

Conceptually:

```text
NamedPipeBridgeHost
    |
    +-- BridgeHandshakeService
    |     cached process metadata only
    |
    +-- Revit capability service(s)
          GetContextAsync(...)
          QueryElementsAsync(...)
          implemented by RevitMCP.Addin
          use EXEC-0001
```

The implementation may extend the existing RevitMCP-owned capability interface or introduce one small explicit interface for query-elements if that produces cleaner dependency boundaries.

Do not introduce:

- reflection-driven capability registration;
- a generic `InvokeCapability` RPC;
- a general command bus;
- MCP SDK dependencies below `RevitMCP.Server`;
- Autodesk Revit API dependencies in `RevitMCP.Bridge` or `RevitMCP.Contracts`.

If implementation evidence shows the existing interface shape cannot evolve cleanly, stop and report the architectural conflict rather than silently introducing a new framework.

## Protocol-advertisement invariant

A host may advertise protocol version 3 only when both guarantees below are functional:

```text
revit.get_context
revit.query_elements
```

This means version 3 inherits the accepted version-2 context capability rather than replacing it.

A host with CAP-0001 but without CAP-0002 must not advertise version 3.

Registration metadata must publish the actual highest supported bridge protocol only after the listener and required capability services are ready, consistent with ADR-0003/LIFECYCLE-0001.

## Revit execution path

Required path:

```text
Named Pipe background thread
    -> JSON-RPC revit.query_elements
    -> Addin capability service
    -> RevitExecutionDispatcher.EnqueueAsync
    -> ExternalEvent.Raise
    -> ExternalEvent.Execute
    -> UIApplication / active Document
    -> validate document_id
    -> collect/filter/count/project element refs
    -> structured QueryElementsResult
    -> JSON-RPC response
```

The Named Pipe/background thread must not directly access active document, active view, elements, element types, levels, or `Element.UniqueId`.

No Revit transaction may be created.

## Query behavior at the Bridge boundary

Bridge does not implement query semantics. It transports the CAP-0002 request/result and stable error model.

The Addin capability service is responsible for deterministic Revit-side execution of:

- active-document identity;
- scope resolution;
- eligible-element collection;
- category/family/type/level/text filtering;
- exact match counting;
- `element_ref` derivation;
- deterministic ordering and limiting.

No Revit API object may be returned through the Bridge.

## Cancellation and timeout semantics

Existing EXEC-0001 and BRIDGE-0002 semantics remain authoritative:

- caller cancellation before queued work starts prevents execution;
- once Revit API execution starts, cancellation/timeout must not forcibly abort the Revit thread;
- Bridge client capability waits are locally bounded;
- a local timeout maps to `REVIT_EXECUTION_TIMEOUT` and cancels the in-flight RPC so still-queued work can be skipped;
- timeout values are not supplied by the MCP tool input.

CAP-0002 does not introduce retries.

## Errors

Transport/capability errors remain compact and deterministic.

Capability-level codes passed through the Bridge include:

```text
NO_ACTIVE_DOCUMENT
DOCUMENT_CONTEXT_CHANGED
NO_ACTIVE_VIEW
INVALID_QUERY
REVIT_EXECUTION_TIMEOUT
REVIT_EXECUTION_FAILED
```

Handshake/protocol errors remain the existing BRIDGE-* errors at the Bridge layer. The Server maps them to its agent-facing instance errors as specified by SERVER-0002.

Unknown category/family/type/level names are normal query inputs and may return zero matches; they are not Bridge failures.

## Context-size requirements

Although Named Pipe transport is local, Bridge payloads should preserve the same bounded CAP-0002 shape returned to agents.

Do not add internal diagnostics, Revit objects, expanded element metadata, paths, or duplicate per-element values to the normal result merely because the transport is local.

## Acceptance criteria

1. protocol version 3 is explicitly introduced;
2. v3 guarantees handshake + get-context + query-elements;
3. get-context capability support is represented explicitly as `{2,3}`, not `>=2`;
4. query-elements support is represented explicitly as `{3}`;
5. a CAP-0001-only host still negotiates version 2;
6. a CAP-0002-capable host can advertise `[3,2,1]`;
7. `revit.query_elements` cannot be invoked after negotiating v1 or v2;
8. request/result contracts remain transport-neutral;
9. `document_id` is validated in Revit execution context;
10. all query Revit API work uses EXEC-0001;
11. no transaction is created;
12. existing cancellation/timeout semantics are preserved;
13. automated tests cover version negotiation, advertisement invariants, method gating, context mismatch, success, timeout, and cancellation;
14. live Revit validation proves a real typed Named Pipe query path before CAP-0002 is considered implemented.

## Explicitly deferred

- generic capability registry/invocation;
- capability discovery over the local Bridge;
- inactive-document targeting;
- linked-document queries;
- parameter/spatial query expansion;
- write capability protocol versions.

## References

- ADR-0006: Document and element reference identity
- CAP-0002: `revit_query_elements`
- BRIDGE-0001: handshake and version negotiation
- BRIDGE-0002: `revit.get_context`
- EXEC-0001: serialized Revit execution dispatcher
