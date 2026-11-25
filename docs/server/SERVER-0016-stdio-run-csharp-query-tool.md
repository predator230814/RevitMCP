# SERVER-0016: Run bounded C# query

- Status: Implemented. The official MCP-client live gate has not been run.
- Date: 2026-11-25
- Capability: [CAP-0020](../capabilities/CAP-0020-bounded-generated-csharp-read.md)
- Bridge: [BRIDGE-0022](../bridge/BRIDGE-0022-bounded-csharp-query-rpc.md)

## Tool

`revit_run_csharp_query`

Title: `Run bounded Revit C# query`

Hints: `readOnlyHint=true`, `openWorldHint=false`. Structured output is enabled.

This is a separate MCP tool. It is not registered under `revit_read`. The BIM-IT Assistant model tools do not expose it.

The description tells the agent to prefer existing primitives or `revit_lookup` and `revit_read`, to use `revit_api.symbols` only when a signature is uncertain, to take `document_id` from `revit_get_context`, and that the local Revit opt-in must be enabled. The source sees `doc`, `elementRef`, and the fixed usings. The tool is not a general-purpose script runner and does not authorize writes.

## Input

Closed object. `additionalProperties` is false. Required: `document_id`, `source`. Optional: `instance_id`.

`document_id` and `source` are strings of at least 1 character. `source` is at most 8,000 characters. `instance_id` is a string or null of at least 1 character.

Unknown MCP fields return `INVALID_REQUEST` before routing. The request does not accept references, usings, options, a compiler path, an API path, an assembly, or IL.

`instance_id` is Server routing only. The Bridge request is `document_id` and `source`.

## Output

The success value is the Bridge `RunCSharpQueryResult`. `result` stays the host-owned JSON value and is not wrapped. Its schema is unconstrained. The other properties are closed: 64 lowercase hex characters for `source_sha256` and `cache_key`, `policy_version` const `1`, `revit_api.name` const `RevitAPI`, `revit_api.version` const `26.5.0.0`, `compile_outcome` const `success`, `duration_ms` integer >= 0, and `result_characters` integer 0 through 16,000.

## Routing

Omitted `instance_id` selects only a Ready instance whose negotiated protocol satisfies `BridgeProtocol.SupportsRunCSharpQuery`. Zero eligible instances return `NO_REVIT_INSTANCE`. One is selected. More than one returns `INSTANCE_REQUIRED` with candidates.

An explicit `instance_id` for a Ready host that supports context discovery still reaches handshake, including protocol 25. A missing id returns `INSTANCE_NOT_FOUND`. The Server does not select a different instance. A protocol 25 handshake returns `BRIDGE_PROTOCOL_INCOMPATIBLE`. A registration and handshake identity mismatch returns `INSTANCE_UNAVAILABLE`.

## Timeout

`revit_run_csharp_query` uses `RunCSharpQueryTimeout` of 40 seconds. The other tools keep the 15-second capability timeout. The 40 seconds lets the CompilerHost 30-second bound win in an ordinary compiler-timeout case. A request that exceeds the outer bound follows the existing Bridge capability timeout behavior and can leave that Bridge client connection unusable. This timeout does not stop a blocking Revit API call.

## Errors

The Server passes these codes through: `CSHARP_QUERY_DISABLED`, `INVALID_CSHARP_QUERY`, `CSHARP_QUERY_API_UNSUPPORTED`, `CSHARP_QUERY_POLICY_REJECTED`, `CSHARP_QUERY_COMPILE_FAILED`, `CSHARP_QUERY_COMPILER_FAILED`, `CSHARP_QUERY_DOCUMENT_NOT_READY`, `CSHARP_QUERY_BUDGET_EXCEEDED`, `CSHARP_QUERY_EXECUTION_FAILED`, `CSHARP_QUERY_INVALID_RESULT`, `CSHARP_QUERY_RESULT_TOO_LARGE`, `BRIDGE_PROTOCOL_INCOMPATIBLE`, and `REVIT_EXECUTION_TIMEOUT`.

CAP-0020 details are reshaped as `RunCSharpQueryErrorDetails` before they reach the MCP client. Unknown fields are stripped. Diagnostics are limited to 8, and a diagnostic message over 180 characters or containing a carriage return or line feed is dropped. A source hash must be 64 lowercase hex characters or it is omitted. Policy version must be 1 or it is omitted. Revit API identity keeps name and version only. Compile outcome must be one of `success`, `policy_rejected`, `compile_failed`, `api_unsupported`, `identity_rejected`, or `invalid_request`. `reason` must be one of `no_active_document`, `project_document_required`, `document_mismatch`, or `document_modifiable`. Malformed details are omitted. The error code and message remain. Source text, a failure cache key, PE, a Revit API filesystem path, a stack trace, and raw exception text are not sent.

An MCP error without details still serializes `code` and `message` only.

## Catalog

Production `tools/list` contains exactly 23 tools. Bridge current version stays 26. The Server does not reference RevitAPI, the CompilerHost, `BimIt.Revit.Compiler`, or Roslyn.

## Official MCP-client live gate

This gate has not been run. Do not record it as passed until the cases below are observed.

Use the official `ModelContextProtocol` 2.2.0 `McpClient` with `StdioClientTransport`, the Release `BimIt.Revit.Server.exe` built from the PR SHA, Bridge protocol 26, the Release Addin built with `RevitVersion=2026` from that same SHA, and Revit 2026.5 build `26.5.0.55`. Open Snowdon Towers Sample Electrical with `Document.IsModified` false. Restart Revit once so that Addin build is loaded. Use one MCP client and server session for A through D. Do not restart Revit or that session between the cases.

`tools/list` must contain exactly 23 tools. `revit_run_csharp_query` is present with the title above, `readOnlyHint` true, and `openWorldHint` false. No Bridge RPC name such as `revit.run_csharp_query` appears.

Call `revit_get_context` and keep the real `document_id`. Confirm `Document.IsModified` is false.

A. `%LOCALAPPDATA%\RevitMCP\capabilities.json` is absent. Call the tool with a valid focused body. Expect `CSHARP_QUERY_DISABLED`, no result, no CompilerHost process, and an unmodified document.

B. Write exactly `{"schema_version":1,"csharp_query_enabled":true}` without a restart. Call:

```csharp
return new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().Select(level => new { element_ref = elementRef(level), name = level.Name, elevation = level.Elevation }).ToList();
```

Expect success, 11 real levels, opaque element refs, closed JSON, `policy_version` 1, `RevitAPI` `26.5.0.0`, `compile_outcome` `success`, source hash `8273b9dc32b5eafe0bfc8b2ebec269308c5501783c713ee97fc6a47943a9d387`, and cache key `9112f3250677a7a123a3edaf755afd19b6e63784241cdd60f41933d4b916369c` when the source text matches the Bridge gate. If the source text differs, record the exact source and the resulting hash. `Document.IsModified` stays false.

C. Leave the opt-in enabled. Call `return Environment.TickCount;`. Expect `CSHARP_QUERY_POLICY_REJECTED`, no result, `compile_outcome` `policy_rejected`, bounded diagnostics, no source echo, no cache key, no filesystem path, and no stack trace. The document stays unmodified.

D. Set `csharp_query_enabled` to false or delete the file. Do not restart. Repeat the valid query. Expect `CSHARP_QUERY_DISABLED`, no result, and no CompilerHost process.

Delete `capabilities.json` afterward. Confirm `Document.IsModified` is false.
