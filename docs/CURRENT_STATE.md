# RevitMCP Current State

_Last updated: 2026-09-09_

## Phase

Implementation started / first live Revit 2026.5 ExternalEvent-backed capability validation.

## What exists

- GitHub repository initialized.
- Canonical AI-agent working rules established in `AGENTS.md`.
- Project vision defined in `docs/VISION.md`.
- Architecture baseline defined in `docs/ARCHITECTURE.md`.
- ADR process initialized under `docs/adr/`.
- ADR-0001 accepts an out-of-process architecture with a standalone C#/.NET MCP server and a Revit capability/execution add-in.
- ADR-0002 accepts duplex Windows Named Pipes with JSON-RPC 2.0 for the local bridge, using Microsoft StreamJsonRpc as the initial implementation behind a RevitMCP-owned abstraction.
- ADR-0003 accepts opaque per-process Revit `instance_id` values, ephemeral LocalAppData registration records, bridge validation, and deterministic multi-instance selection behavior.
- ADR-0004 accepts a small SDK-style .NET solution with `Contracts`, `Bridge`, `Server`, and one multi-version `Addin` project.
- The ADR-0004 solution/project skeleton is committed: `RevitMCP.sln`, `global.json`, centralized build/package props, `src/RevitMCP.Contracts`, `src/RevitMCP.Bridge`, `src/RevitMCP.Server`, one multi-version `src/RevitMCP.Addin` project, and test projects for Contracts, Bridge, Server, and Addin.
- Revit 2025, 2026, and 2027 are built from the same add-in project using an explicit `RevitVersion` build property; the initial target matrix is `net8.0-windows` for Revit 2025/2026 and `net10.0-windows` for Revit 2027.
- GitHub Actions compiles the non-Revit projects and the 2025/2026/2027 add-in matrix.
- `RevitMCP.Contracts` defines transport-neutral registration, handshake, discovery-state, bridge error, and CAP-0001 `GetContext` contracts.
- `RevitMCP.Bridge` implements user-local atomic instance registration, current-user Named Pipe hosting, `bridge.handshake` over StreamJsonRpc, and discovery that classifies candidates as `Ready`, `Unavailable`, `Incompatible`, or `Stale`.
- StreamJsonRpc `2.25.29` is used only inside `RevitMCP.Bridge`, behind RevitMCP-owned abstractions. `NamedPipeBridgeClient` bounds handshake and capability response waits locally. Handshake timeout maps to `BRIDGE_HANDSHAKE_TIMEOUT`; `revit.get_context` timeout maps to `REVIT_EXECUTION_TIMEOUT` and also cancels the in-flight RPC so a still-queued EXEC-0001 item can be skipped without awaiting a silent-peer cancel acknowledgement.
- Autodesk Revit API binaries are not committed to the repository; builds restore version-pinned Nice3point compile-time references instead.
- Capability specifications are recorded under `docs/capabilities/` before implementation.
- CAP-0001 accepts `revit_get_context` as the first end-to-end read-only Revit capability. It returns bounded instance, active-document, active-view, and selection-count context without exposing paths or enumerating selection contents.
- Bridge specifications are recorded under `docs/bridge/` when accepted ADRs require concrete versioned technical contracts.
- BRIDGE-0001 defines the stable `bridge.handshake` bootstrap contract, identity validation, and integer bridge-protocol version negotiation. The handshake uses cached add-in/process metadata and must not invoke `ExternalEvent` or inspect the Revit model.
- BRIDGE-0002 is accepted. It introduces bridge protocol version `2` for `revit.get_context` while keeping version `1` handshake-compatible. A host advertises `[2, 1]` only when a functional CAP-0001 capability service is attached.
- Execution specifications are recorded under `docs/execution/`.
- Lifecycle specifications are recorded under `docs/lifecycle/`. LIFECYCLE-0001 is accepted.
- EXEC-0001 defines one serialized FIFO Revit execution dispatcher per Revit process, backed by one long-lived `ExternalEvent`, asynchronous completion, queued cancellation, non-destructive timeout semantics, failure isolation, and explicit transaction ownership outside the dispatcher.
- The EXEC-0001 queue/state machine is implemented in `src/RevitMCP.Addin/Execution/`. `RevitExecutionQueue<TContext>` is independently testable through `IRevitEventSignal`. `RevitExecutionDispatcher` owns one long-lived `ExternalEvent` / `IExternalEventHandler` pair.
- LIFECYCLE-0001 is accepted. `RevitMcpApplication` implements `IExternalApplication`, generates one process-lifetime `instance_id` in `OnStartup`, and bootstraps on the first eligible `Idling` callback: runtime metadata, EXEC-0001 dispatcher, capability service, Named Pipe listener ready, then ADR-0003 registration. Registration-first bridge teardown is implemented.
- The Addin project sets `CopyLocalLockFileAssemblies` so Revit 2025/2026/2027 plugin output includes the Bridge NuGet runtime graph (`StreamJsonRpc.dll` and its resolved dependencies). Nice3point Revit API assemblies remain compile-time only and are still asserted absent from output.
- Compile-time Revit API references are the version-pinned Nice3point packages: `Nice3point.Revit.Api.RevitAPI` / `RevitAPIUI` `2025.4.60` (Revit 2025), `2026.4.10` (Revit 2026), and `2027.2.0` (Revit 2027). Those assemblies are compile-time only and must not be copied into add-in output.
- `tests/RevitMCP.Addin.Tests` covers EXEC-0001 queue behavior and LIFECYCLE-0001 coordination without launching Revit.
- The initial MCP transport is `stdio`; Streamable HTTP, MCP Apps, WebMCP, Azure/cloud gateways, and other remote deployment paths remain extensions rather than core dependencies.
- Product UI considerations are recorded separately; universal access, conversational use inside or adjacent to Revit, and reduced context switching remain open product goals rather than settled architecture.

## CAP-0001 / BRIDGE-0002 implementation status

CAP-0001 as a whole is **not complete**. The MCP Server/tool slice is intentionally unimplemented.

### Implemented

- Transport-neutral `GetContextRequest` / `GetContextResult` contracts in `RevitMCP.Contracts`.
- Explicit `null` serialization for absent `document` and `active_view` without changing the global `ContractJson` ignore policy.
- Bridge protocol model `SupportedVersions = [2, 1]`, `CurrentVersion = 2`. Handshake-only hosts still advertise/guarantee version `1` only.
- `IRevitCapabilityService` composed beside `BridgeHandshakeService` in `NamedPipeBridgeHost`.
- JSON-RPC method `revit.get_context` on a small composite StreamJsonRpc adapter.
- Typed `NamedPipeBridgeClient.GetContextAsync` with explicit capability timeout, local protocol gating, and RPC cancellation on local timeout so queued EXEC-0001 work is not started after the caller has already timed out.
- Addin `RevitGetContextService` that dispatches through the process-lifetime EXEC-0001 dispatcher and collects only CAP-0001 fields.
- Lifecycle wiring: metadata -> dispatcher -> capability service -> bridge start. Shutdown order remains dispatcher.Stop -> registration withdrawal/bridge -> dispatcher dispose.

### Automated-tested

- Contracts: empty request, snake_case, project/family kinds, string `element_id`, explicit null document/view, selection `count` only, no selected IDs/paths/user/cloud/property bags, round-trip, exact accepted field set.
- Bridge: `[2,1]+[2,1] -> 2`, `[2,1]+[1] -> 1`, handshake-only host does not advertise v2, capability host advertises v2, Named Pipe `revit.get_context` with a fake service, local rejection before handshake and after v1, v2 success, structured `REVIT_EXECUTION_FAILED`, silent capability timeout, client timeout cancels the server request token, caller cancellation, existing silent handshake timeout, existing discovery/handshake/teardown tests.
- Addin/lifecycle: existing EXEC-0001 and LIFECYCLE-0001 tests, capability created before bridge start, handshake-only capability remains nullable, no v2 advertisement without a service at the host, no extra metadata fields.
- Solution tests: Contracts 15, Bridge 39, Addin 38, Server 2. All passed.
- Addin Release builds: Revit 2025 `net8.0-windows`, Revit 2026 `net8.0-windows`, Revit 2027 `net10.0-windows`. Each output has `StreamJsonRpc.dll` and `Nerdbank.Streams.dll` present; `RevitAPI.dll` and `RevitAPIUI.dll` absent.

### Live-tested on Autodesk Revit 2026.5 (`26.5.0.55`)

Zero-document Home state, no model open:

- Registration published `bridge_protocol_version = 2`.
- Real `NamedPipeBridgeClient` handshake with supported versions `[2, 1]` selected protocol `2`.
- Real `GetContextAsync` returned matching `instance_id`, `revit_version = 2026`, `revit_build = 26.5.0.55`, `document = null`, `active_view = null`, `selection.count = 0`.
- Request completed through EXEC-0001 / ExternalEvent without a Revit API context exception.
- Serialized zero-document result: **233 UTF-8 bytes**. Fields: `instance.instance_id`, `instance.revit_version`, `instance.revit_build`, `document`, `active_view`, `selection.count`.
- Normal Revit close withdrew the registration and shut down the Named Pipe.

This is the first live proof of:

```text
bridge thread -> dispatcher -> ExternalEvent.Raise -> ExternalEvent.Execute -> UIApplication -> response
```

### Still pending

- Active-project and selection-count live validation: **NOT RUN**. No disposable local/sample model was opened in this environment.
- Family-document live validation.
- Live Revit 2025 and Revit 2027 compatibility.
- MCP Server `revit_get_context` tool, structuredContent, and zero/one/many instance routing.

## What does not exist yet

- no live CAP-0001 validation against an open project or changed selection;
- no live lifecycle/handshake/capability validation on Revit 2025 or Revit 2027;
- no MCP server runtime, MCP tools, or `revit_list_instances` MCP exposure;
- no explicit document identity/addressing model;
- no request scheduling/fairness policy for multiple clients beyond FIFO serialization required by EXEC-0001;
- no write-locking or transaction concurrency policy;
- no deployment or packaging model;
- no finalized user-facing Revit UI strategy.

## Current priorities

1. Tech Lead review of the Addin + Bridge CAP-0001 slice.
2. Implement the MCP Server `revit_get_context` tool only after that review. Do not expand into additional capabilities, writes, Azure/cloud, WebMCP, or UI work.
3. Complete active-project/selection live validation on a disposable local or Autodesk sample model when it can be done safely.
4. Update project state from observed implementation/validation results before expanding the capability surface.

## Known constraints

- Revit API execution-context and transaction constraints must be respected.
- The solution should remain usable by multiple MCP-compatible clients and LLMs where technically possible.
- Existing Autodesk and third-party Revit MCP implementations should be benchmarked for useful patterns, limitations, interoperability opportunities, and product gaps; they do not determine whether RevitMCP should continue or whether an overlapping capability should exist.
- The architecture should preserve viable paths for both local agents and remote/cloud agents, subject to security, information-governance, and deployment-policy requirements.
- Local Revit IPC is intentionally separate from remote/cloud connectivity; future Azure or other hosted components must not require exposing the local Named Pipe directly.
- Revit process IDs are diagnostics, not the stable RevitMCP client-facing identity. ADR-0003 defines `instance_id` for process-lifetime identity.
- Instance discovery records are candidates only; a validated BRIDGE-0001 handshake is required before an instance is considered ready.
- The handshake is a stable bootstrap contract and must not depend on negotiated capability traffic in order to negotiate a compatible bridge protocol version.
- The handshake must not invoke the Revit execution queue or `ExternalEvent`; it is answered from cached add-in/process metadata.
- Capability requests requiring Revit API access must be dispatched through EXEC-0001 rather than executed on bridge/background threads.
- Revit API work is serialized FIFO in the initial dispatcher. Multiple bridge connections do not imply concurrent Revit API execution.
- Cancellation/timeout before execution prevents queued work from starting; a caller timeout after Revit execution begins must not forcibly abort the Revit thread or an active Revit API operation.
- The execution dispatcher must not automatically create Revit transactions. Transaction policy belongs to capability/application logic and future accepted write specifications.
- Revit instance identity is distinct from document identity.
- Revit-version-specific API differences should be confined to a compatibility boundary rather than scattered throughout capability or MCP-facing code.
- Autodesk Revit 2026.5 (`26.5.0.55`) preloads a .NET 10 generation host and RevitAPI `26.5.0.0` while the current 2026 add-in remains `net8.0-windows` compiled against Nice3point `2026.4.10`. The add-in loaded and completed handshake plus ExternalEvent-backed `revit.get_context` on that host; this is recorded compatibility evidence, not authorization to change the accepted TFM/API matrix.
- Cross-version compatibility requires all supported Revit add-in variants to compile in CI; local compilation against one Revit release is insufficient.
- Capability contracts are transport-neutral below the MCP adapter and must not depend on a specific LLM/client.
- `revit_get_context` is bounded by design: no model enumeration, selection enumeration, file paths, usernames, or cloud project identifiers in the base result.
- A host must not advertise bridge protocol version 2 unless a functional `revit.get_context` capability service is attached.
- WebMCP must remain in scope as an emerging integration surface.
- The public repository must not contain confidential internal discussions, project information, credentials, or organization-specific sensitive details.
- Arbitrary AI-generated code execution inside Revit is not part of the normal production capability surface.
- The Revit UI strategy remains open. A future control/approval surface or richer conversational experience may be considered without changing the core capability and bridge architecture.

## Next task

Review the Addin + Bridge CAP-0001 slice, then implement the MCP Server tool. Do not expand into additional Revit capabilities, writes, Azure/cloud, WebMCP, or UI work.
