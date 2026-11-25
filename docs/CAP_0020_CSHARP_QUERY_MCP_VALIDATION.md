# CAP-0020 MCP live validation

- Status: **PASS**
- Capability: [CAP-0020](capabilities/CAP-0020-bounded-generated-csharp-read.md)
- Server: [SERVER-0016](server/SERVER-0016-stdio-run-csharp-query-tool.md)
- Bridge: [BRIDGE-0022](bridge/BRIDGE-0022-bounded-csharp-query-rpc.md)
- Architecture: [ADR-0028](adr/ADR-0028-bounded-generated-csharp-read-executor.md)
- Pull request: https://github.com/predator230814/RevitMCP/pull/138
- Earlier Bridge proof: [CAP-0020 Bridge live validation](CAP_0020_CSHARP_QUERY_BRIDGE_VALIDATION.md), tested on `4753c6be935663ac8b10f762ee89f87c999b444c`

The tested implementation is `61673a35c8d1ecee19637f6d400079c485805a43`. This record is documentation added after that commit. It does not change the validated binaries, and the live gate was not rerun.

Run on 2026-11-25 through the official MCP client:

`ModelContextProtocol` 2.2.0 `McpClient` / `StdioClientTransport`
→ Release `BimIt.Revit.Server.exe`
→ Bridge protocol 26
→ Release Addin built with `RevitVersion=2026`
→ Revit 2026.5

The local validation harness is not a product component and is not part of this commit. The Assistant was not started. Revit was started once after the Release Addin build, at 2026-11-25 14:22:35 local time, process 43572. It was not restarted between A, B, C, and D. The MCP client and Server process were one session for all four cases.

An independent pyRevit query read `Document.IsModified` after D. pyRevit did not call `revit_run_csharp_query`.

## Environment

| Item | Observed |
| --- | --- |
| Source SHA | `61673a35c8d1ecee19637f6d400079c485805a43` |
| Revit | 2026.5, build `26.5.0.55`, language French |
| Loaded Revit API | `RevitAPI` `26.5.0.0`, returned by the successful query |
| Host registration | Instance `838c89dd-574a-4a21-b82f-47fc57558dc7`, process 43572, `bridge_protocol_version` 26, Addin `1.0.0.0` |
| Active document | `Snowdon Towers Sample Electrical`, `C:\Program Files\Autodesk\Revit 2026\Samples\Snowdon Towers Sample Electrical.rvt`. Project document. `document_id` `3d339623-b1cd-4971-96fc-8111edd998ac`. Active view `COVER SHEET`. Selection count 0. |
| Local opt-in | `%LOCALAPPDATA%\RevitMCP\capabilities.json`. Absent before A. Present only for B, C, and the D disable. Deleted after D and confirmed absent. |
| Assistant | Not started |

Release identities built from that SHA before Revit started:

| File | Identity | SHA-256 |
| --- | --- | --- |
| `BimIt.Revit.Addin.dll` | `1.0.0.0`, 788480 bytes | `83454f3401053f5dfc5ac346f0a5e68e74db2fe0124f5c8d953a6e4265b1462d` |
| `BimIt.Revit.Server.dll` | `1.0.0.0`, 395264 bytes | `ee538112f081736c2505a155a308948a4130a4f27945fec8f02ffcdbd3508ea5` |
| `BimIt.Revit.Server.exe` | 162816 bytes | `eaa7c3f780c186c41c610fb01e53f39aead9132763891962b1f204cfb3fa0fd4` |
| `BimIt.Revit.CompilerHost.exe` | 152064 bytes, packaged beside the Addin | `684b23b18d7b99dd88eeb4806ac577068a6b9cc4bc82dccd7310a4d6f0e8a413` |
| `BimIt.Revit.Bridge.dll` | `1.0.0.0`, 127488 bytes | `f882c22717917ee69f2dbaf5d59ca78fc53cba6a5893b8c252663afd0521b480` |
| `BimIt.Revit.Contracts.dll` | `1.0.0.0`, 144384 bytes | `d0aeebc0fea1689e0d0d6660eab568d36aa7eb8cf1e04e0d77874e59d83130de` |

`tools/list` returned exactly 23 tools. `revit_run_csharp_query` was present with title `Run bounded Revit C# query`, `readOnlyHint` true, and `openWorldHint` false. No Bridge RPC name, including `revit.run_csharp_query`, appeared.

No write, preview, proposal, apply, or save tool was called. No model transaction was started by this gate.

## Model

`revit_get_context` reported `is_modified` false before A and after D, with the same `document_id`. The pyRevit read after D, run `36b38eb0d84f`, reported `Document.IsModified` false. The sample file was not saved.

## Gate

The Level body was the same one-line projection used by the Bridge gate:

```csharp
return new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().Select(level => new { element_ref = elementRef(level), name = level.Name, elevation = level.Elevation }).ToList();
```

| Case | Setup | Result |
| --- | --- | --- |
| A | Capability file absent. No Revit or MCP restart after the pre-gate load. | **PASS.** `CSHARP_QUERY_DISABLED`. No result and no details. No `BimIt.Revit.CompilerHost` process before or after the call. |
| B | File written, without a restart, as schema version 1 with `csharp_query_enabled` true. The Level projection above. | **PASS.** `compile_outcome` `success`. `policy_version` 1. `revit_api` `RevitAPI` `26.5.0.0`. `source_sha256` `8273b9dc32b5eafe0bfc8b2ebec269308c5501783c713ee97fc6a47943a9d387`. `cache_key` `9112f3250677a7a123a3edaf755afd19b6e63784241cdd60f41933d4b916369c`. `duration_ms` 29. `result_characters` 1179. Closed JSON array of 11 levels. Each item has an opaque `element_ref`, `name`, and `elevation`. The names and elevations match the levels reported by the independent context read. No CompilerHost process remained after the call. |
| C | Same enabled file. No restart. Body `return Environment.TickCount;`. | **PASS.** `CSHARP_QUERY_POLICY_REJECTED`. `compile_outcome` `policy_rejected`. `source_sha256` `ca37dfdba83278d4cd4babaefd2c7a52ef7c9ad5606339fcae2cd3e97c5f3fde`. `policy_version` 1. `revit_api` name `RevitAPI`, version `26.5.0.0`. Two diagnostics, both code `CSHARP_QUERY_POLICY_REJECTED`, line 1, columns 8 and 20, message `Environment is outside the read-only query policy.` No result, no source echo, no cache key, no filesystem path, and no stack trace. |
| D | File rewritten, without a restart, to schema version 1 with `csharp_query_enabled` false. The same Level call was repeated. | **PASS.** `CSHARP_QUERY_DISABLED`. No result. No `BimIt.Revit.CompilerHost` process after the call. |

After D the capability file was deleted. The local opt-in is absent again.

## Completion

This gate is the remaining CAP-0020 live proof. With the earlier Bridge gate, CAP-0020 is live-complete. Production `tools/list` is 23. Bridge protocol stays 26.
