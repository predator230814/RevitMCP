# ADR-0002: Named Pipes and JSON-RPC for the local Revit bridge

- Status: Accepted
- Date: 2026-09-08

## Context

ADR-0001 established an out-of-process architecture in which `RevitMCP.Server` runs outside Revit and communicates with `RevitMCP.Addin`, which owns Revit API execution, transaction boundaries, and Revit-specific capability logic.

A local inter-process communication mechanism is required between these two components.

The bridge must support Revit 2025-2027, multiple simultaneous Revit processes, structured request/response contracts, cancellation and explicit errors, while keeping network exposure and dependency weight inside the Revit process as low as practical.

The bridge is an internal RevitMCP boundary. It is not an MCP transport and must not expose MCP-specific protocol types to the Revit capability layer.

## Decision drivers

- Local-only communication by default.
- Strong Windows-user isolation and minimal network attack surface.
- Low dependency and hosting overhead inside the Revit process.
- Deterministic structured request/response semantics.
- Support for notifications, correlation, cancellation, and explicit RPC errors.
- Straightforward support for multiple Revit instances.
- Good testability and diagnostics.
- Independence from future MCP transports, cloud gateways, Azure hosting, WebMCP, and MCP Apps.
- Ability to replace the RPC implementation later without rewriting Revit capabilities.

## Options considered

### Option A: Localhost HTTP with JSON

`RevitMCP.Addin` hosts an HTTP endpoint bound to loopback and `RevitMCP.Server` calls it using HTTP/JSON.

Advantages:

- familiar tooling and debugging;
- easy manual inspection with standard HTTP tools;
- broadly understood implementation model;
- naturally similar to future remote HTTP services.

Disadvantages:

- requires port allocation and collision handling;
- creates a TCP listener and additional local network surface;
- requires explicit protection against accidental non-loopback binding;
- local authentication/authorization must be designed even for same-machine use;
- introduces web-hosting concerns into the Revit process;
- network similarity to future cloud hosting does not justify coupling the local bridge to HTTP.

### Option B: Windows Named Pipes with a custom protocol

Use `NamedPipeServerStream` / `NamedPipeClientStream` and implement custom framing, correlation, serialization, cancellation, errors, and notifications.

Advantages:

- local IPC with no TCP port;
- Windows security integration;
- low transport overhead;
- lightweight inside Revit.

Disadvantages:

- custom protocol design would recreate solved RPC concerns;
- more bespoke code to test and maintain;
- increased risk around framing, timeouts, cancellation, and error correlation.

### Option C: gRPC over Named Pipes

Use gRPC semantics over a Named Pipe transport.

Advantages:

- strongly typed contracts;
- mature RPC semantics;
- streaming and cancellation support;
- efficient serialization.

Disadvantages:

- greater hosting/runtime/dependency weight than required for the initial Revit bridge;
- introduces Protobuf and gRPC concerns for a relatively simple internal request/response boundary;
- more infrastructure inside the Revit process than necessary for current needs.

### Option D: Windows Named Pipes with JSON-RPC 2.0

Use duplex Named Pipes for transport and JSON-RPC 2.0 for request/response, notifications, errors, correlation, and cancellation-related semantics. Use Microsoft's `StreamJsonRpc` as the initial .NET implementation.

Advantages:

- no TCP port or web server inside Revit;
- Windows-user security controls can be applied to the pipe;
- lightweight transport with mature RPC semantics;
- human-readable payloads improve diagnostics;
- `StreamJsonRpc` operates over .NET streams, including pipe streams;
- typed .NET interfaces can sit above the wire protocol;
- notifications and cancellation are available without inventing custom framing;
- implementation remains separate from MCP.

Disadvantages:

- Windows-specific transport;
- JSON is less compact than Protobuf;
- pipe lifecycle, reconnection, discovery, and stale endpoints still require explicit design;
- `StreamJsonRpc` becomes an implementation dependency that must remain behind an abstraction.

## Decision

RevitMCP will use **duplex Windows Named Pipes** as the initial local IPC transport between `RevitMCP.Server` and `RevitMCP.Addin`.

The application protocol over the pipe will be **JSON-RPC 2.0**, with **Microsoft StreamJsonRpc** as the initial .NET implementation.

### 1. Boundary ownership

The local bridge carries RevitMCP application contracts, not MCP messages.

Conceptually:

```text
MCP Client
    |
    | MCP
    v
RevitMCP.Server
    |
    | RevitMCP.Contracts
    | JSON-RPC 2.0
    | Windows Named Pipe
    v
RevitMCP.Addin
    |
    | ExternalEvent / valid Revit API context
    v
Revit API
```

MCP-specific schemas and transport behavior must terminate in `RevitMCP.Server`.

### 2. Security baseline

Named Pipes must be local-only and restricted to the intended local Windows user/session as strongly as practical.

The implementation should use Windows pipe security controls such as current-user-only behavior and/or explicit pipe ACLs as appropriate for the supported runtime versions.

The initial bridge must not expose a TCP listener or network-accessible endpoint.

A future remote/cloud architecture must connect through a separate gateway or agent boundary rather than exposing the local Named Pipe directly.

### 3. RPC abstraction

Application code must depend on a RevitMCP-owned bridge abstraction, not directly on `StreamJsonRpc` types.

Conceptually:

```text
IRevitBridgeClient
IRevitBridgeService
```

`StreamJsonRpc` is the initial implementation detail behind this abstraction and may be replaced by a later ADR without changing capability contracts.

### 4. Contract behavior

The bridge protocol must support:

- request/response correlation;
- explicit structured errors;
- cancellation where the operation can safely honor it;
- timeouts at the caller/orchestration layer;
- one-way notifications where useful for lifecycle/status events;
- protocol/version compatibility metadata;
- deterministic serialization of shared contract DTOs.

Long-running Revit operations still need Revit-aware execution and cancellation rules; transport-level cancellation does not imply that arbitrary Revit API work can be safely interrupted at any point.

### 5. Multi-instance model

Each running Revit process will have a distinct bridge identity and endpoint.

The exact endpoint naming convention, instance identifier, discovery registry, stale-instance cleanup, and selection behavior are intentionally deferred to the next architecture decision.

Process ID may be part of diagnostic metadata or endpoint naming, but MCP-facing APIs should not be forced to use raw OS process IDs as their long-term stable identity.

### 6. Cloud and Azure remain separate concerns

Azure hosting, Streamable HTTP, remote MCP gateways, enterprise agents, and other cloud deployment models remain valid future paths.

This decision deliberately separates:

```text
remote/cloud connectivity
        !=
local Revit IPC
```

A future Azure-hosted component can communicate with a local RevitMCP agent/gateway without changing the internal Revit Named Pipe bridge.

### 7. User interface is not decided by this ADR

The bridge does not require a chat interface inside Revit.

A future Revit dockable control center, approval UI, status/diagnostics UI, or richer conversational surface may reuse the same capability and bridge architecture. The product UI strategy remains a separate decision.

## Consequences

### Positive

- Revit does not need to host an HTTP server for local communication.
- Local network exposure and port-management concerns are avoided.
- The bridge remains independent of MCP client and cloud deployment choices.
- JSON-RPC provides mature RPC behavior without a custom wire protocol.
- Multi-instance Revit support can use independent local endpoints.
- The Revit process carries less infrastructure than a web/gRPC hosting stack would require.
- Future Azure or other remote gateways can be added outside the local IPC boundary.

### Negative / costs

- Named Pipes are Windows-specific.
- Instance discovery and endpoint lifecycle must be implemented separately.
- Security ACL behavior must be tested across supported Revit/.NET runtime combinations.
- Pipe reconnection, server restarts, Revit shutdown, and stale registrations need explicit handling.
- Integration tests must exercise both the RPC protocol and Revit execution dispatch.

## Deferred decisions

Separate decisions are required for:

- Revit instance identity and discovery;
- Named Pipe endpoint naming;
- registration and stale-instance cleanup;
- connection ownership and reconnection strategy;
- bridge protocol/version negotiation details;
- initial solution/project structure;
- remote/cloud gateway architecture;
- user-facing Revit UI strategy;
- write/destructive operation approval UX.

## References

- ADR-0001: `ADR-0001-out-of-process-mcp-and-revit-capability-host.md`
- .NET Named Pipes documentation: https://learn.microsoft.com/dotnet/standard/io/how-to-use-named-pipes-for-network-interprocess-communication
- `NamedPipeServerStream`: https://learn.microsoft.com/dotnet/api/system.io.pipes.namedpipeserverstream
- StreamJsonRpc: https://github.com/microsoft/vs-streamjsonrpc
- StreamJsonRpc connection documentation: https://microsoft.github.io/vs-streamjsonrpc/docs/connecting.html
- JSON-RPC 2.0 specification: https://www.jsonrpc.org/specification
