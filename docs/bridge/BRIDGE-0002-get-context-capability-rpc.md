# BRIDGE-0002: `revit.get_context` capability RPC

- Status: Accepted
- Date: 2026-09-09
- Introduced bridge protocol version: 2

## Purpose

Define the first Revit capability RPC carried by the local RevitMCP bridge.

This specification implements the Addin/Bridge portion of CAP-0001 `revit_get_context` and validates the full execution path from a Named Pipe request through EXEC-0001 into a valid Autodesk Revit API context and back to a structured result.

It does not define the MCP-facing tool or MCP instance-routing behavior. Those remain `RevitMCP.Server` concerns.

## Related accepted decisions

This specification applies:

- ADR-0001: out-of-process MCP server and in-process Revit capability host;
- ADR-0002: Named Pipes + JSON-RPC local bridge;
- ADR-0003: Revit instance registration/discovery/addressing;
- ADR-0005: agent context and token efficiency;
- BRIDGE-0001: handshake and protocol-version negotiation;
- EXEC-0001: serialized Revit execution dispatcher;
- CAP-0001: `revit_get_context`.

## Bridge protocol version

`revit.get_context` is introduced in **bridge protocol version 2**.

BRIDGE-0001 requires a protocol-version increment when a newer peer requires an RPC method or guarantee that an older add-in does not provide under the same protocol version.

Therefore:

```text
protocol 1
  bridge.handshake

protocol 2
  bridge.handshake
  revit.get_context
```

The first implementation supporting CAP-0001 should advertise both versions:

```text
[2, 1]
```

This preserves handshake compatibility with version-1 peers while allowing a current server/client to require version 2 before invoking `revit.get_context`.

A peer must not call `revit.get_context` after negotiating protocol version 1.

Adding protocol version 2 does not change BRIDGE-0001 handshake request/result semantics.

## RPC method

The JSON-RPC method is:

```text
revit.get_context
```

This is intentionally an explicit capability RPC rather than a generic `capability.invoke` method.

The initial bridge should remain easy to inspect, test, version, and reason about. A generic capability envelope may be introduced later only if multiple concrete capabilities demonstrate a real need.

## Transport-neutral request

Conceptual contract:

```text
GetContextRequest
{
}
```

The request contains no fields in the initial version.

In particular it must not contain:

- `instance_id`;
- MCP metadata;
- LLM/client metadata;
- document identity;
- correlation with a specific AI provider.

`instance_id` is resolved before capability execution. The selected Named Pipe connection already identifies the target Revit process.

The request type belongs in `RevitMCP.Contracts` and must not reference StreamJsonRpc, Named Pipes, MCP SDK types, or Autodesk Revit API types.

## Result contract

The result implements CAP-0001 exactly and belongs in `RevitMCP.Contracts`.

Conceptually:

```text
GetContextResult
{
    instance
    document?
    active_view?
    selection
}
```

### Instance

Required:

```text
instance_id: string
revit_version: string
revit_build: string
```

The values should come from the authoritative process-lifetime metadata already owned by the Addin lifecycle/bridge.

There is no need to re-query static instance metadata from the Revit model merely to build this portion of the result.

### Document

When an active document exists:

```text
title: string
kind: "project" | "family"
is_workshared: boolean
is_model_in_cloud: boolean
is_read_only: boolean
is_modified: boolean
```

When no active document exists:

```text
document: null
```

No document path, central path, username, cloud project ID, cloud region, or other environment metadata is returned.

### Active view

When an active document and active view exist:

```text
element_id: string
name: string
view_type: string
```

Otherwise:

```text
active_view: null
```

`element_id` is a string and must be treated as opaque by agent-facing code.

The Addin should use the supported Revit API identifier value and serialize it losslessly using invariant formatting. Version-specific API differences, if any, belong at the existing compatibility boundary rather than in bridge contracts.

### Selection

Always present:

```text
count: integer >= 0
```

Only the count is returned. Selected element identifiers and selected element data are not part of this capability.

### Example

```json
{
  "instance": {
    "instance_id": "a81e3fd0-6d57-4d27-8dbc-b2f9e98221ac",
    "revit_version": "2026",
    "revit_build": "26.5.0.55"
  },
  "document": {
    "title": "Hospital-MEP",
    "kind": "project",
    "is_workshared": true,
    "is_model_in_cloud": true,
    "is_read_only": false,
    "is_modified": true
  },
  "active_view": {
    "element_id": "184392",
    "name": "Level 02 - HVAC",
    "view_type": "FloorPlan"
  },
  "selection": {
    "count": 12
  }
}
```

## Zero-document behavior

An open Revit process with no active document is a successful capability state.

The result is:

```json
{
  "instance": {
    "instance_id": "a81e3fd0-6d57-4d27-8dbc-b2f9e98221ac",
    "revit_version": "2026",
    "revit_build": "26.5.0.55"
  },
  "document": null,
  "active_view": null,
  "selection": {
    "count": 0
  }
}
```

No-active-document must not be converted into `REVIT_EXECUTION_FAILED`.

## Bridge service composition

The handshake and capability responsibilities remain separate.

Conceptually:

```text
NamedPipeBridgeHost
    |
    +-- BridgeHandshakeService
    |     cached metadata only
    |     no Revit API / no ExternalEvent
    |
    +-- IRevitCapabilityService
          implemented by RevitMCP.Addin
          uses EXEC-0001
```

`BridgeHandshakeService` remains owned by `RevitMCP.Bridge`.

The Bridge project may define a small RevitMCP-owned capability interface conceptually equivalent to:

```text
IRevitCapabilityService
  GetContextAsync(GetContextRequest, CancellationToken)
```

The interface contains transport-neutral RevitMCP contract types only. It must not expose Autodesk Revit API types or MCP types.

The Addin supplies the implementation because only the Addin owns the valid Revit API execution context.

The bridge host/adapters compose the handshake service and capability service for JSON-RPC exposure.

Do not introduce a DI container, generic service framework, reflection-driven capability registry, or general command bus for this first capability.

## Protocol-advertisement invariant

A host must not advertise bridge protocol version 2 unless a functional `revit.get_context` capability service is available.

Conversely, a version-1-only handshake host may remain usable without CAP-0001 support.

This prevents registration/handshake metadata from claiming a capability guarantee that the endpoint cannot fulfill.

## Revit execution path

The capability requires valid Revit API context.

Required path:

```text
Named Pipe background thread
    -> JSON-RPC revit.get_context
    -> Addin capability service
    -> RevitExecutionDispatcher.EnqueueAsync
    -> ExternalEvent.Raise
    -> ExternalEvent.Execute
    -> UIApplication
    -> collect dynamic context
    -> structured result
    -> JSON-RPC response
```

The Named Pipe/background thread must not directly read:

- active document;
- active view;
- selection;
- document state.

The capability must not create a Revit transaction.

## Revit data collection

Inside the EXEC-0001 operation, the implementation should obtain only the fields required by CAP-0001.

Conceptually this includes:

- `UIApplication.ActiveUIDocument`;
- active `Document` metadata required by the contract;
- active view metadata required by the contract;
- current selection count.

It must not enumerate model elements, parameters, linked models, warnings, documents, views, or selection contents beyond what is necessary to produce the bounded result.

Static process-lifetime instance metadata may be composed with the dynamic Revit-context result outside the Revit operation, provided no Revit API object escapes the valid execution context.

No Autodesk Revit API object may be returned through `RevitMCP.Contracts`.

## Cancellation and timeout semantics

The Addin service passes request cancellation into EXEC-0001.

EXEC-0001 semantics remain authoritative:

- cancellation before queued work starts prevents that work from executing;
- once Revit execution has started, caller cancellation/timeout may stop the caller waiting but must not forcibly abort the active Revit API operation.

No universal hard-coded capability timeout is introduced in the Addin.

A bridge client operation should accept an explicit caller-provided timeout/deadline or equivalent bounded wait rather than reusing the bootstrap handshake timeout implicitly.

A connected-but-silent peer must not cause a capability caller to wait indefinitely. The local wait technique established for BRIDGE-0001 may be reused where appropriate, but capability timeout maps to `REVIT_EXECUTION_TIMEOUT`, not `BRIDGE_HANDSHAKE_TIMEOUT`.

The later MCP Server adapter owns user/tool-level timeout policy.

## Error model

The initial capability-specific error codes are:

```text
REVIT_EXECUTION_TIMEOUT
REVIT_EXECUTION_FAILED
```

### `REVIT_EXECUTION_TIMEOUT`

The capability did not complete within the caller's bounded execution wait.

A timeout after Revit execution begins does not imply that the Revit operation was aborted.

### `REVIT_EXECUTION_FAILED`

The request reached the capability path but execution could not be completed, including deterministic dispatcher scheduling/stopped failures or a Revit-side exception that cannot be represented as a successful context result.

Errors carried over JSON-RPC must retain at least:

```text
code: string
message: string
```

The implementation may reuse the existing structured bridge error envelope if it remains transport-neutral and preserves the capability code without misclassifying it as a handshake failure.

Stack traces, local paths, usernames, project identifiers, and verbose exception chains must not be returned to agents as normal error data.

## Client behavior

The bridge client surface should expose a typed `GetContextAsync` operation using `GetContextRequest` / `GetContextResult`.

The call is valid only after a successful handshake that selected bridge protocol version 2 or later with a version that guarantees this method.

The implementation should make accidental use under selected protocol version 1 difficult or deterministically rejected.

The bridge client remains independent of MCP SDK types.

## Agent-context and token-efficiency requirements

ADR-0005 applies directly.

For CAP-0001 specifically:

- the result is constant-size with respect to Revit model element count;
- only one active document and one active view are represented;
- selection is a count, not an element list;
- no file paths or cloud/project identifiers are returned;
- no generic property bags are returned;
- no diagnostic stack traces are returned;
- the bridge returns structured data only, with no duplicate human-readable summary;
- fields not defined by CAP-0001 must not be added opportunistically.

This capability is deliberately small enough to be called routinely by an agent without materially consuming its context window.

## Security and data minimization

This is a read-only capability.

It must not:

- modify the model;
- start a transaction;
- expose local/central paths;
- expose usernames;
- expose cloud project IDs;
- expose selected element identities;
- expose arbitrary environment metadata.

Named Pipe user/session isolation and registration/handshake validation remain required before capability traffic.

## Automated validation requirements

The implementation should cover, where practical without launching Revit:

1. `GetContextRequest` is empty and transport-neutral;
2. result serialization follows the CAP-0001 field names and nullability;
3. zero-document state produces null document/view and selection count 0;
4. project/family kind mapping is deterministic;
5. element IDs serialize as strings;
6. selected element IDs are not present in the result contract;
7. file paths/user/cloud IDs are not present in the result contract;
8. capability service dispatches through EXEC-0001 rather than reading Revit API on a bridge thread;
9. no transaction is created;
10. dispatcher scheduling/failure maps deterministically to `REVIT_EXECUTION_FAILED`;
11. bounded client timeout maps to `REVIT_EXECUTION_TIMEOUT`;
12. handshake-only protocol version 1 behavior remains functional;
13. protocol version 2 is selected when both peers support `[2,1]`;
14. version 1 is selected when the remote peer supports only version 1;
15. `revit.get_context` is not treated as available under negotiated version 1;
16. existing BRIDGE-0001 discovery/handshake tests remain green;
17. existing EXEC-0001 tests remain green;
18. Revit 2025/2026/2027 Addin variants compile.

## Real-Revit validation requirements

Before the Addin/Bridge slice of CAP-0001 is considered validated, demonstrate in real Revit:

1. handshake negotiates protocol version 2;
2. a live `revit.get_context` request causes `ExternalEvent.Raise` / `ExternalEvent.Execute` execution without Revit API context errors;
3. Revit Home / zero-document state returns the defined successful null-document result;
4. with an active project document, required document/view/selection fields are returned;
5. selection count changes when the Revit selection changes, without returning selected IDs;
6. no transaction is created;
7. no model/path/user/cloud identifiers beyond CAP-0001 are exposed;
8. normal Revit shutdown remains clean and withdraws registration/bridge readiness.

Initial live validation may be performed on Revit 2026.5 first. Live Revit 2025/2027 validation remains a separate compatibility milestone unless explicitly included.

## Non-goals

BRIDGE-0002 does not define:

- MCP tool registration;
- MCP `structuredContent` formatting;
- zero/one/many instance selection in the MCP server;
- element query/filter capabilities;
- selected element details;
- document identity/addressing;
- writes or transactions;
- generic capability discovery;
- a generic capability invocation envelope;
- progress notifications;
- batching infrastructure;
- remote/cloud bridge exposure;
- UI behavior.

## Acceptance criteria

BRIDGE-0002 is correctly implemented when:

1. bridge protocol version 2 is introduced while version 1 remains supported;
2. `revit.get_context` is guaranteed only under negotiated protocol version 2+;
3. transport-neutral request/result contracts implement CAP-0001 exactly;
4. handshake remains Bridge-owned and bypasses Revit execution;
5. the Addin supplies a small capability service to the Bridge;
6. dynamic Revit context is collected only through EXEC-0001 in valid Revit API context;
7. no Revit transaction is created;
8. zero-document state succeeds;
9. result size is bounded independently of model size;
10. no selected IDs, file paths, usernames, cloud project IDs, or unrelated metadata are returned;
11. capability timeout/failure errors are deterministic;
12. existing handshake/discovery behavior remains backward compatible for protocol version 1;
13. automated tests and cross-version builds pass;
14. live Revit validation proves the first real ExternalEvent-backed bridge capability before the MCP Server slice is implemented.
