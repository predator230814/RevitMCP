# BRIDGE-0001: Handshake and protocol-version negotiation

- Status: Accepted
- Date: 2026-09-08

## Purpose

Define the minimum bootstrap contract used by `RevitMCP.Server` to validate a discovered `RevitMCP.Addin` instance and select a compatible bridge protocol version before capability requests are sent.

This specification implements ADR-0002 and ADR-0003. It does not redefine the local IPC architecture.

## Scope

The handshake exists only to:

1. prove that the declared Named Pipe endpoint is responsive;
2. prove that the endpoint represents the Revit instance described by the discovery registration;
3. select a bridge protocol version understood by both peers;
4. return the minimum cached metadata needed to mark the instance ready for capability execution.

The handshake is intentionally not a Revit capability.

## Non-goals

The handshake must not:

- read the active document;
- read the active view;
- read the current selection;
- enumerate model elements;
- start a Revit transaction;
- require a Revit `ExternalEvent`;
- expose file paths, project identifiers, cloud metadata, or usernames;
- depend on MCP types or any LLM/client vendor;
- become a general-purpose status payload.

## RPC method

The bootstrap JSON-RPC method is conceptually:

```text
bridge.handshake
```

The exact StreamJsonRpc binding mechanism is an implementation detail, but the request and result semantics defined here are transport-neutral and belong in `RevitMCP.Contracts`.

## Request

Conceptual contract:

```text
BridgeHandshakeRequest
- expected_instance_id: string
- supported_protocol_versions: integer[]
- client_name: string?
- client_version: string?
```

Example:

```json
{
  "expected_instance_id": "a81e3fd0-6d57-4d27-8dbc-b2f9e98221ac",
  "supported_protocol_versions": [1],
  "client_name": "RevitMCP.Server",
  "client_version": "0.1.0"
}
```

### Request rules

- `expected_instance_id` is required and opaque to the caller.
- `supported_protocol_versions` is required and must contain at least one positive integer.
- Duplicate protocol versions should be ignored or rejected consistently by the implementation; they must not affect selection semantics.
- Client name/version are optional diagnostic metadata only and must not alter capability authorization or identity.
- The request contains no MCP-specific metadata.

## Result

Conceptual contract:

```text
BridgeHandshakeResult
- instance_id: string
- process_id: integer
- process_start_time_utc: timestamp
- windows_session_id: integer
- revit_version: string
- revit_build: string
- addin_version: string
- supported_protocol_versions: integer[]
- selected_protocol_version: integer
```

Example:

```json
{
  "instance_id": "a81e3fd0-6d57-4d27-8dbc-b2f9e98221ac",
  "process_id": 18432,
  "process_start_time_utc": "2026-09-08T20:42:15Z",
  "windows_session_id": 2,
  "revit_version": "2026",
  "revit_build": "26.5.0.0",
  "addin_version": "0.1.0",
  "supported_protocol_versions": [1],
  "selected_protocol_version": 1
}
```

## Cached metadata requirement

The handshake must be answerable without entering the Revit API execution queue.

Metadata needed by the result must therefore be captured or safely derived during add-in startup and stored in a form that can be returned by the bridge listener without invoking `ExternalEvent`.

This avoids a circular dependency in which bridge readiness depends on the execution queue that the bridge is supposed to validate before capability traffic begins.

## Protocol version model

Bridge protocol versions are positive integers.

Initial version:

```text
1
```

Product versions and bridge protocol versions are separate concepts:

```text
RevitMCP.Server product version  != bridge protocol version
RevitMCP.Addin product version   != bridge protocol version
```

A product release may continue to use the same bridge protocol version when the wire contract remains compatible.

## Version selection

Both peers expose the bridge protocol versions they support.

The selected version is the highest version present in the intersection of the two supported-version sets.

Example:

```text
Server: [3, 2]
Addin:  [2, 1]
Selected: 2
```

If no common version exists, the handshake fails with `BRIDGE_PROTOCOL_INCOMPATIBLE` and the instance must not become ready for capability execution.

## When to increment the protocol version

A new bridge protocol version is required when a change is not backward compatible, including when:

- an existing required field changes meaning or type;
- a required field is removed;
- method semantics change incompatibly;
- a newer server requires RPC methods or guarantees that an older add-in does not provide under the same protocol version;
- request/response framing expectations become incompatible.

Adding optional fields that older peers can safely ignore does not automatically require a new protocol version.

## Bootstrap stability

`bridge.handshake` is the stable bootstrap surface used to negotiate the rest of the bridge protocol.

Its evolution must therefore remain conservative and primarily additive. A peer must not need to know a negotiated protocol version in order to parse enough of the handshake to negotiate that version.

## Identity validation

A registration record is only a discovery candidate. The handshake provides the authoritative bridge identity check.

`RevitMCP.Server` must compare at least:

- registration `instance_id` with handshake `instance_id`;
- registration `process_id` with handshake `process_id`;
- registration `process_start_time_utc` with handshake `process_start_time_utc`;
- registration/session metadata with handshake session metadata where available and reliable.

If identity validation fails, the instance must not become ready.

The expected failure is:

```text
BRIDGE_IDENTITY_MISMATCH
```

The implementation may log additional diagnostic detail, but client-facing errors must not expose unnecessary local-sensitive metadata.

## Discovery state implications

The handshake completes the ADR-0003 discovery validation chain:

```text
registration record
    -> candidate
process validation
    -> candidate alive
pipe connection
    -> endpoint reachable
handshake identity validation
    -> correct instance
protocol negotiation
    -> compatible instance
READY
```

At minimum, internal discovery may distinguish:

- `Ready` — process valid, pipe reachable, identity matches, compatible protocol selected;
- `Unavailable` — process appears valid but bridge cannot currently be reached or handshake times out;
- `Incompatible` — instance identity is valid but no common bridge protocol version exists;
- `Stale` — process no longer exists or process start time no longer matches the registration.

These internal states do not require one-to-one MCP error mapping.

## Errors

The minimum handshake-specific error model is:

```text
BRIDGE_PROTOCOL_INCOMPATIBLE
BRIDGE_IDENTITY_MISMATCH
BRIDGE_HANDSHAKE_TIMEOUT
BRIDGE_HANDSHAKE_FAILED
```

### Semantics

`BRIDGE_PROTOCOL_INCOMPATIBLE`
: No common bridge protocol version exists.

`BRIDGE_IDENTITY_MISMATCH`
: The endpoint responded, but its authoritative identity does not match the discovery registration / expected instance.

`BRIDGE_HANDSHAKE_TIMEOUT`
: The endpoint did not complete the handshake within the configured bootstrap timeout.

`BRIDGE_HANDSHAKE_FAILED`
: The endpoint responded or connection succeeded, but the handshake could not be completed for another deterministic bridge-level reason.

Malformed registration and missing process errors remain discovery concerns rather than handshake errors.

## Security properties

- The handshake does not replace Named Pipe user/session isolation from ADR-0002.
- Registration metadata remains untrusted until identity validation succeeds.
- The handshake is not an authentication token and must not be treated as remote authorization.
- No remote/cloud client may call this local pipe directly across the network.
- Product/client version strings are diagnostics and must not be trusted for authorization decisions.

## Relation to CAP-0001

`CAP-0001 revit_get_context` may execute only after the target instance has passed discovery validation and a compatible bridge protocol version has been selected.

The sequence is:

```text
resolve instance
    -> connect pipe
    -> bridge.handshake
    -> Ready
    -> GetContextRequest {}
    -> Revit execution queue
    -> ExternalEvent
    -> UIApplication
```

Unlike the handshake, `GetContextRequest` requires valid Revit API execution context.

## Acceptance criteria

The first implementation must demonstrate that:

1. a valid registration and matching add-in endpoint complete the handshake and become `Ready`;
2. a mismatched `instance_id` is rejected;
3. a mismatched process identity is rejected;
4. a common protocol version is selected deterministically as the highest common version;
5. no common protocol version returns `BRIDGE_PROTOCOL_INCOMPATIBLE`;
6. handshake execution does not invoke the Revit execution queue or `ExternalEvent`;
7. handshake data contains no active-document, active-view, selection, file-path, username, or cloud-project metadata;
8. the contract can be unit-tested without Revit running by using an in-memory/fake bridge endpoint;
9. `RevitMCP.Contracts` definitions remain independent of StreamJsonRpc, Named Pipes, MCP, and Revit API types.

## Deferred concerns

This specification intentionally does not define:

- request scheduling or fairness;
- execution queue implementation;
- capability cancellation semantics;
- progress notifications;
- write-operation locking;
- bridge reconnect strategy beyond the minimum needed for discovery;
- remote/cloud protocol negotiation;
- capability discovery over the bridge;
- document identity.

Those concerns are introduced only when a concrete capability or operational requirement needs them.

## References

- ADR-0002: Named Pipes + JSON-RPC local bridge
- ADR-0003: Revit instance registration, discovery, and addressing
- CAP-0001: `revit_get_context`
