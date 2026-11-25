# BRIDGE-0022: Bounded C# query RPC

- Status: Implemented in the Bridge and Addin. The Bridge live gate is **PASS** in [CAP_0020_CSHARP_QUERY_BRIDGE_VALIDATION.md](../CAP_0020_CSHARP_QUERY_BRIDGE_VALIDATION.md). The MCP tool is implemented by [SERVER-0016](../server/SERVER-0016-stdio-run-csharp-query-tool.md), and its official MCP-client live gate is **PASS** in [CAP_0020_CSHARP_QUERY_MCP_VALIDATION.md](../CAP_0020_CSHARP_QUERY_MCP_VALIDATION.md).
- Date: 2026-11-25
- Capability: [CAP-0020](../capabilities/CAP-0020-bounded-generated-csharp-read.md)
- Architecture: [ADR-0028](../adr/ADR-0028-bounded-generated-csharp-read-executor.md)

## RPC

`revit.run_csharp_query`

Support is the explicit set `{26}`. `SupportsRunCSharpQuery` is true only for 26. Numeric `version >= N` checks are not used.

Protocol 26 preserves every protocol-25 capability by explicit membership. Unknown protocol 27 supports nothing.

The request is `document_id` and `source` only. `source` is 1 to 8,000 characters. Unknown fields are `INVALID_CSHARP_QUERY`. The request does not carry `instance_id`, references, usings, options, a compiler path, a Revit API path, or IL.

## Advertisement

A full host advertises `[26..1]` only when `IRevitRunCSharpQueryService` is composed with the complete v25 prefix. A complete v25 host without that service advertises `[25..1]`.

Protocol 26 means the method is available. It does not mean the local opt-in is on. A production host may advertise 26 while `%LOCALAPPDATA%\RevitMCP\capabilities.json` is missing or false. The call then returns `CSHARP_QUERY_DISABLED` and does not start the CompilerHost or enqueue EXEC-0001.

## Enablement

The Addin reads the capability file on every call. Schema version 1 contains exactly `schema_version: 1` and boolean `csharp_query_enabled`. Missing, unreadable, malformed, wrong-schema, wrong-type, and unknown-property files are disabled. The product does not create the file. No Bridge argument changes it.

## Result and errors

Success returns `result`, `source_sha256`, `cache_key`, `policy_version`, `revit_api` name and version, `compile_outcome`, `duration_ms`, and `result_characters`. `result` is the host-owned JSON from the execution kernel.

CAP-0020 errors may include structured `details`: `reason`, `source_sha256`, `policy_version`, `revit_api` name and version, at most 8 diagnostics, and `compile_outcome`. Details omit the source, cache key, PE, Revit API path, stack trace, and raw exception text. A Bridge error without details serializes `code` and `message` only.

## What protocol 26 does not change

- The MCP tool is the separate `revit_run_csharp_query`. Production `tools/list` is 23. The official MCP-client live gate is **PASS**.
- No Assistant model tool.
- No ribbon or settings UI.
- No generated write and no new transaction.
- Policy v1 still admits only loaded `RevitAPI` 26.5.0.0. Another identity returns `CSHARP_QUERY_API_UNSUPPORTED`.
