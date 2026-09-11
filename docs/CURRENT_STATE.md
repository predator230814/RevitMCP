# RevitMCP Current State

_Last updated: 2026-09-11_

## Phase

Implementation started / CAP-0001 and CAP-0002 are live-validated end-to-end on Revit 2026.5. CAP-0003 Revit inspection and BRIDGE-0004 protocol v4 are implemented and live-validated through the typed Bridge. SERVER-0003 registers `revit_get_elements` as the third stdio MCP tool; official-MCP-client-to-Revit live CAP-0003 validation has **not** been run in this slice.

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
- `RevitMCP.Contracts` defines transport-neutral registration, handshake, discovery-state, bridge error, CAP-0001 `GetContext`, CAP-0002 `QueryElements`, and CAP-0003 `GetElements` contracts.
- `RevitMCP.Bridge` implements user-local atomic instance registration, current-user Named Pipe hosting, `bridge.handshake` over StreamJsonRpc, and discovery that classifies candidates as `Ready`, `Unavailable`, `Incompatible`, or `Stale`.
- StreamJsonRpc `2.25.29` is used only inside `RevitMCP.Bridge`, behind RevitMCP-owned abstractions. `NamedPipeBridgeClient` bounds handshake and capability response waits locally. Handshake timeout maps to `BRIDGE_HANDSHAKE_TIMEOUT`; capability timeout maps to `REVIT_EXECUTION_TIMEOUT` and cancels the in-flight RPC so a still-queued EXEC-0001 item can be skipped without awaiting a silent-peer cancel acknowledgement.
- Autodesk Revit API binaries are not committed to the repository; builds restore version-pinned Nice3point compile-time references instead.
- Capability specifications are recorded under `docs/capabilities/` before implementation.
- CAP-0001 accepts `revit_get_context` as the first end-to-end read-only Revit capability. It returns bounded instance, active-document, active-view, and selection-count context without exposing paths or enumerating selection contents.
- CAP-0002 accepts `revit_query_elements` as the second read-only capability. It requires intentional scope plus at least one bounded filter and returns exact `matched_count`, `truncated`, and at most 100 opaque `element_ref` values rather than element bodies.
- CAP-0003 accepts `revit_get_elements` as the next read-only capability. It requires the current `document_id`, inspects 1..10 known opaque `element_ref` values, and returns only explicitly projected basic fields and named visible parameters with item-level partial success.
- Bridge specifications are recorded under `docs/bridge/` when accepted ADRs require concrete versioned technical contracts.
- BRIDGE-0001 defines the stable `bridge.handshake` bootstrap contract, identity validation, and integer bridge-protocol version negotiation. The handshake uses cached add-in/process metadata and must not invoke `ExternalEvent` or inspect the Revit model.
- BRIDGE-0002 is accepted. It introduces bridge protocol version `2` for `revit.get_context` while keeping version `1` handshake-compatible.
- BRIDGE-0003 is accepted and implemented for CAP-0002. It introduces bridge protocol version `3`, which explicitly guarantees both `revit.get_context` and `revit.query_elements`.
- BRIDGE-0004 is accepted and implemented. Protocol version `4` adds `revit.get_elements` and explicitly preserves get-context `{2,3,4}` and query-elements `{3,4}`. Get-elements is `{4}` only. Unknown v5 remains unsupported for all three.
- SERVER-0002 is implemented. `revit_query_elements` remains eligible on a v4 host.
- SERVER-0003 is implemented for the stdio MCP surface. `tools/list` exposes exactly `revit_get_context`, `revit_query_elements`, and `revit_get_elements` with explicit registration and strict structured schemas. CAP-0003 routing requires protocol `{4}`. Official-MCP-client-to-Revit 2026.5 live CAP-0003 validation remains pending.
- Execution specifications are recorded under `docs/execution/`.
- Lifecycle specifications are recorded under `docs/lifecycle/`. LIFECYCLE-0001 is accepted.
- EXEC-0001 defines one serialized FIFO Revit execution dispatcher per Revit process, backed by one long-lived `ExternalEvent`, asynchronous completion, queued cancellation, non-destructive timeout semantics, failure isolation, and explicit transaction ownership outside the dispatcher.
- The EXEC-0001 queue/state machine is implemented in `src/RevitMCP.Addin/Execution/`. `RevitExecutionQueue<TContext>` is independently testable through `IRevitEventSignal`. `RevitExecutionDispatcher` owns one long-lived `ExternalEvent` / `IExternalEventHandler` pair.
- LIFECYCLE-0001 is accepted. `RevitMcpApplication` implements `IExternalApplication`, generates one process-lifetime `instance_id` in `OnStartup`, and bootstraps on the first eligible `Idling` callback: runtime metadata, EXEC-0001 dispatcher, capability service, Named Pipe listener ready, then ADR-0003 registration. Registration-first bridge teardown is implemented.
- The Addin project sets `CopyLocalLockFileAssemblies` so Revit 2025/2026/2027 plugin output includes the Bridge NuGet runtime graph (`StreamJsonRpc.dll` and its resolved dependencies). Nice3point Revit API assemblies remain compile-time only and are still asserted absent from output.
- Compile-time Revit API references are the version-pinned Nice3point packages: `Nice3point.Revit.Api.RevitAPI` / `RevitAPIUI` `2025.4.60` (Revit 2025), `2026.4.10` (Revit 2026), and `2027.2.0` (Revit 2027). Those assemblies are compile-time only and must not be copied into add-in output.
- `tests/RevitMCP.Addin.Tests` covers EXEC-0001 queue behavior, LIFECYCLE-0001 coordination, ADR-0006 open-document identity bookkeeping, CAP-0002 request/filter matching, CAP-0003 pure request validation/projection/value bounding, and Closing/Closed identity cleanup without launching Revit.
- The initial MCP transport is `stdio`. `RevitMCP.Server` hosts a real stdio MCP process using official `ModelContextProtocol` `2.2.0`. Streamable HTTP, MCP Apps, WebMCP, Azure/cloud gateways, and other remote deployment paths remain extensions rather than core dependencies.
- Product UI considerations are recorded separately; universal access, conversational use inside or adjacent to Revit, and reduced context switching remain open product goals rather than settled architecture.

## CAP-0001 / SERVER-0001 implementation status

CAP-0001 is implemented end-to-end for the accepted base contract. Active-project live validation on Revit 2026.5 is **PASS**, including a later Bridge v3 host regression. Family-document, live Revit 2025, live Revit 2027, and live multi-instance routing remain pending compatibility validation.

### Implemented

- Transport-neutral `GetContextRequest` / `GetContextResult` contracts in `RevitMCP.Contracts`.
- Explicit `null` serialization for absent `document` and `active_view` without changing the global `ContractJson` ignore policy.
- Current implemented Bridge protocol model `SupportedVersions = [4, 3, 2, 1]`, `CurrentVersion = 4`. Context+query hosts still advertise `[3, 2, 1]`. CAP-0001-only hosts still advertise `[2, 1]`. Handshake-only hosts still advertise `[1]`. Incomplete non-prefix combinations never advertise a version they cannot satisfy.
- `IRevitCapabilityService`, `IRevitQueryElementsService`, and `IRevitGetElementsService` are composed explicitly beside `BridgeHandshakeService` in `NamedPipeBridgeHost`.
- JSON-RPC methods `revit.get_context`, `revit.query_elements`, and `revit.get_elements` on a small composite StreamJsonRpc adapter. Each accepted connection adapter stores the selected protocol after a successful handshake and rejects capability methods locally before invoking the service when handshake is absent or explicit capability support is false.
- Typed `NamedPipeBridgeClient.GetContextAsync` / `QueryElementsAsync` / `GetElementsAsync` with explicit capability timeout, local protocol gating (never `>=`), and RPC cancellation on local timeout so queued EXEC-0001 work is not started after the caller has already timed out.
- Addin `RevitGetContextService`, `RevitQueryElementsService`, and `RevitGetElementsService` dispatch through the process-lifetime EXEC-0001 dispatcher. Query and get-elements share one `OpenDocumentIdentityService`.
- Lifecycle wiring: metadata -> dispatcher -> get-context + query + get-elements services + document-close cleanup -> bridge start. Shutdown order remains dispatcher.Stop -> registration withdrawal/bridge -> cleanup unsubscribe / dispatcher dispose.
- SERVER-0001: `RevitMCP.Server` is a real stdio MCP process using official `ModelContextProtocol` `2.2.0` only inside the Server boundary.
- `tools/list` currently exposes exactly three Revit tools: `revit_get_context`, `revit_query_elements`, and `revit_get_elements`.
- Fresh current-session discovery, deterministic 0/1/many routing, and a fresh typed bridge invocation (`handshake [4,3,2,1]`, require selected protocol explicitly in `{2,3,4}` for get-context, `{3,4}` for query, and `{4}` for get-elements) on every MCP capability call.
- Modern MCP success uses authoritative `structuredContent` with empty `content`. Errors use `isError: true` and one compact JSON text block without violating success `outputSchema`.

### Automated-tested

- Contracts: empty request, snake_case, project/family kinds, string `element_id`, explicit null document/view, selection `count` only, no selected IDs/paths/user/cloud/property bags, round-trip, exact accepted field set. CAP-0002 query contracts: exact snake_case and accepted request/filter/result fields, opaque `document_id` / `element_ref` strings without GUID/UUID schema, both scopes, zero and bounded refs, no per-element metadata. CAP-0003 contracts: snake_case request/result/parameter fields, no transport-neutral `instance_id`, opaque document/ref strings, field/status/source enums, `not_found` two-property shape, basic-field absent/null/value tri-state round-trip, parameters omitted vs empty+false, explicit `value_text` null, `INVALID_INSPECTION`.
- Bridge: `[4,3,2,1]+[4,3,2,1] -> 4`, context+query host still `[3,2,1]`, CAP-0001-only host still `[2,1]`, handshake-only `[1]`, get-elements-only / query+get-elements without context stay `[1]`, context+get-elements without query stays `[2,1]`, get-context allowed after `{2,3,4}`, query after `{3,4}`, get-elements after `{4}` only, unknown v5 unsupported, Named Pipe get-elements round-trip including `not_found` / tri-state / parameters, capability errors survive StreamJsonRpc, timeout / remote-token cancel / caller cancellation / unusable-after-timeout. Endpoint adapter gating: get-elements before handshake and after v1/v2/v3/v5 does not invoke the service, including raw StreamJsonRpc peers. Inherited get-context/query remain green on v4.
- Addin/lifecycle: existing EXEC-0001 and LIFECYCLE-0001 tests, get-context + query + get-elements created before bridge start and passed to Bridge, handshake-only services remain nullable, incomplete fake compositions advertise only their valid protocol prefix, shared identity is constructed once, no invalid v4 registration on startup failure. CAP-0002 request/filter tests remain green after extracting one Addin-internal basic-metadata helper. CAP-0003 pure tests remain green. No fake Autodesk Revit API extraction coverage.
- Server: 0/1/many routing remains deterministic for all three tools, get-context eligibility is explicitly `{2,3,4}`, query eligibility is `{3,4}`, get-elements eligibility is `{4}`, v1/v2/v3/unknown v5 are ineligible for get-elements, v4 handshake followed by `GetContextAsync` / `QueryElementsAsync` / `GetElementsAsync` succeeds in Server tests, process-level stdio `tools/list` exposes exactly the three accepted tools, advertised closed input schemas are enforced at the MCP tool boundary before discovery/Bridge, unexpected CAP-0003 properties and missing/null/uninterpretable required CAP-0003 fields return `INVALID_REQUEST` without a Revit instance, no Autodesk Revit API / AspNetCore MCP package.
- Solution tests: Contracts 66, Bridge 102, Addin 118, Server 162. All passed.
- Server Release `net10.0` build succeeded.
- Addin Release builds: Revit 2025 `net8.0-windows`, Revit 2026 `net8.0-windows`, Revit 2027 `net10.0-windows`. Each output has `StreamJsonRpc.dll` and `Nerdbank.Streams.dll` present; `RevitAPI.dll` and `RevitAPIUI.dll` absent.

### Live-tested on Autodesk Revit 2026.5 (`26.5.0.55`)

Zero-document Home state, no model open, one eligible instance, registration `bridge_protocol_version = 2`:

- Official `ModelContextProtocol` `2.2.0` client launched the actual Release `RevitMCP.Server` over stdio. The Server process was not restarted between calls.
- `tools/list` exposed exactly `revit_get_context` at that historical validation point.
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

Official `ModelContextProtocol` `2.2.0` client (`McpClient` / `StdioClientTransport`) launched the actual Release `RevitMCP.Server` over stdio. `tools/list` exposed exactly `revit_get_context` at that historical validation point. Omitted `instance_id` auto-selected the single eligible instance. Success: `isError = false`, `content = []`, `structuredContent` present.

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

## CAP-0002 implementation status

CAP-0002 `revit_query_elements` is implemented end-to-end.

```text
CAP-0002 revit_query_elements: implemented end-to-end
SERVER-0002: implemented
tools/list: exactly two tools
CAP-0002 official MCP client -> Revit 2026.5: PASS
CAP-0001 regression on same v3 host: PASS
```

### Implemented

- Transport-neutral `QueryElementsRequest`, `QueryElementFilters`, `QueryElementsResult`, `QueryElementsContext`, and `QueryScope` (`document` | `active_view`) in `RevitMCP.Contracts`.
- Accepted capability error codes `NO_ACTIVE_DOCUMENT`, `DOCUMENT_CONTEXT_CHANGED`, `NO_ACTIVE_VIEW`, and `INVALID_QUERY`, preserving `REVIT_EXECUTION_TIMEOUT` / `REVIT_EXECUTION_FAILED`.
- Addin-owned `OpenDocumentIdentityService` / `OpenDocumentIdentityMap<TKey>`: same live key yields the same opaque `document_id`; a different key yields a different id; ids are generated internally and are not derived from title, path, cloud identity, username, or process id; no model write or Extensible Storage.
- Equality-aware dictionary with strong references. Successful document close forgets the mapping through two-phase `DocumentClosing` / `DocumentClosed` correlation (`RevitAPIEventStatus.Succeeded` only). Cancelled, failed, or unknown Autodesk close statuses keep the id. Autodesk `DocumentId` on those events is a temporary pair key only, not a RevitMCP `document_id`.
- `RevitQueryElementsService` validates the request off-thread, then executes through EXEC-0001: active document, document_id guard, scope collector, `WhereElementIsNotElementType()`, ordinal-insensitive filters, exact `matched_count`, `Element.UniqueId` refs, ordinal sort, limit, `truncated`.
- Direct level matching uses only `element.LevelId` -> `Level.Name`. No geometry/host/room inference.
- Typed `NamedPipeBridgeClient.QueryElementsAsync` gated to protocol `{3,4}`.
- SERVER-0002 MCP tool `revit_query_elements` is registered explicitly beside `revit_get_context`. Agent input maps to transport-neutral `QueryElementsRequest` (`instance_id` stays a Server routing field). Query routing uses `ResolveForQueryElements` with the same opaque-id / 0/1/many algorithm as CAP-0001 and explicit current `{3,4}` eligibility.
- MCP tool input is closed at runtime, not only in advertised schemas. Unexpected top-level names, `instance_id` / `document_id` typos, and unexpected nested `filters` properties return `INVALID_REQUEST` (`isError: true`, compact `{code,message}`) before discovery or Bridge work. This is a Server MCP-boundary code, not CAP-0002 `INVALID_QUERY`.

Accepted CAP-0002 v1 design:

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
- Current implemented BRIDGE-0004 protocol v4 guarantees `revit.get_context`, `revit.query_elements`, and `revit.get_elements`; get-context support is `{2,3,4}`, query-elements support is `{3,4}`, get-elements support is `{4}`.
- SERVER-0002 extends the existing stdio host to exactly two MCP tools while preserving fresh discovery, typed Bridge calls, strict structured output, and empty modern text content.

### Live-tested typed Bridge query on Autodesk Revit 2026.5 (`26.5.0.55`)

Autodesk-provided modeled sample **Snowdon Towers Sample HVAC** (`rme_advanced_sample_project.rvt` was not installed locally). No production/client model. No `.rvt` committed. No save back into the source sample.

```text
registration.bridge_protocol_version = 3
handshake [3,2,1] -> selected 3
GetContextAsync on that v3 connection -> success
QueryElementsAsync -> success
CAP-0001 MCP on the v3 host -> PASS
```

Observed document-scope evidence (`scope=document`, category `Mechanical Equipment`): `matched_count = 37`, omitted `document_id` returned a non-empty opaque id, explicit retry returned the same id and the same ordinal refs. Zero-match unknown category: count 0 / empty refs / `truncated=false`. Multi-dimension: `Air Terminals` AND level `L2` = 37; `Air Terminals` AND `text_contains=Supply` = 332. `text_contains=Supply` = 419; `text_contains=Diffuser` = 35. Truncation: same filters with `limit=1` kept `matched_count=37`, one ref, `truncated=true`. UTF-8 Bridge result sizes: normal 37-ref payload **2215** bytes; truncated payload **270** bytes; zero-match **213** bytes.

`scope=active_view` on floor plan `L2` (`FloorPlan`) returned **9** Mechanical Equipment refs, a strict subset of the 37 document-scope matches. Cover-sheet iteration did not widen to document scope.

Invalid empty filters over the real Bridge returned `INVALID_QUERY`. Activating disposable `Project1` in the same Revit process with the previous HVAC `document_id` returned `DOCUMENT_CONTEXT_CHANGED` and did not query Project1. Switching back to still-open HVAC reused the same `document_id`. Closing HVAC without saving and reopening the same Autodesk sample assigned a new `document_id`; the previous id then returned `DOCUMENT_CONTEXT_CHANGED`.

Official `ModelContextProtocol` `2.2.0` `McpClient` / `StdioClientTransport` launched Release `RevitMCP.Server.exe` against that v3 host and called `revit_query_elements`:

```text
tools/list count = 2
revit_get_context
revit_query_elements
```

Observed MCP document-scope Mechanical Equipment: `matched_count = 37`, `structuredContent` **1949** UTF-8 bytes, `content` **0**. Explicit `document_id` retry returned the same id and the same 37 refs. Truncation `limit=1`: `matched_count = 37`, one ref, `truncated=true`, **220** bytes. Zero-match unknown category: count 0 / empty refs / `truncated=false` / **173** bytes / `isError=false`. Multi-filter `Air Terminals` AND `L2`: `matched_count = 37`, **1949** bytes.

Cover Sheet `active_view` returned **0** Mechanical Equipment refs (did not widen). Floor plan `L2` (`FloorPlan`) returned **9** refs, a subset of the 37, **604** bytes.

Activating disposable `Project1` and querying with the HVAC `document_id` returned `DOCUMENT_CONTEXT_CHANGED` (`isError=true`, no structuredContent, one compact JSON text block). CAP-0001 `revit_get_context` on the same Server/v3 host remained success with empty `content` (HVAC Cover Sheet **383** bytes; HVAC L2 **368** bytes; Project1 **364** bytes).

Forbidden fields (paths, username, cloud ids, PID, pipe, session, per-element names/ids/parameters/geometry/diagnostics) were absent from successful query structured content.

CAP-0002 query collection does not create a Revit `Transaction`, `SubTransaction`, or `TransactionGroup`.

## CAP-0003 implementation status

```text
CAP-0003 contracts/shaping: implemented
CAP-0003 Revit element resolution: implemented
CAP-0003 visible parameter extraction: implemented
BRIDGE-0004 runtime/v4: implemented
typed Bridge CAP-0003 live Revit 2026.5: PASS

SERVER-0003 MCP tool: implemented (automated)
CAP-0003 end-to-end MCP live Revit: NOT RUN

current runtime Bridge: v4
current MCP tools: 3
```

CAP-0003 Revit inspection is implemented through the typed local Bridge. SERVER-0003 now registers `revit_get_elements` as the third stdio MCP tool. Official-MCP-client-to-Revit live CAP-0003 validation has not been run in this slice.

Accepted v1 design:

- read-only inspection of known opaque refs in the active document;
- `document_id` is required as an ADR-0006 active-document guard;
- input contains 1..10 unique `element_ref` values and result item order preserves request order;
- explicit projection is required;
- basic fields are limited to `name`, `category_name`, `family_name`, `type_name`, `level_name`;
- requested fields are present with `null` when unavailable; unrequested fields are omitted;
- optional `parameter_names` accepts 1..10 unique Revit display names, matched `OrdinalIgnoreCase` without trimming;
- visible parameters are inspected on the target element (`source=instance`) and resolved type (`source=type`);
- duplicate visible parameters with the same display name are retained rather than silently choosing one;
- parameter results expose only name/source/bounded `value_text`/value truncation, not parameter ids/GUIDs or raw storage values;
- at most 20 parameter entries per element and 512 characters per non-null `value_text`;
- missing/deleted/unresolvable refs are item-level `status=not_found`, allowing partial batch success;
- no all-parameters mode, geometry, connectors, parameter write identity, or raw internal-unit double output;
- all Revit work must use EXEC-0001 and create no transaction.

Accepted BRIDGE-0004 design:

```text
protocol 1 -> handshake
protocol 2 -> + get_context
protocol 3 -> + query_elements
protocol 4 -> + get_elements
```

Explicit accepted capability sets after BRIDGE-0004 implementation:

```text
get_context    = {2,3,4}
query_elements = {3,4}
get_elements   = {4}
```

A host may advertise `[4,3,2,1]` only when get-context + query-elements + get-elements are all functional.

Accepted SERVER-0003 design adds exactly one third MCP tool, `revit_get_elements`, through the existing stdio Server. It preserves fresh routing, strict MCP closed-input enforcement, authoritative structured output with empty modern `content`, and explicit v4 capability gating. The MCP tool is implemented; live Revit validation of that MCP path is still pending.

### Implemented in this slice

- Transport-neutral CAP-0003 contracts and Addin-local pure validation/projection/value bounding from the previous slice remain unchanged.
- One small Addin-internal `ElementBasicMetadataResolver` shared by CAP-0002 filters and CAP-0003 basic fields (`element.Name`, `Category?.Name`, `GetTypeId()` / `ElementType.FamilyName` / `ElementType.Name`, `LevelId` / `Level.Name`) with the same per-execution type/level caches. CAP-0002 observable semantics are unchanged.
- `RevitGetElementsService : IRevitGetElementsService` validates off-thread, then executes through EXEC-0001: active document, ordinal `document_id` guard, `Document.GetElement(requestedRef)` without parsing, `ElementType` -> item-level `not_found`, exact requested-ref echo, visible `GetOrderedParameters()` instance/type matching before value formatting, and the existing pure shaper.
- Addin-local `RevitParameterValueReader`: `HasValue == false` -> null; String `AsString()`; Integer prefer `AsValueString()` else invariant `AsInteger()`; Double only `AsValueString()` else null (never `AsDouble()`); ElementId referenced name or null (never numeric id).
- Bridge protocol v4 advertisement/gating and typed `NamedPipeBridgeClient.GetElementsAsync`.
- Existing Server tools understand inherited v4 via `BridgeProtocol.SupportsGetContext` / `SupportsQueryElements`.
- SERVER-0003: explicit `GetElementsApplicationService`, `ResolveForGetElements` / `IsGetElementsEligible`, closed CAP-0003 MCP schemas, and `revit_get_elements` registration beside the existing two tools. `instance_id` remains Server routing state and does not enter `GetElementsRequest`.

### Live-tested typed Bridge inspection on Autodesk Revit 2026.5 (`26.5.0.55`)

Autodesk-provided modeled sample **Snowdon Towers Sample HVAC**. Temporary local copy only. No production/client model. No `.rvt` committed. No save back into the source sample.

```text
registration.bridge_protocol_version = 4
handshake [4,3,2,1] -> selected 4
GetContextAsync on that v4 connection -> success (Snowdon Towers Sample HVAC, Cover Sheet)
QueryElementsAsync on that v4 connection -> success (Mechanical Equipment matched_count = 37)
GetElementsAsync -> success
```

Basic projection of one Mechanical Equipment ref (`163dfb52-e8ff-4ce3-8c1c-c35b84917839-0016579f`) requested all five fields: one `ok` item, exact requested ref echo, same `instance_id` / `document_id`, `name = Heat Recovery Unit (HRU)`, `category_name = Mechanical Equipment`, `family_name = HeatRecoveryUnit`, `type_name = Heat Recovery Unit (HRU)`, `level_name = L4`. UTF-8 Bridge payload **482** bytes. No numeric ElementId.

Subset `fields = ["name"]` returned only `element_ref`, `status`, and `name`. Category/family/type/level and parameter fields were omitted. UTF-8 **317** bytes.

Partial batch `[valid-ref, "not-a-revit-element-ref"]`: item 1 `ok`, item 2 `not_found`, request order preserved, whole call successful. UTF-8 **410** bytes.

Observed visible parameters (display names from the Autodesk sample):

- instance `Mark` on the HRU, requested as `mark` (OrdinalIgnoreCase): returned name `Mark`, `value_text = HRU409`;
- type `Type Mark` on the HRU: present with explicit `value_text = null`;
- nonexistent `NotARealParameterName`: no entries;
- instance `Flow` on Supply Air Terminal `48x4 1/4-8 In Inlet 2-Slot`: `100 CFM` (formatted Revit text, not a raw internal-unit double);
- type `Width` on Supply Air Terminal `4" DIA`: `0' - 6"`;
- instance `Schedule Level` on that air terminal: `L2` / `L1 - Block 37` (referenced element name, never a numeric ElementId).

Populated parameter payloads: Mark/Type Mark **692** bytes; Flow/Manufacturer batch **2309** bytes; Width/Model batch **2450** bytes.

Document guard: `GetElementsAsync` with an explicit non-matching `document_id` while HVAC remained active returned top-level `DOCUMENT_CONTEXT_CHANGED` and did not inspect items. No fallback. A subsequent inspection with the live HVAC `document_id` (`6dd3fbcc-5cbc-4d3c-8785-fc989993ec95`) continued to succeed. Disposable Project1 activation was not required for this guard proof.

Official `ModelContextProtocol` `2.2.0` `McpClient` / `StdioClientTransport` launched Release `RevitMCP.Server.exe` against that v4 host:

```text
tools/list count = 2
revit_get_context
revit_query_elements
revit_get_elements NOT present
```

`revit_get_context` success: `content = []`, structuredContent **383** bytes. `revit_query_elements` Mechanical Equipment: `matched_count = 37`, `content = []`, structuredContent **1949** bytes. Truncation and zero-match CAP-0002 paths remained green.

Forbidden CAP-0003 fields (paths, username, cloud ids, PID, pipe, session, numeric element/parameter ids, GUIDs, raw doubles, geometry, bounding boxes, connectors, stack traces) were absent from successful Bridge inspection payloads.

CAP-0003 inspection creates no Revit `Transaction`, `SubTransaction`, or `TransactionGroup`.

## What does not exist yet

- no official-MCP-client-to-Revit 2026.5 live CAP-0003 validation;
- no full/all-parameter element dump or parameter-name discovery mode;
- no language-independent parameter identity suitable for writes;
- no machine-readable quantity/unit contract for raw numeric parameter analytics;
- no live CAP-0001 family-document validation;
- no live lifecycle/handshake/capability validation on Revit 2025 or Revit 2027;
- no live multi-instance routing validation;
- no persistent cross-session document identity/addressing model;
- no request scheduling/fairness policy for multiple clients beyond FIFO serialization required by EXEC-0001;
- no write-locking or transaction concurrency policy;
- no deployment or packaging model;
- no finalized user-facing Revit UI strategy.

## Current priorities

1. After Tech Lead review of SERVER-0003, run official-MCP-client-to-Revit 2026.5 CAP-0003 live validation. Do not begin CAP-0004 or writes until review.
2. Keep family-document, live Revit 2025, live Revit 2027, and live multi-instance routing as pending compatibility validations rather than CAP-0003 blockers.
3. Do not expand into writes, Azure/cloud, WebMCP implementation, MCP Apps implementation, or UI work during CAP-0003.

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
- Cross-call element handles for query/inspection capabilities use opaque `element_ref` semantics based initially on Revit `Element.UniqueId`; numeric `ElementId` must not become the durable chaining contract.
- CAP-0001 `active_view.element_id` remains unchanged because it describes current context rather than a durable element handle.
- Revit-version-specific API differences should be confined to a compatibility boundary rather than scattered throughout capability or MCP-facing code.
- Autodesk Revit 2026.5 (`26.5.0.55`) preloads a .NET 10 generation host and RevitAPI `26.5.0.0` while the current 2026 add-in remains `net8.0-windows` compiled against Nice3point `2026.4.10`. The add-in loaded and completed handshake plus ExternalEvent-backed capabilities on that host; this is recorded compatibility evidence, not authorization to change the accepted TFM/API matrix.
- Cross-version compatibility requires all supported Revit add-in variants to compile in CI; local compilation against one Revit release is insufficient.
- Capability contracts are transport-neutral below the MCP adapter and must not depend on a specific LLM/client.
- `revit_get_context` is bounded by design: no model enumeration, selection enumeration, file paths, usernames, or cloud project identifiers in the base result.
- CAP-0002 remains bounded by intentional filters, a maximum of 100 returned references, exact server-side match counting, and no per-element detail payload.
- CAP-0003 is bounded by at most 10 inspected refs, explicit field/parameter projection, at most 20 returned parameter entries per element, and 512-character parameter display values. It has no all-parameters mode.
- Parameter display-name lookup in CAP-0003 is intentionally a read-oriented localized convenience. It does not establish a stable language-independent/write-safe parameter identity.
- CAP-0003 must not expose raw Revit internal-unit doubles as if they were portable quantities; machine-readable quantity/unit semantics require a separate accepted design.
- Bridge capability compatibility is explicit, not numeric. Current implementation is handshake `{1,2,3,4}`, get-context `{2,3,4}`, query-elements `{3,4}`, get-elements `{4}`. Unknown v5 is unsupported until explicitly documented.
- A host must not advertise bridge protocol version 2 unless a functional `revit.get_context` capability service is attached; version 3 requires get-context + query-elements; accepted version 4 requires get-context + query-elements + get-elements.
- WebMCP must remain in scope as an emerging integration surface.
- The public repository must not contain confidential internal discussions, project information, credentials, or organization-specific sensitive details.
- Arbitrary AI-generated code execution inside Revit is not part of the normal production capability surface.
- The Revit UI strategy remains open. A future control/approval surface or richer conversational experience may be considered without changing the core capability and bridge architecture.

## Next task

The next slice after Tech Lead review of SERVER-0003 is official-MCP-client-to-Revit 2026.5 CAP-0003 live validation. Do not start CAP-0004, write operations, Azure/cloud, WebMCP implementation, MCP Apps implementation, or UI work until review.
