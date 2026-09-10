# RevitMCP Current State

_Last updated: 2026-09-10_

## Phase

Implementation started / CAP-0001 live-validated on Revit 2026.5 for Home (no document) and an open disposable project with changing UI selection. CAP-0002 `revit_query_elements` is accepted as the next bounded read-only capability and is not yet implemented.

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
- ADR-0005 makes bounded, filtered, deterministic agent context and token efficiency a first-class capability-design requirement.
- ADR-0006 accepts an opaque process-scoped `document_id` for each open-document lifetime and opaque cross-call `element_ref` values based initially on Revit `Element.UniqueId`; `document_id` may be generated from a GUID internally but remains an opaque string contractually.
- The ADR-0004 solution/project skeleton is committed: `RevitMCP.sln`, `global.json`, centralized build/package props, `src/RevitMCP.Contracts`, `src/RevitMCP.Bridge`, `src/RevitMCP.Server`, one multi-version `src/RevitMCP.Addin` project, and test projects for Contracts, Bridge, Server, and Addin.
- Revit 2025, 2026, and 2027 are built from the same add-in project using an explicit `RevitVersion` build property; the initial target matrix is `net8.0-windows` for Revit 2025/2026 and `net10.0-windows` for Revit 2027.
- GitHub Actions compiles the non-Revit projects and the 2025/2026/2027 add-in matrix.
- `RevitMCP.Contracts` defines transport-neutral registration, handshake, discovery-state, bridge error, and CAP-0001 `GetContext` contracts.
- `RevitMCP.Bridge` implements user-local atomic instance registration, current-user Named Pipe hosting, `bridge.handshake` over StreamJsonRpc, and discovery that classifies candidates as `Ready`, `Unavailable`, `Incompatible`, or `Stale`.
- StreamJsonRpc `2.25.29` is used only inside `RevitMCP.Bridge`, behind RevitMCP-owned abstractions. `NamedPipeBridgeClient` bounds handshake and capability response waits locally. Handshake timeout maps to `BRIDGE_HANDSHAKE_TIMEOUT`; `revit.get_context` timeout maps to `REVIT_EXECUTION_TIMEOUT` and also cancels the in-flight RPC so a still-queued EXEC-0001 item can be skipped without awaiting a silent-peer cancel acknowledgement.
- Autodesk Revit API binaries are not committed to the repository; builds restore version-pinned Nice3point compile-time references instead.
- Capability specifications are recorded under `docs/capabilities/` before implementation.
- CAP-0001 accepts `revit_get_context` as the first end-to-end read-only Revit capability. It returns bounded instance, active-document, active-view, and selection-count context without exposing paths or enumerating selection contents.
- CAP-0002 accepts `revit_query_elements` as the next read-only capability. It requires intentional scope plus at least one bounded filter and returns exact `matched_count`, `truncated`, and at most 100 opaque `element_ref` values rather than element bodies.
- Bridge specifications are recorded under `docs/bridge/` when accepted ADRs require concrete versioned technical contracts.
- BRIDGE-0001 defines the stable `bridge.handshake` bootstrap contract, identity validation, and integer bridge-protocol version negotiation. The handshake uses cached add-in/process metadata and must not invoke `ExternalEvent` or inspect the Revit model.
- BRIDGE-0002 is accepted. It introduces bridge protocol version `2` for `revit.get_context` while keeping version `1` handshake-compatible. A host advertises `[2, 1]` only when a functional CAP-0001 capability service is attached.
- BRIDGE-0003 is accepted for CAP-0002. It introduces bridge protocol version `3`, which explicitly guarantees both `revit.get_context` and `revit.query_elements`; current capability sets are get-context `{2,3}` and query-elements `{3}`, never a numeric `>=` rule.
- SERVER-0002 is accepted as the stdio MCP exposure contract for CAP-0002. Once implemented, `tools/list` will expose exactly `revit_get_context` and `revit_query_elements` with explicit registration and strict structured schemas.
- Execution specifications are recorded under `docs/execution/`.
- Lifecycle specifications are recorded under `docs/lifecycle/`. LIFECYCLE-0001 is accepted.
- EXEC-0001 defines one serialized FIFO Revit execution dispatcher per Revit process, backed by one long-lived `ExternalEvent`, asynchronous completion, queued cancellation, non-destructive timeout semantics, failure isolation, and explicit transaction ownership outside the dispatcher.
- The EXEC-0001 queue/state machine is implemented in `src/RevitMCP.Addin/Execution/`. `RevitExecutionQueue<TContext>` is independently testable through `IRevitEventSignal`. `RevitExecutionDispatcher` owns one long-lived `ExternalEvent` / `IExternalEventHandler` pair.
- LIFECYCLE-0001 is accepted. `RevitMcpApplication` implements `IExternalApplication`, generates one process-lifetime `instance_id` in `OnStartup`, and bootstraps on the first eligible `Idling` callback: runtime metadata, EXEC-0001 dispatcher, capability service, Named Pipe listener ready, then ADR-0003 registration. Registration-first bridge teardown is implemented.
- The Addin project sets `CopyLocalLockFileAssemblies` so Revit 2025/2026/2027 plugin output includes the Bridge NuGet runtime graph (`StreamJsonRpc.dll` and its resolved dependencies). Nice3point Revit API assemblies remain compile-time only and are still asserted absent from output.
- Compile-time Revit API references are the version-pinned Nice3point packages: `Nice3point.Revit.Api.RevitAPI` / `RevitAPIUI` `2025.4.60` (Revit 2025), `2026.4.10` (Revit 2026), and `2027.2.0` (Revit 2027). Those assemblies are compile-time only and must not be copied into add-in output.
- `tests/RevitMCP.Addin.Tests` covers EXEC-0001 queue behavior and LIFECYCLE-0001 coordination without launching Revit.
- The initial MCP transport is `stdio`. `RevitMCP.Server` now hosts a real stdio MCP process using official `ModelContextProtocol` `2.2.0`. Streamable HTTP, MCP Apps, WebMCP, Azure/cloud gateways, and other remote deployment paths remain extensions rather than core dependencies.
- Product UI considerations are recorded separately; universal access, conversational use inside or adjacent to Revit, and reduced context switching remain open product goals rather than settled architecture.

## CAP-0001 / SERVER-0001 implementation status

CAP-0001 is implemented end-to-end for the accepted base contract. Active-project live validation on Revit 2026.5 is **PASS**. Family-document, live Revit 2025, live Revit 2027, and live multi-instance routing remain pending compatibility validation. No additional capability is implemented yet.

### Implemented

- Transport-neutral `GetContextRequest` / `GetContextResult` contracts in `RevitMCP.Contracts`.
- Explicit `null` serialization for absent `document` and `active_view` without changing the global `ContractJson` ignore policy.
- Bridge protocol model `SupportedVersions = [2, 1]`, `CurrentVersion = 2`. Handshake-only hosts still advertise/guarantee version `1` only.
- `IRevitCapabilityService` composed beside `BridgeHandshakeService` in `NamedPipeBridgeHost`.
- JSON-RPC method `revit.get_context` on a small composite StreamJsonRpc adapter.
- Typed `NamedPipeBridgeClient.GetContextAsync` with explicit capability timeout, local protocol gating, and RPC cancellation on local timeout so queued EXEC-0001 work is not started after the caller has already timed out.
- Addin `RevitGetContextService` that dispatches through the process-lifetime EXEC-0001 dispatcher and collects only CAP-0001 fields.
- Lifecycle wiring: metadata -> dispatcher -> capability service -> bridge start. Shutdown order remains dispatcher.Stop -> registration withdrawal/bridge -> dispatcher dispose.
- SERVER-0001: `RevitMCP.Server` is a real stdio MCP process using official `ModelContextProtocol` `2.2.0` only inside the Server boundary.
- `tools/list` exposes exactly one Revit tool today: `revit_get_context`.
- Fresh current-session discovery, deterministic 0/1/many routing, and a fresh typed bridge invocation (`handshake [2,1]`, require selected protocol exactly `2`) on every MCP call.
- Modern MCP success uses CAP-0001 `structuredContent` with empty `content`. Errors use `isError: true` and one compact JSON text block without violating the success `outputSchema`.

### Automated-tested

- Contracts: empty request, snake_case, project/family kinds, string `element_id`, explicit null document/view, selection `count` only, no selected IDs/paths/user/cloud/property bags, round-trip, exact accepted field set.
- Bridge: `[2,1]+[2,1] -> 2`, `[2,1]+[1] -> 1`, handshake-only host does not advertise v2, capability host advertises v2, Named Pipe `revit.get_context` with a fake service, local rejection before handshake and after v1, v2 success, structured `REVIT_EXECUTION_FAILED`, silent capability timeout, client timeout cancels the server request token, caller cancellation, existing silent handshake timeout, existing discovery/handshake/teardown tests.
- Addin/lifecycle: existing EXEC-0001 and LIFECYCLE-0001 tests, capability created before bridge start, handshake-only capability remains nullable, no v2 advertisement without a service at the host, no extra metadata fields.
- Server: 0/1/many routing, deterministic candidate ordering/fields, explicit unknown/unavailable/incompatible/stale/v1 mapping, no alternate after explicit failure, no mass `get_context` during ambiguity, handshake `[2,1]` and pipe targeting, capability timeout/failure codes, caller cancellation, dispose, MCP tool metadata/schemas, modern structuredContent, legacy compact JSON fallback for pre-2025-06-18 revisions, error `isError` without structuredContent, process-level stdio `tools/list` against the built Server executable, no Autodesk Revit API / AspNetCore MCP package.
- Solution tests: Contracts 15, Bridge 39, Addin 38, Server 46. All passed.
- Server Release `net10.0` build succeeded.
- Addin Release builds: Revit 2025 `net8.0-windows`, Revit 2026 `net8.0-windows`, Revit 2027 `net10.0-windows`. Each output has `StreamJsonRpc.dll` and `Nerdbank.Streams.dll` present; `RevitAPI.dll` and `RevitAPIUI.dll` absent.

### Live-tested on Autodesk Revit 2026.5 (`26.5.0.55`)

Zero-document Home state, no model open, one eligible instance, registration `bridge_protocol_version = 2`:

- Official `ModelContextProtocol` `2.2.0` client launched the actual Release `RevitMCP.Server` over stdio. The Server process was not restarted between calls.
- `tools/list` exposed exactly `revit_get_context`.
- Omitted `instance_id` auto-selected the single eligible instance and returned success.
- Structured result: matching `instance_id`, `revit_version = 2026`, `revit_build = 26.5.0.55`, `document = null`, `active_view = null`, `selection.count = 0`.
- Modern success `structuredContent` = **175 UTF-8 bytes**. `content` = **0 bytes**. Fields: `instance`, `document`, `active_view`, `selection`.
- Explicit returned `instance_id` retried against the same instance and succeeded.
- After normal Revit close, the same MCP Server process returned a tool execution error. Fresh discovery settled on `NO_REVIT_INSTANCE` (`isError: true`, no structuredContent, no protocol crash). One intermediate poll during teardown mapped to `REVIT_EXECUTION_FAILED` before registration withdrawal completed.

This is live proof of:

```text
real MCP client -> stdio -> RevitMCP.Server -> revit_get_context
-> discovery -> Named Pipe -> bridge.handshake [2,1]
-> revit.get_context -> EXEC-0001 / ExternalEvent -> UIApplication
-> CAP-0001 structuredContent
```

Active-project live validation on Autodesk Revit 2026.5 (`26.5.0.55`), registration `bridge_protocol_version = 2`, disposable local project only (generic titles `Default_M_ENU` then `Project1`; no production model, no local path recorded):

```text
CAP-0001 active-project live validation on Revit 2026.5: PASS
selection count 0: PASS
selection count 1: PASS
selection count N: PASS (count = 2)
active view: PASS
active-view switch: NOT RUN
explicit instance retry: PASS
modern content duplication: none
forbidden fields absent: PASS
```

Official `ModelContextProtocol` `2.2.0` client (`McpClient` / `StdioClientTransport`) launched the actual Release `RevitMCP.Server` over stdio. `tools/list` exposed exactly `revit_get_context`. Omitted `instance_id` auto-selected the single eligible instance. Success: `isError = false`, `content = []`, `structuredContent` present.

Observed document (non-null): `kind = project`. Document booleans deserialized as booleans and were all `false` on the disposable unsaved projects (`is_workshared`, `is_model_in_cloud`, `is_read_only`, `is_modified`). Those values were not hard-coded as acceptance criteria.

Observed active view (non-null): `element_id` remained a JSON string, `name` and `view_type` non-empty. Example: `element_id = "32"`, `name = L1 - Architectural`, `view_type = FloorPlan`. No Server restart and no RevitMCP reconnect were required between selection-state calls on the same Server process.

Selection sequence observed: `0 -> 1 -> 2`. Each successful payload contained only the accepted CAP-0001 fields (`instance`, `document`, `active_view`, `selection`). No `path` / `file_path` / `central_path` / `username` / `user` / `cloud_project_id` / `project_guid` / `pipe_name` / `process_id` / `session_id` / `selected_ids` / `element_ids` / `elements` / `parameters` / `diagnostics` and no generic property bag. `selection` exposed `count` only.

UTF-8 sizes:

| State | structuredContent | content |
| --- | ---: | ---: |
| disposable project + selection 0 | 361 | 0 |
| disposable project + selection 1 | 364 | 0 |
| disposable project + selection 2 | 364 | 0 |

The 361-byte and 364-byte measurements were taken in different disposable project/view contexts, so that cross-context difference is not attributed to selection. On the same open project, selection `1` and `2` produced identical **364**-byte structured payloads; only `selection.count` changed. Payload size did not grow with selected element count.

Explicit `instance_id` retry on the returned id succeeded against the same instance and returned the same active project context. CAP-0001 collection does not create a Revit `Transaction`, `SubTransaction`, or `TransactionGroup`. UI selection changes used for validation are not RevitMCP model writes.

### Still pending compatibility validation

- Family-document live validation: **NOT RUN**.
- Live Revit 2025 validation: **NOT RUN**.
- Live Revit 2027 validation: **NOT RUN**.
- Live multi-instance routing: **NOT RUN**. Automated 0/1/many coverage remains in Server tests.

## CAP-0002 design status

CAP-0002 `revit_query_elements` is **Accepted / NOT IMPLEMENTED**.

Accepted v1 design:

- Read-only query of the active document only.
- Required `scope = document | active_view`.
- At least one filter required from category name, family name, type name, level name, or name text substring.
- Filter arrays use OR within the array; different filter dimensions are ANDed.
- Revit `ElementType` objects are not returned as query population.
- Linked-document contents, parameter filters, bounding boxes, arbitrary view ids, geometry, worksets, phases, design options, and pagination are deferred.
- `limit` defaults to 50 and is capped at 100.
- Result is only `context { instance_id, document_id }`, exact `matched_count`, `truncated`, and bounded opaque `element_refs`.
- `document_id` is an opaque active-document lifetime guard from ADR-0006; explicit mismatch returns `DOCUMENT_CONTEXT_CHANGED` rather than silently using another active document.
- `element_ref` is opaque and initially based on Revit `Element.UniqueId`, not a numeric `ElementId` intended for retention.
- BRIDGE-0003 protocol v3 guarantees both `revit.get_context` and `revit.query_elements`; get-context support is explicitly `{2,3}`, query-elements support is `{3}`.
- SERVER-0002 extends the existing stdio host to exactly two MCP tools while preserving fresh discovery, typed Bridge calls, strict structured output, and empty modern text content.

## What does not exist yet

- no implemented CAP-0002 code yet;
- no implemented process-lifetime `document_id` service yet; ADR-0006 defines the accepted semantics;
- no second MCP tool yet; `revit_query_elements` is specified but not implemented;
- no live CAP-0001 family-document validation;
- no live lifecycle/handshake/capability validation on Revit 2025 or Revit 2027;
- no live multi-instance routing validation;
- no CAP-0003 element-inspection capability;
- no persistent cross-session document identity/addressing model;
- no request scheduling/fairness policy for multiple clients beyond FIFO serialization required by EXEC-0001;
- no write-locking or transaction concurrency policy;
- no deployment or packaging model;
- no finalized user-facing Revit UI strategy.

## Current priorities

1. Review and merge ADR-0006, CAP-0002, BRIDGE-0003, and SERVER-0002 as the accepted source of truth for the next read-only slice.
2. Implement CAP-0002 in small reviewable steps without introducing CAP-0003 or a generic capability framework.
3. Keep family-document, live Revit 2025, live Revit 2027, and live multi-instance routing as pending compatibility validations; they are not blockers for starting CAP-0002.
4. Do not expand into writes, Azure/cloud, WebMCP implementation, or UI work during the CAP-0002 slice.

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
- ADR-0006 `document_id` is opaque, process/open-document-lifetime scoped, and may use a generated GUID internally without exposing UUID semantics in the contract.
- Cross-call element handles for new query/inspection capabilities use opaque `element_ref` semantics based initially on Revit `Element.UniqueId`; numeric `ElementId` must not become the durable chaining contract.
- CAP-0001 `active_view.element_id` remains unchanged because it describes current context rather than a durable element handle.
- Revit-version-specific API differences should be confined to a compatibility boundary rather than scattered throughout capability or MCP-facing code.
- Autodesk Revit 2026.5 (`26.5.0.55`) preloads a .NET 10 generation host and RevitAPI `26.5.0.0` while the current 2026 add-in remains `net8.0-windows` compiled against Nice3point `2026.4.10`. The add-in loaded and completed handshake plus ExternalEvent-backed `revit.get_context` on that host; this is recorded compatibility evidence, not authorization to change the accepted TFM/API matrix.
- Cross-version compatibility requires all supported Revit add-in variants to compile in CI; local compilation against one Revit release is insufficient.
- Capability contracts are transport-neutral below the MCP adapter and must not depend on a specific LLM/client.
- `revit_get_context` is bounded by design: no model enumeration, selection enumeration, file paths, usernames, or cloud project identifiers in the base result.
- CAP-0002 must remain bounded by intentional filters, a maximum of 100 returned references, exact server-side match counting, and no per-element detail payload.
- Bridge capability compatibility is explicit, not numeric: current accepted guarantees are handshake `{1,2,3}`, get-context `{2,3}`, query-elements `{3}`. Future versions must be documented before being treated as compatible.
- A host must not advertise bridge protocol version 2 unless a functional `revit.get_context` capability service is attached; a host must not advertise version 3 unless both `revit.get_context` and `revit.query_elements` are functional.
- WebMCP must remain in scope as an emerging integration surface.
- The public repository must not contain confidential internal discussions, project information, credentials, or organization-specific sensitive details.
- Arbitrary AI-generated code execution inside Revit is not part of the normal production capability surface.
- The Revit UI strategy remains open. A future control/approval surface or richer conversational experience may be considered without changing the core capability and bridge architecture.

## Next task

After the CAP-0002 specification PR is merged, implement ADR-0006 + CAP-0002 + BRIDGE-0003 + SERVER-0002 in small reviewable slices. Do not start CAP-0003, write operations, Azure/cloud, WebMCP implementation, or UI work as part of that task.
