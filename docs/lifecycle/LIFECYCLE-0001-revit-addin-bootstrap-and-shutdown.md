# LIFECYCLE-0001: Revit add-in bootstrap and shutdown

- Status: Accepted
- Date: 2026-09-09

## Purpose

Define the minimum lifecycle required to turn the existing RevitMCP add-in infrastructure into a correctly initialized local Revit instance without violating Autodesk Revit API execution-context rules.

This specification connects the accepted ADR-0003 discovery model, BRIDGE-0001 handshake, and EXEC-0001 execution dispatcher. It intentionally does not implement CAP-0001 or any MCP-facing tool.

## Problem

Revit `IExternalApplication.OnStartup` and `OnShutdown` are synchronous lifecycle callbacks, while the current Named Pipe bridge host uses asynchronous startup/disposal.

The add-in must also guarantee that an ADR-0003 registration is never published before the EXEC-0001 dispatcher and Named Pipe handshake endpoint are actually ready.

The initial lifecycle therefore needs one explicit, controlled boundary between Revit's synchronous lifecycle and the asynchronous bridge infrastructure.

## Decision summary

The initial RevitMCP add-in lifecycle will:

1. perform only minimal preparation in `IExternalApplication.OnStartup`;
2. generate the process-lifetime `instance_id` and subscribe a one-shot bootstrap handler to Revit `Idling`;
3. perform real bootstrap during the first eligible `Idling` callback;
4. create the EXEC-0001 dispatcher before starting the bridge;
5. start the Named Pipe listener before publishing the ADR-0003 registration;
6. expose no registration if bootstrap fails;
7. perform no automatic startup retry in the initial implementation;
8. stop accepting execution work and remove readiness before tearing down the bridge and dispatcher;
9. use no `async void`, unobserved fire-and-forget bootstrap, or background-thread Revit API access.

## Lifecycle states

The implementation should maintain a small RevitMCP-owned lifecycle state, conceptually:

```text
Created
BootstrapPending
Starting
Ready
Faulted
Stopping
Stopped
```

The exact representation and type names are implementation details.

State transitions must be guarded so duplicate `Idling` notifications, startup failure, and shutdown cannot initialize or dispose the runtime twice.

## OnStartup

`IExternalApplication.OnStartup(UIControlledApplication application)` must remain small and synchronous.

It should:

1. verify/create only non-Revit-model bootstrap state needed for the add-in lifetime;
2. generate one opaque `instance_id` for this successful add-in process lifetime;
3. subscribe a bootstrap handler to `application.Idling`;
4. transition to `BootstrapPending`;
5. return `Result.Succeeded`.

`OnStartup` must not:

- start the Named Pipe bridge;
- publish an ADR-0003 registration;
- create one `ExternalEvent` per request;
- inspect documents/views/selection;
- call CAP-0001;
- use `Task.Run` to call Revit API;
- launch unobserved asynchronous bootstrap work.

If the minimal preparation required to subscribe/bootstrap fails synchronously, `OnStartup` may return `Result.Failed` after cleaning up anything already created.

## First-Idling bootstrap

The first eligible Revit `Idling` callback is the bootstrap boundary.

The handler must unsubscribe itself or atomically guard against re-entry before performing initialization.

The `Idling` sender provides the `UIApplication` required to obtain real runtime Revit metadata and to create Revit API execution infrastructure in a valid context.

The bootstrap sequence is:

```text
first Idling
    -> transition BootstrapPending -> Starting
    -> unsubscribe/disable bootstrap handler
    -> capture runtime/process metadata
    -> create RevitExecutionDispatcher
    -> start NamedPipeBridgeHost
       -> Named Pipe listener becomes ready
       -> ADR-0003 registration is published atomically
    -> retain dispatcher + bridge host for add-in lifetime
    -> transition Starting -> Ready
```

The order is mandatory.

A registration must never exist for an instance whose dispatcher was not initialized successfully.

## Runtime metadata

Bootstrap should populate `BridgeInstanceMetadata` from authoritative runtime sources:

- `instance_id`: opaque value generated for this add-in process lifetime;
- `process_id`: current Revit process ID;
- `process_start_time_utc`: current Revit process start time in UTC;
- `windows_session_id`: current process Windows session ID;
- `revit_version`: `UIApplication.Application.VersionNumber`;
- `revit_build`: `UIApplication.Application.VersionBuild`;
- `addin_version`: RevitMCP.Addin assembly/application version;
- supported bridge protocol versions: the accepted bridge protocol list.

Bootstrap metadata must not contain document paths, usernames, active-document details, cloud project identifiers, or other BIM/model data.

Bootstrap must work when Revit has no active document.

## Synchronous-to-asynchronous bridge boundary

The current `NamedPipeBridgeHost.StartAsync` is asynchronous because it coordinates background IPC and filesystem publication.

The initial lifecycle may bridge this from the synchronous `Idling` callback with one explicit, controlled sync-over-async boundary, conceptually:

```text
NamedPipeBridgeHost.StartAsync(...)
    .GetAwaiter()
    .GetResult()
```

This is permitted only at the lifecycle adapter boundary and is not a general application pattern.

Requirements:

- the wait must be bounded by a startup cancellation/timeout token; no unbounded wait on the Revit thread;
- the async bridge implementation must not require the Revit synchronization context to complete;
- no Revit API calls may occur inside asynchronous/background bridge work;
- no `.Wait()` / `.Result` blocking is permitted inside `ExternalEvent.Execute` or capability execution;
- the exact startup timeout duration is an implementation/maintenance parameter, not a protocol or capability contract.

The existing bridge host already waits for its listener to become ready before publishing the registration; that invariant must be preserved.

## Readiness invariant

An instance is externally discoverable as ready only after all of the following are true:

```text
RevitExecutionDispatcher exists
    -> Named Pipe listener is accepting connections
    -> registration is published
    -> discovery validates process identity
    -> BRIDGE-0001 handshake succeeds
    -> Ready
```

The registration file is therefore a readiness candidate, never merely a signal that the add-in assembly loaded.

## Bootstrap failure

If dispatcher creation, bridge startup, or registration publication fails during first-Idling bootstrap:

1. no registration may remain published;
2. any partially started bridge host must be disposed;
3. any created dispatcher must be stopped/disposed safely;
4. lifecycle transitions to `Faulted`;
5. Revit remains running;
6. RevitMCP is unavailable for that Revit session unless a later accepted design introduces recovery.

The initial implementation performs no automatic retry and does not resubscribe to `Idling` after bootstrap failure.

Because `OnStartup` has already returned `Result.Succeeded` by this point, a later bootstrap failure must not pretend that Revit rejected the add-in startup. The failure belongs in local diagnostics/logging rather than a false Ready registration.

## No fire-and-forget bootstrap

The initial implementation must not use:

- `async void` for bootstrap;
- unobserved `Task.Run` bootstrap;
- a task that continues initialization after the one-shot lifecycle callback without owned completion/error state;
- background-thread calls to Autodesk Revit API.

The goal is deterministic ownership and failure, not merely avoiding UI blocking at any cost.

## Resource ownership

The `IExternalApplication` instance, directly or through one small lifecycle coordinator, owns the process-lifetime resources:

- one generated `instance_id`;
- the one-shot `Idling` subscription while bootstrap is pending;
- one `RevitExecutionDispatcher` after creation;
- one `NamedPipeBridgeHost` after startup;
- lifecycle state and minimal diagnostics.

Do not introduce a general DI container, hosted-service framework, retry manager, health supervisor, or application framework for this initial lifecycle.

## OnShutdown

`IExternalApplication.OnShutdown(UIControlledApplication application)` must synchronously begin a deterministic shutdown.

Conceptual order:

```text
1. transition to Stopping / prevent bootstrap from starting
2. unsubscribe pending bootstrap Idling handler if still subscribed
3. dispatcher.Stop() so no new capability work is accepted
4. remove/invalidate ADR-0003 registration
5. stop Named Pipe listener/connections
6. dispose bridge infrastructure
7. dispose RevitExecutionDispatcher / ExternalEvent
8. transition to Stopped
```

If bootstrap never reached `Ready`, shutdown cleans up whichever resources actually exist and remains idempotent.

## Registration-first bridge teardown

The bridge shutdown path must remove/invalidate the registration before leaving a stopped/unreachable Named Pipe endpoint advertised as a live discovery candidate.

The current `NamedPipeBridgeHost.DisposeAsync` implementation removes its registration after canceling/waiting for the accept loop. The lifecycle implementation should refine that ordering so readiness is withdrawn first, while keeping disposal idempotent and preserving normal registration-lease cleanup.

This refinement belongs to the bridge/lifecycle implementation, not to MCP-facing contracts.

## Shutdown synchronous-to-asynchronous boundary

`OnShutdown` may use the same narrowly controlled sync-over-async lifecycle boundary to dispose asynchronous bridge resources.

Requirements:

- no unbounded wait;
- no dependency on a Revit synchronization-context continuation;
- no remote/client acknowledgement required to let Revit exit;
- shutdown exceptions must be contained/logged rather than escaping in a way that leaves cleanup half-owned;
- dispatcher/ExternalEvent disposal happens only after queued-not-started work has been rejected and bridge readiness has been withdrawn.

The exact shutdown timeout is an implementation/maintenance parameter.

## EXEC-0001 relationship

BRIDGE-0001 handshake remains outside the execution queue and is answered from cached startup metadata.

Future Revit capability RPCs must use the dispatcher created by this lifecycle.

Lifecycle startup must create the dispatcher before registration is published so a successful discovery/handshake implies capability execution infrastructure exists.

The dispatcher does not automatically create transactions.

## CAP-0001 relationship

CAP-0001 remains out of scope for LIFECYCLE-0001 implementation.

After lifecycle wiring is implemented and reviewed, CAP-0001 may use the retained dispatcher to execute `GetContextRequest` in `ExternalEvent.Execute(UIApplication)`.

No active document is required merely to bootstrap RevitMCP.

## Threading rules

- Autodesk Revit API access remains on valid Revit API callbacks/ExternalEvent context only.
- Named Pipe accept/client handling remains background infrastructure and must not call Revit API directly.
- `OnStartup`, bootstrap `Idling`, `ExternalEvent.Execute`, and `OnShutdown` must not wait for MCP clients or remote acknowledgements.
- Lifecycle synchronization must be sufficient to make bootstrap-vs-shutdown and duplicate-bootstrap races deterministic.

## Diagnostics

Initial diagnostics should be local and bounded.

At minimum, implementation should make it possible to determine:

- whether lifecycle is BootstrapPending, Ready, Faulted, or Stopped;
- startup failure reason locally;
- the generated `instance_id` when available.

Do not expose stack traces, machine secrets, paths, usernames, or project/model data through the registration contract.

A broader logging/observability architecture remains deferred.

## Non-goals

LIFECYCLE-0001 does not introduce:

- CAP-0001 implementation;
- MCP Server runtime or tools;
- write/destructive operations;
- transactions;
- automatic bootstrap retries;
- watchdog/heartbeat;
- application health manager;
- Windows service/background process;
- Azure/cloud gateway;
- WebMCP;
- MCP Apps;
- Revit chat/UI;
- installer/packaging design;
- persistent document identity.

## Required automated coverage

Where practical without running Revit, tests should cover lifecycle coordination through small abstractions/fakes, including:

1. startup subscribes exactly one bootstrap callback;
2. duplicate bootstrap callbacks cannot initialize twice;
3. dispatcher creation precedes bridge readiness/publication;
4. failed bridge startup disposes the dispatcher and publishes no registration;
5. failed registration publication leaves no live owned bridge/registration;
6. shutdown before first Idling cancels bootstrap cleanly;
7. shutdown after Ready stops dispatcher before withdrawing bridge readiness;
8. shutdown is idempotent;
9. bootstrap-vs-shutdown race cannot publish readiness after stopping begins;
10. metadata mapping excludes BIM/model/user data;
11. zero-active-document is not a lifecycle failure.

Tests must not require Autodesk Revit to be installed unless they specifically exercise real Revit integration.

## Real Revit validation

Before considering the lifecycle fully validated, manually verify in supported Revit versions as appropriate that:

1. the add-in loads successfully;
2. first-Idling bootstrap creates the dispatcher without API-context exceptions;
3. the registration appears only after the Named Pipe endpoint is usable;
4. BRIDGE-0001 handshake succeeds against the live Revit instance;
5. no document is required to reach Ready;
6. closing Revit removes the registration and terminates the bridge without deadlock;
7. no Revit API call occurs from bridge/background threads.

This validation is separate from compile/CI success.

## Acceptance criteria

LIFECYCLE-0001 is implemented correctly when:

1. one `IExternalApplication`-owned lifecycle exists per loaded add-in instance;
2. `OnStartup` performs minimal synchronous setup and defers bootstrap to one-shot `Idling`;
3. one process-lifetime `instance_id` is used for bootstrap/registration;
4. runtime Revit version/build metadata comes from `UIApplication.Application`;
5. one EXEC-0001 dispatcher is created before bridge publication;
6. one Named Pipe host is ready before registration publication;
7. startup failure leaves no Ready registration or orphaned owned resources;
8. no automatic retry/fire-and-forget bootstrap exists;
9. shutdown stops new execution, withdraws registration, stops bridge, then disposes dispatcher;
10. lifecycle sync-over-async waits are narrowly scoped and bounded;
11. automated lifecycle tests cover race/failure ordering where practical;
12. all Revit 2025/2026/2027 add-in builds remain green;
13. real Revit validation confirms bootstrap/shutdown behavior without API-thread misuse or deadlock.

## Deferred decisions

The following remain deferred:

- automatic bootstrap retry/recovery;
- user-visible lifecycle error UI;
- richer logging/telemetry;
- health monitoring;
- bridge reconnect policies beyond connection-level behavior;
- CAP-0001 and later capabilities;
- remote/cloud lifecycle coordination;
- installer/manifest deployment strategy beyond what is minimally required for manual validation.

## References

- ADR-0001: Out-of-process MCP server and Revit capability host
- ADR-0002: Named Pipes + JSON-RPC local bridge
- ADR-0003: Revit instance registration, discovery, and addressing
- ADR-0004: Solution structure and multi-version build
- BRIDGE-0001: Handshake and protocol-version negotiation
- EXEC-0001: Revit execution queue and ExternalEvent dispatch
- CAP-0001: `revit_get_context`
- Autodesk Revit API Developer Guide: External Applications / `IExternalApplication`
- Autodesk Revit API reference: `UIApplication.Idling`
