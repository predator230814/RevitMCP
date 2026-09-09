# ADR-0003: Revit instance registration, discovery, and addressing

- Status: Accepted
- Date: 2026-09-08

## Context

RevitMCP must support users who may have zero, one, or several Autodesk Revit processes running at the same time. Multiple MCP clients may also connect concurrently through separate `RevitMCP.Server` processes.

ADR-0001 established a standalone MCP server and an in-process Revit capability/execution host. ADR-0002 established duplex Windows Named Pipes with JSON-RPC 2.0 for the local bridge. RevitMCP therefore needs a deterministic way to:

- identify a specific running RevitMCP-enabled Revit process;
- discover available local instances without scanning arbitrary network endpoints;
- locate the correct Named Pipe endpoint;
- distinguish a live registration from stale state left by a crashed process;
- avoid making operating-system process identifiers part of the stable public capability contract;
- preserve a future path for remote hosts or gateways without redesigning local Revit identity.

Autodesk's current Revit MCP uses the Revit process ID as the target identifier. That is a useful reference pattern but is not adopted as the RevitMCP domain identity because process IDs are operating-system details, can be reused over time, and do not by themselves prove that the RevitMCP add-in is loaded and responsive.

## Decision drivers

- Deterministic multi-instance addressing.
- Independence from raw operating-system process IDs.
- Safe local discovery without TCP service discovery.
- Compatibility with ADR-0002 Named Pipes.
- Ability to detect and clean stale registrations after crashes.
- Separation between discovery metadata and proof that the bridge is actually alive.
- Support for multiple simultaneous MCP clients.
- Minimal disk churn and implementation complexity.
- Future compatibility with local-to-cloud gateway scenarios.

## Options considered

### Option A: Use the Revit process ID as the public instance identity

Advantages:

- simple;
- easy to inspect and troubleshoot;
- mirrors Autodesk's current Revit MCP approach.

Disadvantages:

- exposes an OS implementation detail through the capability contract;
- PIDs are reused;
- PID existence does not prove the RevitMCP add-in or bridge is ready;
- makes later remote/multi-host addressing less natural.

### Option B: Discover instances only by enumerating Named Pipes

The server would scan the local Named Pipe namespace for RevitMCP pipe names and infer available Revit instances from those endpoints.

Advantages:

- no registration files;
- transport and discovery use the same OS primitive.

Disadvantages:

- couples discovery tightly to endpoint naming;
- provides limited metadata before connecting;
- makes stale-state semantics and future bridge changes harder to evolve;
- makes discovery behavior more dependent on implementation details of the Windows pipe namespace.

### Option C: Publish ephemeral per-instance registration records and verify them through the bridge

Each RevitMCP add-in session creates a unique opaque instance identity, starts its Named Pipe endpoint, and then atomically publishes a small local registration record describing how to reach it. The MCP server discovers registration records, validates their process metadata, and performs a JSON-RPC handshake before considering the instance ready.

Advantages:

- separates discovery from transport;
- allows useful metadata before bridge connection;
- supports explicit stale cleanup;
- keeps a stable domain identity independent of PID and pipe naming;
- works naturally with multiple simultaneous Revit processes;
- can evolve toward remote host registries later without changing `instance_id` semantics.

Disadvantages:

- requires small local registration files;
- requires lifecycle and stale-cleanup logic;
- registration metadata must be written safely and treated as untrusted until validated.

## Decision

RevitMCP will use **ephemeral per-process registration records with an opaque `instance_id`** for local Revit discovery and addressing.

### 1. Instance identity

Each successful startup of `RevitMCP.Addin` creates a new opaque `instance_id`, initially represented as a randomly generated GUID.

The identity represents a specific running RevitMCP-enabled Revit process lifetime. It is not:

- a Revit installation identity;
- a workstation identity;
- a Revit document identity;
- a Windows process ID.

A Revit restart therefore creates a new `instance_id`.

Client-facing capability contracts should use `instance_id` when explicit Revit targeting is required.

### 2. Process ID is diagnostic metadata

The registration includes the Revit process ID and process start time, but these remain operating-system metadata used for diagnostics and stale-registration validation.

`process_id` must not be the only long-term public identity of a Revit instance.

The pair of process ID and process start time is used during discovery to reduce false positives caused by PID reuse.

### 3. Registration location

Local registrations are stored under the current user's local application data in a RevitMCP-owned directory, conceptually:

```text
%LOCALAPPDATA%\RevitMCP\instances\<windows-session-id>\<instance-id>.json
```

The exact path naming may be refined during implementation, but the following properties are required:

- user-local rather than machine-wide;
- isolated by interactive Windows session where practical;
- writable without administrative privileges;
- not intended as a remote/network discovery mechanism.

### 4. Registration contents

The registration record contains only local discovery and compatibility metadata. The initial schema should include at least:

```text
instance_id
process_id
process_start_time_utc
windows_session_id
revit_version
revit_build
addin_version
bridge_protocol_version
pipe_name
registration_created_utc
```

Optional user-facing metadata may be added later, but document paths, project metadata, usernames, or other potentially sensitive model information should not be written merely for discovery unless there is a clear product requirement and security review.

### 5. Publish only after the endpoint is ready

Startup ordering is:

1. create the new `instance_id`;
2. establish the Named Pipe listener described by ADR-0002;
3. initialize the bridge sufficiently to answer the discovery handshake;
4. atomically publish the registration file.

A registration therefore should not advertise an endpoint that has not yet reached a minimum ready state.

Temporary-file-plus-rename or an equivalent atomic replacement pattern should be used so readers do not observe partially written JSON.

### 6. Discovery and validation

`RevitMCP.Server` discovers candidates by reading registration records in the current user's applicable local/session directory.

A registration file is a **candidate**, not proof of liveness.

For each candidate the server should:

1. parse and validate the registration schema;
2. verify that the recorded process exists;
3. verify that the process start time matches where available;
4. validate supported bridge protocol metadata;
5. connect to the declared Named Pipe endpoint;
6. perform a JSON-RPC handshake that returns authoritative instance metadata including the same `instance_id`.

Only a successful validated handshake makes an instance `Ready` for capability execution.

### 7. Stale registrations

On a normal Revit shutdown, `RevitMCP.Addin` should remove its registration record.

Abnormal termination may leave stale registrations. Discovery therefore also performs cleanup:

- if the process does not exist, or its start time no longer matches, the registration is stale and may be removed;
- if the process exists but the bridge cannot currently be reached, the registration is not immediately assumed dead and may be reported temporarily as unavailable;
- malformed or incompatible registrations are ignored and may be quarantined or removed according to implementation policy.

A high-frequency disk heartbeat is **not required for the initial implementation**. Process validation plus bridge handshake is the baseline liveness mechanism. A heartbeat may be added later only if operational evidence justifies it.

### 8. Pipe endpoint identity

Each Revit process exposes an endpoint associated with its `instance_id`. The exact pipe-name format is an implementation detail, but it must:

- be collision-resistant for multiple simultaneous Revit processes;
- include a RevitMCP namespace/version prefix;
- avoid embedding sensitive project information;
- remain local to the applicable Windows user/session security boundary.

The `pipe_name` is discovery metadata, not a client-facing Revit identity.

### 9. Multiple clients

A single RevitMCP-enabled Revit process may be used by more than one local MCP client/server process.

The bridge implementation must therefore support multiple client connections to the same logical Revit instance where feasible.

Concurrent bridge connections do **not** imply concurrent Revit API execution. Requests that require Revit API context will be coordinated by the Revit execution dispatcher/queue and `ExternalEvent` model.

Scheduling, fairness, cancellation, and write-operation concurrency policies are separate design concerns.

### 10. Client-facing selection behavior

The MCP-facing layer should provide a capability conceptually equivalent to `revit_list_instances` that returns stable RevitMCP instance metadata rather than requiring clients to enumerate operating-system processes.

Default targeting behavior should be deterministic:

- zero ready instances and no explicit `instance_id` -> return a no-instance error;
- exactly one ready instance and no explicit `instance_id` -> the server may select it automatically;
- more than one ready instance and no explicit `instance_id` -> return an instance-required result/error with candidates;
- explicit unknown or unavailable `instance_id` -> return an explicit instance-not-found or instance-unavailable error.

The server must not rely on a hidden long-lived "current Revit" selection that could silently target a different process later.

### 11. Revit instance identity is separate from document identity

`instance_id` identifies the Revit process lifetime, not the active RVT document.

The active document can change while `instance_id` remains unchanged. Explicit document identity/addressing, if required by future capabilities, will be designed separately.

### 12. Future remote/cloud topology

This ADR defines **local** Revit instance identity and discovery only.

A future remote gateway may introduce an additional host/workstation identity, for example conceptually:

```text
host_id + instance_id
```

without changing the semantics of the local `instance_id`.

Remote/cloud components must not access the LocalAppData registry or Named Pipe directly across the network; a separate secure local-to-remote adapter/gateway is required.

## Conceptual lifecycle

```text
Revit starts
    |
    v
RevitMCP.Addin creates instance_id
    |
    v
Named Pipe listener becomes ready
    |
    v
Registration published atomically
    |
    +-----------------------------+
    |                             |
    v                             v
RevitMCP.Server A           RevitMCP.Server B
    |                             |
    +------ discovery scan -------+
              |
              v
     validate process metadata
              |
              v
        JSON-RPC handshake
              |
              v
          instance Ready
              |
              v
       capability requests
              |
              v
      Revit execution queue
              |
              v
          ExternalEvent
              |
              v
           Revit API
```

## Consequences

### Positive

- Revit instances have an explicit domain identity independent of PID.
- Multiple running Revit processes can be addressed deterministically.
- Discovery remains local, simple, and independent of TCP ports.
- Discovery and transport are cleanly separated.
- Stale registration after crashes can be detected without continuous heartbeat writes.
- Multiple MCP clients can discover the same Revit instance.
- The model leaves room for later remote host/gateway addressing.

### Negative / costs

- The add-in and server must implement registration lifecycle and cleanup.
- Registration files are another local state surface that requires schema/version handling.
- The system must guard against malformed, spoofed, or stale local registration data and rely on bridge validation before execution.
- Multi-client execution introduces later scheduling and write-concurrency questions.

## Deferred decisions

This ADR intentionally does not decide:

- explicit document identity/addressing;
- request scheduling/fairness across multiple clients;
- write-locking or transaction concurrency policy;
- exact solution/project structure;
- detailed bridge handshake schema and protocol negotiation;
- remote host/workstation identity;
- remote/cloud instance discovery;
- user-facing Revit UI or conversational experience.

## References

- ADR-0001: Out-of-process MCP server and Revit capability host
- ADR-0002: Named Pipes + JSON-RPC local bridge
- Microsoft Windows Named Pipes documentation: https://learn.microsoft.com/windows/win32/ipc/named-pipes
- .NET `System.IO.Pipes` documentation: https://learn.microsoft.com/dotnet/api/system.io.pipes
