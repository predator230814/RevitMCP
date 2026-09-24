# RevitMCP Current State

_Last updated: 2026-09-24_

## Phase

CAP-0001 through CAP-0006 are implemented through Contracts/Addin/typed Bridge. CAP-0001 through CAP-0005 are live-validated end-to-end on Revit 2026.5 through the official stdio MCP client. BRIDGE-0007 / protocol v7 is implemented through the typed local Bridge. Typed Bridge live validation for CAP-0006 / BRIDGE-0007 on Revit 2026.5 is **PASS**. SERVER-0006 is implemented: `tools/list` exposes exactly six MCP tools, and CAP-0006 is reachable through the stdio MCP code path as `revit_get_mep_topology`. Official MCP-client-to-Revit live validation for SERVER-0006 / CAP-0006 is **PASS**. Typed Bridge live validation for CAP-0005 / BRIDGE-0006 on Revit 2026.5 remains **PASS**. Official MCP-client-to-Revit live validation for SERVER-0005 / CAP-0005 is **PASS**. Current MCP tools are exactly 6:

```text
revit_get_context
revit_query_elements
revit_get_elements
revit_describe_parameters
revit_get_parameter_values
revit_get_mep_topology
```

Live compatibility validation of this merged six-tool stack at SHA `5740f0ef9c73e40b471a1047231623b47a88edde` is **PASS** on Revit 2025 (`25.4.30.30`), Revit 2027 (`27.0.10.13`), concurrent multi-instance routing, and a CAP-0001 family document. No production code changed for that validation.

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
- The post-SERVER-0004 architecture checkpoint is complete. ADR-0007 is accepted: RevitMCP remains a specialized Revit capability service; Autodesk cloud capability is logically separated from the Revit-local MCP boundary; APS/ACC/Forma service topology is deferred; orchestration is external and optional.
- CAP-0005 accepts `revit_get_parameter_values` as a read-only capability: explicit `element_ref + parameter_ref` typed reads. Contracts, Addin, typed Bridge RPC, and SERVER-0005 stdio MCP exposure are implemented. Typed Bridge live validation for CAP-0005 / BRIDGE-0006 on Revit 2026.5 is **PASS**. Official MCP-client-to-Revit live validation for SERVER-0005 is **PASS**.
- The ADR-0004 solution/project skeleton is committed: `RevitMCP.sln`, `global.json`, centralized build/package props, `src/RevitMCP.Contracts`, `src/RevitMCP.Bridge`, `src/RevitMCP.Server`, one multi-version `src/RevitMCP.Addin` project, and test projects for Contracts, Bridge, Server, and Addin.
- Revit 2025, 2026, and 2027 are built from the same add-in project using an explicit `RevitVersion` build property; the initial target matrix is `net8.0-windows` for Revit 2025/2026 and `net10.0-windows` for Revit 2027.
- GitHub Actions compiles the non-Revit projects and the 2025/2026/2027 add-in matrix.
- `RevitMCP.Contracts` defines transport-neutral registration, handshake, discovery-state, bridge error, CAP-0001 `GetContext`, CAP-0002 `QueryElements`, CAP-0003 `GetElements`, CAP-0004 `DescribeParameters`, CAP-0005 `GetParameterValues`, and CAP-0006 `GetMepTopology` contracts.
- `RevitMCP.Bridge` implements user-local atomic instance registration, current-user Named Pipe hosting, `bridge.handshake` over StreamJsonRpc, and discovery that classifies candidates as `Ready`, `Unavailable`, `Incompatible`, or `Stale`.
- StreamJsonRpc `2.25.29` is used only inside `RevitMCP.Bridge`, behind RevitMCP-owned abstractions. `NamedPipeBridgeClient` bounds handshake and capability response waits locally. Handshake timeout maps to `BRIDGE_HANDSHAKE_TIMEOUT`; capability timeout maps to `REVIT_EXECUTION_TIMEOUT` and cancels the in-flight RPC so a still-queued EXEC-0001 item can be skipped without awaiting a silent-peer cancel acknowledgement.
- Autodesk Revit API binaries are not committed to the repository; builds restore version-pinned Nice3point compile-time references instead.
- Capability specifications are recorded under `docs/capabilities/` before implementation.
- CAP-0001 accepts `revit_get_context` as the first end-to-end read-only Revit capability. It returns bounded instance, active-document, active-view, and selection-count context without exposing paths or enumerating selection contents.
- CAP-0002 accepts `revit_query_elements` as the second read-only capability. It requires intentional scope plus at least one bounded filter and returns exact `matched_count`, `truncated`, and at most 100 opaque `element_ref` values rather than element bodies.
- CAP-0003 accepts `revit_get_elements` as the next read-only capability. It requires the current `document_id`, inspects 1..10 known opaque `element_ref` values, and returns only explicitly projected basic fields and named visible parameters with item-level partial success.
- CAP-0004 accepts `revit_describe_parameters` as the next read-only capability. It requires the current `document_id`, discovers visible instance/type parameter definitions on 1..10 known refs, and returns opaque `parameter_ref` identity plus data-type semantics without values.
- CAP-0005 accepts `revit_get_parameter_values` as the next read-only capability. It reads typed values for 1..50 explicit `element_ref + parameter_ref` pairs. Addin, typed Bridge, and SERVER-0005 stdio MCP implementation exist. Official MCP-client-to-Revit live validation on Revit 2026.5 is **PASS**.
- Bridge specifications are recorded under `docs/bridge/` when accepted ADRs require concrete versioned technical contracts.
- BRIDGE-0001 defines the stable `bridge.handshake` bootstrap contract, identity validation, and integer bridge-protocol version negotiation. The handshake uses cached add-in/process metadata and must not invoke `ExternalEvent` or inspect the Revit model.
- BRIDGE-0002 is accepted. It introduces bridge protocol version `2` for `revit.get_context` while keeping version `1` handshake-compatible.
- BRIDGE-0003 is accepted and implemented for CAP-0002. It introduces bridge protocol version `3`, which explicitly guarantees both `revit.get_context` and `revit.query_elements`.
- BRIDGE-0004 is accepted and implemented. Protocol version `4` adds `revit.get_elements` and explicitly preserves get-context `{2,3,4}` and query-elements `{3,4}`. Get-elements is `{4,5}` after BRIDGE-0005.
- BRIDGE-0005 is accepted and implemented. Protocol version `5` adds `revit.describe_parameters` and explicitly preserves get-context `{2,3,4,5}`, query-elements `{3,4,5}`, and get-elements `{4,5}`. Describe-parameters is `{5,6}` after BRIDGE-0006. Typed Bridge live validation on Revit 2026.5 is **PASS**.
- BRIDGE-0006 is accepted and implemented. Protocol version `6` adds `revit.get_parameter_values` and explicitly preserves get-context `{2,3,4,5,6}`, query-elements `{3,4,5,6}`, get-elements `{4,5,6}`, and describe-parameters `{5,6}`. Typed Bridge live validation for CAP-0005 / BRIDGE-0006 on Revit 2026.5 is **PASS**. SERVER-0005 is merged; official MCP live validation is **PASS**.
- CAP-0006 / BRIDGE-0007 are implemented through Contracts, Addin, and typed Bridge protocol v7. `revit.get_mep_topology` is `{7}` only. Inherited sets on v7 are get-context `{2,3,4,5,6,7}`, query-elements `{3,4,5,6,7}`, get-elements `{4,5,6,7}`, describe-parameters `{5,6,7}`, and get-parameter-values `{6,7}`. A full host advertises `[7,6,5,4,3,2,1]` only when that complete prefix is functional. Typed Bridge live Revit validation for CAP-0006 / BRIDGE-0007 on Revit 2026.5 is **PASS**. SERVER-0006 is implemented. Current MCP tools are exactly 6. Official MCP live validation of `revit_get_mep_topology` is **PASS**. Unknown v8 is unsupported.
- SERVER-0002 is implemented. `revit_query_elements` remains eligible on a v3, v4, v5, or v6 host.
- SERVER-0003 is implemented for the stdio MCP surface. CAP-0003 routing requires protocol `{4,5,6}`. Official-MCP-client-to-Revit 2026.5 live CAP-0003 validation is **PASS**.
- SERVER-0004 is implemented. `tools/list` previously exposed exactly `revit_get_context`, `revit_query_elements`, `revit_get_elements`, and `revit_describe_parameters`. CAP-0004 routing requires protocol `{5,6}`. Official-MCP-client-to-Revit 2026.5 live CAP-0004 validation is **PASS**.
- SERVER-0005 is implemented and merged. `tools/list` includes `revit_get_parameter_values`. Inherited Server routing accepts v7 for CAP-0001..CAP-0005 because Bridge protocol v7 explicitly includes them. Official MCP-client-to-Revit live validation is **PASS**. Typed Bridge live validation remains **PASS**.
- SERVER-0006 is implemented. It adds `revit_get_mep_topology` through the existing stdio Server. Routing is protocol `{7}` only. Official MCP-client-to-Revit live validation is **PASS**. Current MCP tools are exactly 6.
- Execution specifications are recorded under `docs/execution/`.
- Lifecycle specifications are recorded under `docs/lifecycle/`. LIFECYCLE-0001 is accepted.
- EXEC-0001 defines one serialized FIFO Revit execution dispatcher per Revit process, backed by one long-lived `ExternalEvent`, asynchronous completion, queued cancellation, non-destructive timeout semantics, failure isolation, and explicit transaction ownership outside the dispatcher.
- The EXEC-0001 queue/state machine is implemented in `src/RevitMCP.Addin/Execution/`. `RevitExecutionQueue<TContext>` is independently testable through `IRevitEventSignal`. `RevitExecutionDispatcher` owns one long-lived `ExternalEvent` / `IExternalEventHandler` pair.
- LIFECYCLE-0001 is accepted. `RevitMcpApplication` implements `IExternalApplication`, generates one process-lifetime `instance_id` in `OnStartup`, and bootstraps on the first eligible `Idling` callback: runtime metadata, EXEC-0001 dispatcher, capability service, Named Pipe listener ready, then ADR-0003 registration. Registration-first bridge teardown is implemented.
- The Addin project sets `CopyLocalLockFileAssemblies` so Revit 2025/2026/2027 plugin output includes the Bridge NuGet runtime graph (`StreamJsonRpc.dll` and its resolved dependencies). Nice3point Revit API assemblies remain compile-time only and are still asserted absent from output.
- Compile-time Revit API references are the version-pinned Nice3point packages: `Nice3point.Revit.Api.RevitAPI` / `RevitAPIUI` `2025.4.60` (Revit 2025), `2026.4.10` (Revit 2026), and `2027.2.0` (Revit 2027). Those assemblies are compile-time only and must not be copied into add-in output.
- `tests/RevitMCP.Addin.Tests` covers EXEC-0001 queue behavior, LIFECYCLE-0001 coordination, ADR-0006 open-document identity bookkeeping, CAP-0002 request/filter matching, CAP-0003 pure request validation/projection/value bounding, CAP-0004 request validation/identity/data-type/aggregation, and Closing/Closed identity cleanup without launching Revit.
- The initial MCP transport is `stdio`. `RevitMCP.Server` hosts a real stdio MCP process using official `ModelContextProtocol` `2.2.0`. Streamable HTTP, MCP Apps, WebMCP, Azure/cloud gateways, and other remote deployment paths remain extensions rather than core dependencies.
- Product UI considerations are recorded separately; universal access, conversational use inside or adjacent to Revit, and reduced context switching remain open product goals rather than settled architecture.

## CAP-0001 / SERVER-0001 implementation status

CAP-0001 is implemented end-to-end for the accepted base contract. Active-project live validation on Revit 2026.5 is **PASS**, including a later Bridge v3 host regression. Family-document, live Revit 2025, live Revit 2027, and live multi-instance routing are **PASS** at SHA `5740f0ef9c73e40b471a1047231623b47a88edde`.

### Implemented

- Transport-neutral `GetContextRequest` / `GetContextResult` contracts in `RevitMCP.Contracts`.
- Explicit `null` serialization for absent `document` and `active_view` without changing the global `ContractJson` ignore policy.
- Current implemented Bridge protocol model `SupportedVersions = [7, 6, 5, 4, 3, 2, 1]`, `CurrentVersion = 7`. Full CAP-0001..CAP-0006 hosts advertise `[7, 6, 5, 4, 3, 2, 1]`. CAP-0001..CAP-0005 hosts without topology still advertise `[6, 5, 4, 3, 2, 1]`. CAP-0001..CAP-0004 hosts still advertise `[5, 4, 3, 2, 1]`. Context+query+get-elements hosts still advertise `[4, 3, 2, 1]`. Context+query hosts still advertise `[3, 2, 1]`. CAP-0001-only hosts still advertise `[2, 1]`. Handshake-only hosts still advertise `[1]`. Incomplete non-prefix combinations never advertise a version they cannot satisfy.
- `IRevitCapabilityService`, `IRevitQueryElementsService`, `IRevitGetElementsService`, `IRevitDescribeParametersService`, `IRevitGetParameterValuesService`, and `IRevitGetMepTopologyService` are composed explicitly beside `BridgeHandshakeService` in `NamedPipeBridgeHost`.
- JSON-RPC methods `revit.get_context`, `revit.query_elements`, `revit.get_elements`, `revit.describe_parameters`, `revit.get_parameter_values`, and `revit.get_mep_topology` on a small composite StreamJsonRpc adapter. Each accepted connection adapter stores the selected protocol after a successful handshake and rejects capability methods locally before invoking the service when handshake is absent or explicit capability support is false.
- Typed `NamedPipeBridgeClient.GetContextAsync` / `QueryElementsAsync` / `GetElementsAsync` / `DescribeParametersAsync` / `GetParameterValuesAsync` / `GetMepTopologyAsync` with explicit capability timeout, local protocol gating (never `>=`), and RPC cancellation on local timeout so queued EXEC-0001 work is not started after the caller has already timed out.
- Addin `RevitGetContextService`, `RevitQueryElementsService`, `RevitGetElementsService`, `RevitDescribeParametersService`, and `RevitGetParameterValuesService` dispatch through the process-lifetime EXEC-0001 dispatcher. Query, get-elements, describe-parameters, and get-parameter-values share one `OpenDocumentIdentityService`. Describe-parameters and get-parameter-values share one document-lifetime `parameter_ref` map with reverse lookup; that map is forgotten on successful document close.
- Lifecycle wiring: metadata -> dispatcher -> get-context + query + get-elements + describe-parameters + get-parameter-values + get-mep-topology services + document-close cleanup -> bridge start. Shutdown order remains dispatcher.Stop -> registration withdrawal/bridge -> cleanup unsubscribe / dispatcher dispose.
- SERVER-0001: `RevitMCP.Server` is a real stdio MCP process using official `ModelContextProtocol` `2.2.0` only inside the Server boundary.
- `tools/list` currently exposes exactly six Revit tools: `revit_get_context`, `revit_query_elements`, `revit_get_elements`, `revit_describe_parameters`, `revit_get_parameter_values`, and `revit_get_mep_topology`.
- Fresh current-session discovery, deterministic 0/1/many routing, and a fresh typed bridge invocation (`handshake [7,6,5,4,3,2,1]`, require selected protocol explicitly in `{2,3,4,5,6,7}` for get-context, `{3,4,5,6,7}` for query, `{4,5,6,7}` for get-elements, `{5,6,7}` for describe-parameters, `{6,7}` for get-parameter-values, and `{7}` for get-mep-topology) on every MCP capability call. `instance_id` remains Server routing-only and does not enter transport-neutral capability requests. CAP-0005 exists through the stdio MCP code path; official MCP-client-to-Revit live validation is **PASS**. CAP-0006 is exposed as `revit_get_mep_topology`; official MCP-client-to-Revit live validation is **PASS**.
- Modern MCP success uses authoritative `structuredContent` with empty `content`. Errors use `isError: true` and one compact JSON text block without violating success `outputSchema`.

### Automated-tested

- Contracts: empty request, snake_case, project/family kinds, string `element_id`, explicit null document/view, selection `count` only, no selected IDs/paths/user/cloud/property bags, round-trip, exact accepted field set. CAP-0002 query contracts: exact snake_case and accepted request/filter/result fields, opaque `document_id` / `element_ref` strings without GUID/UUID schema, both scopes, zero and bounded refs, no per-element metadata. CAP-0003 contracts: snake_case request/result/parameter fields, no transport-neutral `instance_id`, opaque document/ref strings, field/status/source enums, `not_found` two-property shape, basic-field absent/null/value tri-state round-trip, parameters omitted vs empty+false, explicit `value_text` null, `INVALID_INSPECTION`. CAP-0004 contracts: snake_case request/result/descriptor fields, no transport-neutral `instance_id`, opaque document/element/parameter refs, identity/data-type enums, local identity omits portable fields, empty data type omits `forge_type_id`, no parameter values. CAP-0005 contracts: snake_case request/result/item fields, no transport-neutral `instance_id`, opaque refs, exact item statuses, `kind` value variants, omission of `value` when `has_value=false`, `INVALID_PARAMETER_READ`.
- Bridge: `[6,5,4,3,2,1]+[6,5,4,3,2,1] -> 6`, describe-capable host still `[5,4,3,2,1]`, get-elements-capable host still `[4,3,2,1]`, context+query host still `[3,2,1]`, CAP-0001-only host still `[2,1]`, handshake-only `[1]`, incomplete prefixes stay at the highest complete prefix, get-context allowed after `{2,3,4,5,6}`, query after `{3,4,5,6}`, get-elements after `{4,5,6}`, describe-parameters after `{5,6}`, get-parameter-values after `{6}` only, unknown v7 unsupported. Named Pipe get-parameter-values round-trip, inherited CAP-0001..CAP-0004 regressions on v6, capability errors survive StreamJsonRpc, per-item statuses remain successful results, timeout / remote-token cancel / unusable-after-timeout. Endpoint adapter and raw StreamJsonRpc gating: get-parameter-values before handshake and after v1..v5/v7 does not invoke the service.
- Addin/lifecycle: existing EXEC-0001 and LIFECYCLE-0001 tests, get-context + query + get-elements + describe-parameters + get-parameter-values created before bridge start and passed to Bridge, handshake-only services remain nullable, incomplete fake compositions advertise only their valid protocol prefix, shared document identity is constructed once, document-lifetime `parameter_ref` map is constructed once with reverse lookup and forgotten on successful close, no invalid v6 registration on startup failure. CAP-0002 request/filter tests remain green. CAP-0003 pure tests remain green. CAP-0004 pure tests remain green after shared identity-helper extraction. CAP-0005 pure tests cover request bounds, duplicate pairs, opaque empty/whitespace refs, and forward/reverse `parameter_ref` stability. No fake Autodesk Revit API value-extraction coverage.
- Server: 0/1/many routing remains deterministic for the five current tools, get-context eligibility is explicitly `{2,3,4,5,6}`, query eligibility is `{3,4,5,6}`, get-elements eligibility is `{4,5,6}`, describe-parameters eligibility is `{5,6}`, get-parameter-values eligibility is `{6}` only, v1..v5/unknown v7 are ineligible for get-parameter-values, v5/v7 get-parameter-values targets map to `INSTANCE_UNAVAILABLE`, v6 handshake followed by CAP-0005 and inherited CAP-0001..CAP-0004 eligibility remains green in Server tests, process-level stdio `tools/list` exposes exactly the five accepted tools, advertised closed input schemas are enforced at the MCP tool boundary before discovery/Bridge, unexpected CAP-0003/CAP-0004/CAP-0005 properties, missing/null/uninterpretable required fields, and semantically duplicate CAP-0005 pairs return `INVALID_REQUEST` without a Revit instance, no Autodesk Revit API / AspNetCore MCP package.
- Solution tests associated with the SERVER-0004 implementation: Contracts 83 PASS, Bridge 124 PASS, Addin 2026 142 PASS, Server 237 PASS. GitHub CI for PR #32 / Build #67 was fully green.
- Server Release `net10.0` build succeeded.
- Addin Release builds: Revit 2025 PASS (`net8.0-windows`), Revit 2026 PASS (`net8.0-windows`), Revit 2027 PASS (`net10.0-windows`). Each output has `StreamJsonRpc.dll` and `Nerdbank.Streams.dll` present; `RevitAPI.dll` and `RevitAPIUI.dll` absent.

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

### Compatibility validation

Family-document, live Revit 2025, live Revit 2027, and live multi-instance routing are **PASS** at SHA `5740f0ef9c73e40b471a1047231623b47a88edde`. Evidence is in the live compatibility section below. Automated 0/1/many coverage remains in Server tests.

## CAP-0002 implementation status

CAP-0002 `revit_query_elements` is implemented end-to-end.

```text
CAP-0002 revit_query_elements: implemented end-to-end
SERVER-0002: implemented
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
- Typed `NamedPipeBridgeClient.QueryElementsAsync` gated to protocol `{3,4,5}`.
- SERVER-0002 MCP tool `revit_query_elements` is registered explicitly beside `revit_get_context`. Agent input maps to transport-neutral `QueryElementsRequest` (`instance_id` stays a Server routing field). Query routing uses `ResolveForQueryElements` with the same opaque-id / 0/1/many algorithm as CAP-0001 and explicit current `{3,4,5}` eligibility.
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
- After BRIDGE-0006, current capability sets are get-context `{2,3,4,5,6}`, query-elements `{3,4,5,6}`, get-elements `{4,5,6}`, describe-parameters `{5,6}`, get-parameter-values `{6}`. `BridgeProtocol.SupportedVersions = [6,5,4,3,2,1]`.
- SERVER-0002 originally added the second stdio MCP tool while preserving fresh discovery, typed Bridge calls, strict structured output, and empty modern text content. SERVER-0003 and SERVER-0004 later added the third and fourth tools.

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

SERVER-0003 MCP tool: implemented
CAP-0003 official MCP-client-to-Revit 2026.5 live validation: PASS
SERVER-0003 end-to-end MCP validation: PASS
CAP-0001 and CAP-0002 regression on the same v4 host: PASS

current runtime Bridge: v5
current MCP tools: 4
```

CAP-0003 Revit inspection is implemented through the typed local Bridge and exposed as the third stdio MCP tool. Official `ModelContextProtocol` `2.2.0` `McpClient` / `StdioClientTransport` reached live Autodesk Revit 2026.5 through Release `RevitMCP.Server.exe`.

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

Accepted SERVER-0003 design adds exactly one third MCP tool, `revit_get_elements`, through the existing stdio Server. It preserves fresh routing, strict MCP closed-input enforcement, authoritative structured output with empty modern `content`, and explicit v4 capability gating. The MCP tool is implemented and live-validated on Revit 2026.5.

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

A later official-MCP-client-to-Revit 2026.5 CAP-0003 validation used the same Snowdon Towers Sample HVAC session (`bridge_protocol_version = 4`, `revit_build = 26.5.0.55`) and Release `RevitMCP.Server.exe` built from `main` `1958091`. Official `ModelContextProtocol` `2.2.0` `McpClient` / `StdioClientTransport`:

```text
tools/list count = 3
revit_get_context
revit_get_elements
revit_query_elements
```

No Bridge/internal `revit.get_*` / `revit.query_*` / handshake tools.

```text
CAP-0001 official MCP on the v4 host: PASS
CAP-0002 official MCP on the v4 host: PASS
CAP-0003 official MCP basic / parameters / mixed / partial not_found: PASS
CAP-0003 official MCP active-document switch -> DOCUMENT_CONTEXT_CHANGED: PASS
```

`revit_get_context` success: Snowdon Towers Sample HVAC, Cover Sheet, `content = []`, structuredContent **383** bytes. `revit_query_elements` Mechanical Equipment: `matched_count = 37`, `content = []`, structuredContent **1949** bytes. Two real refs were reused for inspection (`163dfb52-e8ff-4ce3-8c1c-c35b84917839-0016579f`, `163dfb52-e8ff-4ce3-8c1c-c35b84917839-001659cf`).

MCP `revit_get_elements` evidence (modern success `isError = false`, `content = []`):

- basic five-field projection of both refs: two `ok` items in request order, same instance/document ids, `name = Heat Recovery Unit (HRU)`, `category_name = Mechanical Equipment`, `family_name = HeatRecoveryUnit`, `type_name = Heat Recovery Unit (HRU)`, `level_name = L4`. UTF-8 **614** bytes. No unrequested fields.
- parameter-only `Mark` / `Type Mark`: instance `Mark` = `HRU409` / `HRU407`; type `Type Mark` present with explicit `value_text = null`. UTF-8 **698** bytes.
- mixed `fields=["name"]` + `parameter_names=["Mark"]`: only `name` plus the parameter pair. UTF-8 **608** bytes.
- partial `[valid-ref, "not-a-revit-element-ref"]`: `ok` then `not_found` (`element_ref` + `status` only). UTF-8 **307** bytes.
- Supply Air Terminal observation: instance `Flow` = `100 CFM` (formatted, not a raw double); instance `Schedule Level` = `L2` (referenced name, not a numeric ElementId); requested `Width` produced no entries on that first Supply ref. UTF-8 **425** bytes.

Document guard: MCP `revit_get_elements` with an explicit non-matching `document_id` while HVAC remained active returned top-level `DOCUMENT_CONTEXT_CHANGED` (`isError = true`, no structuredContent, one compact JSON text block). No fallback.

Active-document switch on the same official MCP path: after activating disposable `Project1` in the same Revit 2026.5 process, `revit_get_context` returned `title = Project1` (`structuredContent` **364** bytes, `content = []`). `revit_get_elements` with the previous HVAC `document_id` (`6dd3fbcc-5cbc-4d3c-8785-fc989993ec95`) and a previously real HVAC `element_ref` then returned top-level `DOCUMENT_CONTEXT_CHANGED` (`isError = true`, no structuredContent, one compact JSON text block). No retry or fallback onto Project1. Switching back to still-open HVAC reused the same `document_id`; `revit_get_context` returned Snowdon Towers Sample HVAC / Cover Sheet (**383** bytes) and `revit_get_elements` on that same HVAC ref succeeded (`status = ok`, `name = Heat Recovery Unit (HRU)`, **244** bytes).

Forbidden CAP-0003 fields (paths, username, cloud ids, PID, pipe, session, numeric element/parameter ids, GUIDs, raw doubles, geometry, bounding boxes, connectors, stack traces) were absent from successful MCP inspection payloads.

CAP-0003 inspection creates no Revit `Transaction`, `SubTransaction`, or `TransactionGroup`.

## CAP-0004 implementation status

```text
CAP-0004 contracts/shaping: COMPLETE
CAP-0004 Revit parameter discovery: COMPLETE
BRIDGE-0005 runtime/v5: COMPLETE
typed Bridge CAP-0004 live Revit 2026.5: PASS

SERVER-0004 spec: accepted
SERVER-0004 MCP tool: implemented
official MCP CAP-0004 live validation: PASS
current implemented Bridge protocol: v5
current MCP tools: 4
```

CAP-0004 Revit discovery is implemented through the typed local Bridge. BRIDGE-0005 is implemented: typed-Bridge live validation on Revit 2026.5 is **PASS**. SERVER-0004 is implemented in the existing stdio Server: explicit fourth MCP tool `revit_describe_parameters`, strict closed MCP input validation before discovery/Bridge, `instance_id` routing-only and absent from `DescribeParametersRequest`, omitted `source` maps to `both`, omitted `limit` maps to `50`, fresh discovery/client/handshake per invocation, no retry/fallback, modern success `structuredContent` with `content=[]`, compact JSON TextContent errors, read-only. Official MCP-client CAP-0004 live validation on Revit 2026.5 is **PASS**.

Accepted v1 design remains as specified in `docs/capabilities/CAP-0004-revit-describe-parameters.md`. `parameter_ref` is Addin-owned, document-scoped, and forgotten on successful document close. Identity kind uses Revit built-in / shared / local APIs rather than ForgeTypeId string parsing. Data-type kind uses `Definition.GetDataType()` plus `UnitUtils.IsMeasurableSpec`, `Category.IsBuiltInCategory`, and `SpecUtils.IsSpec`.

### Live-tested typed Bridge discovery on Autodesk Revit 2026.5 (`26.5.0.55`)

Tested SHA `238487ce72c72e6149226aa68c53715f8e3becc1`. Autodesk-provided modeled sample **Snowdon Towers Sample HVAC**. Temporary local copy only. No production/client model. No `.rvt` committed. No save back into the source sample. No writes. The live validation harness remains untracked under `tools/`.

```text
registration.bridge_protocol_version = 5
handshake [5,4,3,2,1] -> selected 5
CAP-0001 GetContextAsync regression: PASS
CAP-0002 QueryElementsAsync regression: PASS
CAP-0003 GetElementsAsync regression on v5: PASS
CAP-0004 DescribeParametersAsync through real NamedPipeBridgeClient: PASS
opaque parameter_ref stability across repeated calls: PASS
name_contains=Flow: PASS
mixed valid + invalid ref -> ok + not_found: PASS
```

Observed on Cover Sheet / two live Air Terminal refs: built-in identity with `parameter_type_id` (example `Assembly Code` / `autodesk.revit.parameter:assemblyCode-1.0.0`); measurable MEP data type (`Flow` / `autodesk.spec.aec.hvac:airFlow-2.0.0`, plus built-in `Max Flow` / `Min Flow`). Shared parameters were not observed naturally on those elements; that absence is not a failure.

### Live-tested official MCP CAP-0004 on Autodesk Revit 2026.5 (`26.5.0.55`)

Date: 2026-09-21. Tested SHA `42fcb5506e1df5ef6a559a11fa6d2f55cdd1e4c2`. Release `RevitMCP.Server` `0.1.0` (`net10.0`) through official `ModelContextProtocol` `2.2.0` `McpClient` / `StdioClientTransport`. Revit 2026 / `26.5.0.55`, Bridge protocol `5`. Autodesk-provided modeled sample **Snowdon Towers Sample HVAC**, disposable TEMP copy only. No production/client model. No `.rvt` committed. No save back into the source sample. No writes. The live validation harness remains untracked under `tools/`.

```text
official ModelContextProtocol 2.2.0 client
-> stdio RevitMCP.Server
-> MCP tools
-> Named Pipe bridge protocol v5
-> EXEC-0001
-> Revit 2026.5 API
```

```text
SERVER-0004 official MCP -> Revit 2026.5 live gate: PASS
tools/list exactly 4: PASS
CAP-0001 regression: PASS
CAP-0002 regression: PASS
CAP-0003 regression: PASS
CAP-0004 basic: PASS
parameter_ref stability while document remains open: PASS
built_in identity: PASS
measurable MEP Flow datatype: PASS
name_contains Flow: PASS
mixed ok/not_found: PASS
DOCUMENT_CONTEXT_CHANGED: PASS
malformed MCP input -> INVALID_REQUEST before discovery: PASS
modern structured success/error behavior: PASS
bounded payloads: PASS
no write / no Revit transaction: PASS
```

Representative observations:

```text
CAP2 Air Terminals: matched_count = 509, truncated = true
CAP4 basic: 2 valid refs, source omitted -> both, limit omitted -> 50,
            matched_count = 63, returned descriptors = 50, truncated = true
parameter_ref stability: 50/50 returned refs stable across a repeated request
                         in the same open document
built-in example: Assembly Code, identity.kind = built_in,
                  parameter_type_id present, guid absent
MEP datatype: Flow, identity.kind = local on this model,
              data_type.kind = measurable_spec,
              forge_type_id = autodesk.spec.aec.hvac:airFlow-2.0.0
Max Flow / Min Flow: identity.kind = built_in, data_type.kind = measurable_spec
name_contains = Flow: matched_count = 3, truncated = false,
                      all three returned names contain Flow
mixed: valid ref -> ok, fake ref -> not_found, overall MCP result remains success
wrong document: DOCUMENT_CONTEXT_CHANGED, isError = true,
                no structuredContent, one compact JSON TextContent
malformed MCP request: INVALID_REQUEST before discovery/Bridge
```

Shared parameter identity was not naturally observed in this model during this live gate. That is not a failure: automated coverage already exists, and the model was not modified to manufacture a shared parameter.

Representative live-validation UTF-8 payload sizes (not contractual SLAs):

| Call | UTF-8 bytes |
| --- | ---: |
| get_context | 383 |
| query_elements | 2573 |
| get_elements | 412 |
| describe_parameters basic / 50 descriptors | 15118 |
| describe_parameters filtered Flow | 1244 |
| describe_parameters mixed | 13455 |
| INVALID_REQUEST | 87 |
| DOCUMENT_CONTEXT_CHANGED | 108 |

Safety: disposable model copy, read-only, no Revit transaction, no parameter modification, no element creation/deletion, `is_modified=false`. CAP-0004 results exposed no parameter values, raw numeric values, numeric durable ElementId, `Parameter.Id`, formulas, geometry, connectors, or paths.

This means CAP-0004 / BRIDGE-0005 / SERVER-0004 are now end-to-end implemented and validated for the Revit 2026.5 reference environment. Live Revit 2025 and Revit 2027 compatibility validation is **PASS** and recorded in the compatibility section.

## CAP-0005 implementation status

```text
CAP-0005 contracts/Addin/typed Bridge: implemented
BRIDGE-0006 runtime/v6: implemented
typed Bridge CAP-0005 live Revit 2026.5: PASS

SERVER-0005 MCP tool: implemented
official MCP CAP-0005 live validation: PASS
current implemented Bridge protocol: v6
current MCP tools: 5
```

CAP-0005 typed value reads are implemented through the typed local Bridge as `NamedPipeBridgeClient.GetParameterValuesAsync` and through SERVER-0005 as the fifth stdio MCP tool `revit_get_parameter_values`. Official MCP-client-to-Revit live validation on Revit 2026.5 is **PASS**.

Accepted v1 design remains as specified in `docs/capabilities/CAP-0005-revit-get-parameter-values.md`. Runtime semantics are unchanged by this live-validation record. `parameter_ref` remains Addin-owned, document-scoped, and forgotten on successful document close. CAP-0005 consumes the exact minted CAP-0004 refs; it does not fall back to display-name matching.

### Live-tested typed Bridge values on Autodesk Revit 2026.5 (`26.5.0.55`)

Date: 2026-09-22. Tested SHA `12f45a5946cd502f008693e990ba43559d1c478c`. Production Addin output and real `NamedPipeBridgeClient`. Autodesk-provided modeled sample **Snowdon Towers Sample HVAC**, disposable TEMP copy only. No production/client model. No `.rvt` committed. No save back into the source sample. No writes. The live validation harness remains untracked under `tools/`.

```text
registration.bridge_protocol_version = 6
handshake [6,5,4,3,2,1] -> selected 6
CAP-0001 GetContextAsync regression: PASS
CAP-0002 QueryElementsAsync regression: PASS
CAP-0003 GetElementsAsync regression: PASS
CAP-0004 DescribeParametersAsync regression: PASS
CAP-0004 parameter_ref -> CAP-0005 GetParameterValuesAsync chaining: PASS
```

Observed typed values on naturally present Autodesk-sample parameters:

- String (Host, IfcGUID, Mark, System Classification, System Name);
- Integer (Critical Path, Export to IFC, Export Type to IFC; observed values were `0`);
- measurable Quantity, including Flow = `100` with `data_type.kind = measurable_spec`, `data_type.forge_type_id = autodesk.spec.aec.hvac:airFlow-2.0.0`, and `unit_type_id = autodesk.unit.unit:cubicFeetPerMinute-1.0.1`. Quantity payload is `{kind, value, unit_type_id}` only; no raw internal-unit double field is exposed;
- ElementId-backed references: Phase Created resolved to `New Construction` with an eligible `element_ref`; System Type resolved to `Exhaust Air` with no `element_ref` (ElementType-like target). No numeric ElementId;
- `has_value=false` (Comments, Cost, Max Flow, Min Flow, Type Image).

Observed item statuses: `parameter_ref_not_found`, `element_not_found`, and `parameter_not_present` (valid Flow instance ref on a target that does not carry that parameter).

Document guard: activating disposable `Project1` and retrying the previous HVAC `document_id` returned top-level `DOCUMENT_CONTEXT_CHANGED` with no fallback. Switching back to still-open HVAC reused the same `document_id` and the same minted Flow `parameter_ref`. After close without save and reopen of the same TEMP copy: a new `document_id` was assigned; the old `parameter_ref` returned `parameter_ref_not_found`; a newly minted Flow ref on the same UniqueId resolved to `ok` quantity 100 CFM.

Safety: no `Transaction` / `SubTransaction` / `TransactionGroup`, no parameter modification, no element creation/deletion, no save, `is_modified=false` before and after.

Naturally **not** observed in this Autodesk sample; recorded rather than manufactured:

- `unsupported_value` / unit-conversion-failure path;
- a clearly non-Boolean non-zero integer.

This means CAP-0005 / BRIDGE-0006 are implemented and live-validated through the typed local Bridge for the Revit 2026.5 reference environment. Official MCP-client-to-Revit live validation of SERVER-0005 / CAP-0005 is recorded below. Live Revit 2025 and Revit 2027 compatibility validation is **PASS** and recorded in the compatibility section.

### Live-tested official MCP SERVER-0005 on Autodesk Revit 2026.5 (`26.5.0.55`)

Date: 2026-09-22. Tested SHA `edd862bfbaaed1241e0730294d644ec30f54cff7`. Release `RevitMCP.Server` `0.1.0` (`net10.0`) through official `ModelContextProtocol` `2.2.0` `McpClient` / `StdioClientTransport`. Revit 2026 / `26.5.0.55`, Bridge protocol `6`. Autodesk-provided modeled sample **Snowdon Towers Sample HVAC**, disposable TEMP copy `RevitMCP-SERVER0005-Snowdon-HVAC.rvt` only. No production/client model. No `.rvt` committed. No save back into the source sample. No writes. The live validation harness remains untracked under `tools/`.

```text
official ModelContextProtocol 2.2.0 client
-> stdio RevitMCP.Server
-> fresh discovery/routing
-> NamedPipeBridgeClient
-> handshake Bridge v6
-> Addin
-> EXEC-0001
-> Revit 2026.5 API
```

```text
SERVER-0005 official MCP -> Revit 2026.5 live gate: PASS
tools/list exactly 5: PASS
CAP-0001 get_context: PASS
CAP-0002 query Air Terminals: PASS
CAP-0004 describe Flow: PASS
CAP-0004 parameter_ref -> CAP-0005 get values: PASS
Flow 100 CFM + unit_type_id: PASS
parameter_ref_not_found item-level success: PASS
element_not_found item-level success: PASS
mixed ok + item errors remain success: PASS
DOCUMENT_CONTEXT_CHANGED: PASS
malformed MCP input -> INVALID_REQUEST before discovery: PASS
modern structured success/error behavior: PASS
no write / no Revit transaction: PASS
```

Representative observations:

```text
registration.bridge_protocol_version = 6
revit_build = 26.5.0.55
document title = RevitMCP-SERVER0005-Snowdon-HVAC
is_modified before/after = false
CAP2 Air Terminals: matched_count = 509, truncated = true
CAP4 name_contains=Flow minted instance Flow
  parameter_ref = aea63cbb-2dfb-4c8b-8306-4ff41b894933
  data_type.kind = measurable_spec
  data_type.forge_type_id = autodesk.spec.aec.hvac:airFlow-2.0.0
CAP5 Flow: status=ok, has_value=true, value.kind=quantity,
  value.value=100,
  unit_type_id=autodesk.unit.unit:cubicFeetPerMinute-1.0.1
quantity payload is {kind, value, unit_type_id} only
no raw/internal Revit double, no numeric ElementId
unknown parameter_ref -> item parameter_ref_not_found, isError=false
unknown element_ref -> item element_not_found, isError=false
mixed batch of those three pairs remains isError=false
wrong document_id -> DOCUMENT_CONTEXT_CHANGED, isError=true,
  no structuredContent, one compact JSON TextContent
malformed extra property -> INVALID_REQUEST, not NO_REVIT_INSTANCE,
  not INVALID_PARAMETER_READ
```

Representative live-validation UTF-8 payload sizes (not contractual SLAs):

| Call | UTF-8 bytes |
| --- | ---: |
| get_context | 389 |
| query_elements | 2573 |
| describe_parameters Flow | 1166 |
| get_parameter_values Flow | 474 |
| parameter_ref_not_found | 272 |
| element_not_found | 255 |
| mixed | 743 |
| INVALID_REQUEST | 87 |
| DOCUMENT_CONTEXT_CHANGED | 108 |

Safety: disposable TEMP model copy, read-only, no `Transaction` / `SubTransaction` / `TransactionGroup`, no parameter modification, no element creation/deletion, no save, `is_modified=false` before and after.

Naturally **not** observed during this official MCP gate; recorded rather than manufactured:

- `unsupported_value` / unit-conversion-failure path.

This is already covered by automated tests. Typed Bridge live validation remains **PASS**.

### Live-tested typed Bridge topology on Autodesk Revit 2026.5 (`26.5.0.55`)

Date: 2026-09-22. Tested SHA `98f0a491601679596c4e66691007971d2b892480`. Real `NamedPipeBridgeClient` -> Bridge v7 -> Addin -> EXEC-0001 -> Revit. Autodesk-provided modeled sample **Snowdon Towers Sample HVAC**, disposable TEMP copy only. No production/client model. No `.rvt` committed. No save back into the source sample. No writes. The live validation harness remains untracked under `tools/`.

```text
NamedPipeBridgeClient
-> handshake Bridge v7, supported [7,6,5,4,3,2,1]
-> Addin
-> EXEC-0001
-> Revit 2026.5 API
```

```text
CAP-0006 / BRIDGE-0007 typed-Bridge live gate: PASS
registration.bridge_protocol_version = 7
revit_build = 26.5.0.55
handshake selected = 7
physical HVAC adjacency: PASS
multi-hop / minimum depth / canonical undirected edges: PASS
deterministic repeat: PASS
max_depth truncation: PASS
max_edges=10 truncation, no hidden nodes: PASS
unknown seed -> not_found, call success: PASS
wrong document_id -> DOCUMENT_CONTEXT_CHANGED: PASS
no write / no save: PASS
no_connectors: not naturally observed
```

Representative seed: Round Duct / Tees, category Ducts, `element_ref` `04be4628-f152-40c0-a439-566f04daea9a-001642dd`. Wide HVAC request: 20 nodes, 19 edges, max depth 6, truncated for depth. The identical request repeated with the same ordered seeds, nodes, edges, `truncated`, and `truncation_reasons`. Depth 1 kept 3 nodes and 2 edges and reported depth truncation. `max_edges=10` kept 11 nodes and 10 edges; every non-seed node still had an emitted edge to a node at depth - 1.

The first live attempt returned `ok` seeds with zero edges. A connected-reference diagnostic showed `ReferenceEquals=false` and `Document.Equals=true` for a physical HVAC mate in the same open document. `ResolveOwnerRef` now uses `EqualityComparer<Document>.Default.Equals`. The retest above is on that fix.

Safety: disposable TEMP model copy, read-only, no `Transaction` / `SubTransaction` / `TransactionGroup`, no parameter modification, no element creation/deletion, no save, `is_modified=false` before and after.

Naturally **not** observed; recorded rather than manufactured:

- `no_connectors` (the host model query for Walls matched nothing; architecture in this sample is not a host category).

### Live-tested official MCP SERVER-0006 on Autodesk Revit 2026.5 (`26.5.0.55`)

Date: 2026-09-24. Tested SHA `b7be3989b802f8cdb7a960e1a98e290ac7eb1006`. Release `RevitMCP.Server` `0.1.0` (`net10.0`) through official `ModelContextProtocol` `2.2.0` `McpClient` / `StdioClientTransport`. Revit 2026 / `26.5.0.55`, Bridge protocol `7`. Autodesk-provided modeled sample **Snowdon Towers Sample HVAC**, disposable TEMP copy `RevitMCP-SERVER0006-Snowdon-HVAC.rvt` only. No production/client model. No `.rvt` committed. No save back into the source sample. No writes. The live validation harness remains untracked under `tools/`.

```text
official ModelContextProtocol 2.2.0 client
-> stdio RevitMCP.Server
-> fresh discovery/routing
-> NamedPipeBridgeClient
-> handshake Bridge v7
-> Addin
-> EXEC-0001
-> Revit 2026.5 API
```

```text
SERVER-0006 official MCP -> Revit 2026.5 live gate: PASS
tools/list exactly 6: PASS
revit_get_context: PASS
HVAC seed through revit_query_elements: PASS
revit_get_mep_topology: PASS
physical HVAC adjacency: PASS
multi-hop depth / minimum-hop: PASS
canonical undirected edges: PASS
deterministic repeat: PASS
max_depth truncation: PASS
max_edges=10 truncation, no hidden nodes: PASS
unknown seed -> not_found, call success: PASS
wrong document_id -> DOCUMENT_CONTEXT_CHANGED: PASS
malformed MCP input -> INVALID_REQUEST: PASS
modern structured success / compact error: PASS
no write / no save: PASS
no_connectors: not naturally observed
```

Representative seed: Round Duct / Tees, category Ducts, `element_ref` `04be4628-f152-40c0-a439-566f04daea9a-001642dd`. Wide HVAC request: 20 nodes, 19 edges, max depth 6, truncated for depth. The identical MCP request repeated with the same ordered seeds, nodes, edges, `truncated`, and `truncation_reasons`. Depth 1 kept 3 nodes and 2 edges and reported depth truncation. `max_edges=10` kept 11 nodes and 10 edges; every non-seed node still had an emitted edge to a node at depth - 1. Success responses used `isError=false`, authoritative `structuredContent`, and `content=[]`. `INVALID_REQUEST` and `DOCUMENT_CONTEXT_CHANGED` used `isError=true`, no success-shaped structured content, and one compact JSON text block.

Safety: disposable TEMP model copy, read-only, no `Transaction` / `SubTransaction` / `TransactionGroup`, no parameter modification, no element creation/deletion, no save, `is_modified=false` before and after.

Naturally **not** observed; recorded rather than manufactured:

- `no_connectors` (the host model query for Walls matched nothing).

## Live compatibility validation at `5740f0e`

Date: 2026-09-24. Tested git SHA `5740f0ef9c73e40b471a1047231623b47a88edde` (`Merge pull request #43`). No production code changed. The live harness stayed untracked under `tools/`.

Client/Server path for every case:

```text
official ModelContextProtocol 2.2.0 McpClient / StdioClientTransport
-> Release RevitMCP.Server net10.0 (stdio)
-> Bridge handshake SupportedVersions [7,6,5,4,3,2,1]
-> Addin -> EXEC-0001 -> Revit
```

Each registration advertised `bridge_protocol_version = 7` before the MCP calls. `tools/list` was exactly these six names, in ordinal order, with no Bridge RPC methods:

```text
revit_describe_parameters
revit_get_context
revit_get_elements
revit_get_mep_topology
revit_get_parameter_values
revit_query_elements
```

Success responses used `isError=false`, authoritative `structuredContent`, and `content=[]`.

### Revit 2025 — PASS

Host: Autodesk Revit 2025, journal `Release: 2025.4.3`, `Build: 20250815_1515(x64)`, `FileVersion` / `revit_build` `25.4.30.30`, language `FRA`. Add-in: existing `net8.0-windows` Release `RevitMCP.Addin.dll`. Journal `AddInLoadFailureMessage: NoError`. Journal also recorded Nice3point `RevitAPI` / `RevitAPIUI` `25.4.60.0` conflicting with preloaded `25.4.30.0`; the add-in still started and served protocol 7.

Disposable project: TEMP copy titled `RevitMCP-COMPAT-2025` (source `DynamoSample_2025.rvt`). Instance `1d259165-7a9d-48fb-a3ac-c15f75668f18`.

- `revit_get_context`: `revit_version=2025`, `revit_build=25.4.30.30`, `document.kind=project`, `document.title=RevitMCP-COMPAT-2025`, `is_modified=false`.
- `revit_query_elements`: English category names Walls, Doors, Floors, Ducts, Furniture, Generic Models, Columns, and Levels each returned `matched_count=0`. `text_contains=a` returned `matched_count=3071`, 5 refs.
- Bounded `revit_get_elements`: ref `00c6c04a-12df-4dad-941f-b193ceb216ca-0000225e`, `status=ok`, `name=Phase - New`, `category_name=Matériaux`.
- After those reads, `is_modified=false` and the title was unchanged. No save.

### Revit 2027 — PASS

Host: Autodesk Revit 2027, journal `Release: 2027.0.1`, `Build: 20260330_1515(x64)`, `FileVersion` / `revit_build` `27.0.10.13`, language `ENU`. Add-in: accepted `net10.0-windows` Release `RevitMCP.Addin.dll`. Journal `AddInLoadFailureMessage: NoError`. Journal also recorded Nice3point `RevitAPI` / `RevitAPIUI` `27.2.0.0` conflicting with preloaded `27.0.10.0`; the add-in still started and served protocol 7.

Disposable project: TEMP copy titled `RevitMCP-COMPAT-2027`, opened from a copy of the 2025 Dynamo sample. The journal recorded an in-memory upgrade from Revit 2025 to Revit 2027 and the host prompt to save afterward. Disk last-modification time reported on open was `19-Dec-2024`. Instance `997fcf5f-b85e-4a43-8b18-386a4f7cd678`.

- `revit_get_context`: `revit_version=2027`, `revit_build=27.0.10.13`, `document.kind=project`, `document.title=RevitMCP-COMPAT-2027`, `is_modified=false`.
- `revit_query_elements` category Walls: `matched_count=38`, 5 refs.
- Bounded `revit_get_elements`: ref `17836a3c-e764-47fa-a2e0-08216444f621-0007882c`, `status=ok`, `name=CL_W1`, `category_name=Walls`.
- After those reads, `is_modified=false` and the title was unchanged. RevitMCP did not save the upgraded model.

### Multi-instance routing — PASS

Concurrent eligible instances: the Revit 2025 project above and the Revit 2027 project above. Both registration files were discovered.

Omitted `instance_id` on `revit_get_context` returned compact `INSTANCE_REQUIRED` (`isError=true`, no `structuredContent`) with candidates already sorted by ordinal `instance_id`:

```text
1d259165-7a9d-48fb-a3ac-c15f75668f18  2025  25.4.30.30
997fcf5f-b85e-4a43-8b18-386a4f7cd678  2027  27.0.10.13
```

Explicit `instance_id` returned that instance only: 2025 title `RevitMCP-COMPAT-2025` / build `25.4.30.30`, and 2027 title `RevitMCP-COMPAT-2027` / build `27.0.10.13`. The two document titles stayed distinct. Unknown id `missing-instance-id` returned `INSTANCE_NOT_FOUND`.

Closing the disposable Revit 2025 process removed its registration file. A later explicit call for `1d259165-7a9d-48fb-a3ac-c15f75668f18` returned `INSTANCE_NOT_FOUND`. The still-open Revit 2027 instance still returned its own title `RevitMCP-COMPAT-2027`.

Naturally **not** observed; recorded rather than manufactured:

- `INSTANCE_UNAVAILABLE` (teardown deleted the registration, so the closed id was `INSTANCE_NOT_FOUND`);
- an ineligible protocol instance (both live instances advertised protocol 7).

### CAP-0001 family document — PASS

Host: the same Revit 2025 `25.4.30.30` / `20250815_1515(x64)`. Disposable family: TEMP copy of `rac_basic_sample_family.rfa`, title `RevitMCP-COMPAT-Family`. Instance `1b662de0-ae26-44b8-9fde-670566125c57`, protocol 7.

`revit_get_context` returned `document.kind=family`, `document.title=RevitMCP-COMPAT-Family`, `revit_build=25.4.30.30`, `is_modified=false` before and after. `tools/list` remained exactly 6. The accepted CAP-0001 contract already required `kind=family` for an active family document, so the contract was not extended.

A command-line open of a copied Metric Column `.rft` exited before a family document stayed open. The `.rfa` copy is the family document that was validated.

## What does not exist yet

- no write capability or write authorization;
- no APS/ACC/Forma MCP, orchestrator, or dynamic tool-list exposure;
- no full/all-parameter element dump or whole-document parameter catalog;
- local parameter identity remains document-scoped; built-in/shared canonical identity and opaque `parameter_ref` now exist for later read/write chaining;
- official MCP-client-to-Revit live validation for SERVER-0005 / CAP-0005 on Revit 2026.5 is **PASS**; typed Bridge live validation remains **PASS**;
- CAP-0006 / BRIDGE-0007 typed Bridge live validation on Revit 2026.5 is **PASS**; SERVER-0006 official MCP live validation of `revit_get_mep_topology` is **PASS**;
- live Revit 2025, live Revit 2027, multi-instance routing, and CAP-0001 family-document validation are **PASS** at SHA `5740f0ef9c73e40b471a1047231623b47a88edde`;
- no persistent cross-session document identity/addressing model;
- no request scheduling/fairness policy for multiple clients beyond FIFO serialization required by EXEC-0001;
- no write-locking or transaction concurrency policy;
- no deployment or packaging model;
- no finalized user-facing Revit UI strategy.

## Current priorities

1. Official MCP live validation of SERVER-0006 / `revit_get_mep_topology` on Revit 2026.5 is **PASS**. `tools/list` exposes exactly six tools.
2. Live Revit 2025, live Revit 2027, multi-instance routing, and CAP-0001 family-document validation are **PASS** at SHA `5740f0ef9c73e40b471a1047231623b47a88edde`.
3. Do not begin writes, APS, orchestrator, WebMCP, MCP Apps, UI, or dynamic tool scoping.

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
- Autodesk Revit 2025 (`25.4.30.30`, build `20250815_1515(x64)`) preloads RevitAPI `25.4.30.0` while the 2025 add-in remains `net8.0-windows` compiled against Nice3point `2025.4.60`. The journal recorded that assembly conflict and `AddInLoadFailureMessage: NoError`. Handshake, `revit_get_context`, query, and bounded inspection completed on that host.
- Autodesk Revit 2027 (`27.0.10.13`, build `20260330_1515(x64)`) preloads RevitAPI `27.0.10.0` while the 2027 add-in remains `net10.0-windows` compiled against Nice3point `2027.2.0`. The journal recorded that assembly conflict and `AddInLoadFailureMessage: NoError`. Handshake, `revit_get_context`, query, and bounded inspection completed on that host.
- Cross-version compatibility requires all supported Revit add-in variants to compile in CI; local compilation against one Revit release is insufficient.
- Capability contracts are transport-neutral below the MCP adapter and must not depend on a specific LLM/client.
- `revit_get_context` is bounded by design: no model enumeration, selection enumeration, file paths, usernames, or cloud project identifiers in the base result.
- CAP-0002 remains bounded by intentional filters, a maximum of 100 returned references, exact server-side match counting, and no per-element detail payload.
- CAP-0003 is bounded by at most 10 inspected refs, explicit field/parameter projection, at most 20 returned parameter entries per element, and 512-character parameter display values. It has no all-parameters mode.
- Parameter display-name lookup in CAP-0003 is intentionally a read-oriented localized convenience. It does not establish a stable language-independent/write-safe parameter identity.
- CAP-0003 must not expose raw Revit internal-unit doubles as if they were portable quantities; machine-readable quantity/unit semantics require a separate accepted design.
- Bridge capability compatibility is explicit, not numeric. Current implementation is handshake `{1,2,3,4,5,6,7}`, get-context `{2,3,4,5,6,7}`, query-elements `{3,4,5,6,7}`, get-elements `{4,5,6,7}`, describe-parameters `{5,6,7}`, get-parameter-values `{6,7}`, get-mep-topology `{7}`. Unknown v8 is unsupported until explicitly documented.
- A host must not advertise bridge protocol version 2 unless a functional `revit.get_context` capability service is attached; version 3 requires get-context + query-elements; accepted version 4 requires get-context + query-elements + get-elements; accepted version 5 requires those plus describe-parameters; accepted version 6 requires those plus get-parameter-values; accepted version 7 requires those plus get-mep-topology.
- WebMCP must remain in scope as an emerging integration surface.
- The public repository must not contain confidential internal discussions, project information, credentials, or organization-specific sensitive details.
- Arbitrary AI-generated code execution inside Revit is not part of the normal production capability surface.
- The Revit UI strategy remains open. A future control/approval surface or richer conversational experience may be considered without changing the core capability and bridge architecture.

## Next task

Live compatibility validation of the merged six-tool stack is **PASS** at SHA `5740f0ef9c73e40b471a1047231623b47a88edde` on Revit 2025 (`25.4.30.30`), Revit 2027 (`27.0.10.13`), concurrent multi-instance routing, and a CAP-0001 family document. SERVER-0006 official MCP live validation on Revit 2026.5 remains **PASS**. Do not begin writes, APS, orchestrator, WebMCP, MCP Apps, UI, or dynamic tool scoping.
