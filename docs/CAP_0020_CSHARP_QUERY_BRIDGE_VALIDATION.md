# CAP-0020 Bridge live validation

- Status: **PASS**
- Capability: [CAP-0020](capabilities/CAP-0020-bounded-generated-csharp-read.md)
- Bridge: [BRIDGE-0022](bridge/BRIDGE-0022-bounded-csharp-query-rpc.md)
- Architecture: [ADR-0028](adr/ADR-0028-bounded-generated-csharp-read-executor.md)
- Pull request: https://github.com/predator230814/RevitMCP/pull/137

The tested implementation is `4753c6be935663ac8b10f762ee89f87c999b444c`. This record is documentation added after that commit. It does not change the validated binaries, and the live gate was not rerun.

Run on 2026-11-25 through the typed Bridge path:

Debug `spikes/BimIt.Revit.CSharpQueryBridgeHarness`
→ `NamedPipeBridgeClient`
→ `revit.run_csharp_query`
→ Bridge protocol 26
→ Release Addin built from that SHA
→ Revit 2026.5

The harness is not an MCP tool. No MCP Server was started. The Assistant was not started. Revit was restarted once before case A so that it loaded that Release Addin, and it was not restarted between A, B, C, and D.

An independent pyRevit query read `Document.IsModified` before the first call and after each case. pyRevit did not call `revit.run_csharp_query`.

## Environment

| Item | Observed |
| --- | --- |
| Source SHA | `4753c6be935663ac8b10f762ee89f87c999b444c` |
| Revit | 2026.5, build `26.5.0.55`, language French |
| Loaded Revit API | `RevitAPI` `26.5.0.0`, returned by the successful query |
| Host registration | Handshake negotiated protocol 26 on A, B, C, and D |
| Active document | `Snowdon Towers Sample Electrical`, `C:\Program Files\Autodesk\Revit 2026\Samples\Snowdon Towers Sample Electrical.rvt`. Project document. Active view `COVER SHEET`. Selection count 0. |
| Local opt-in | `%LOCALAPPDATA%\RevitMCP\capabilities.json`. Absent before A. Present only for B, C, and the D disable. Deleted after D. |
| Assistant | Not started |

No write, preview, proposal, apply, or save tool was called. No model transaction was started by this gate.

## Model

`Document.IsModified` was `false` on the context read before A, and on the pyRevit reads after A (`5d64109a836d`), B (`2af155244051`), C (`a7a295005d0e`), and D (`585316dd00b5`). The document stayed a project document. The sample file was not saved.

## Gate

| Case | Setup | Result |
| --- | --- | --- |
| A | Capability file absent. No Revit restart after the pre-gate load. | **PASS.** Negotiated 26. `CSHARP_QUERY_DISABLED`. No result. No `BimIt.Revit.CompilerHost` process before or after the call. |
| B | File written, without a Revit restart, as schema version 1 with `csharp_query_enabled` `true`. Focused Level projection through `NamedPipeBridgeClient`. | **PASS.** Negotiated 26. `compile_outcome` `success`. `policy_version` 1. `revit_api` `RevitAPI` `26.5.0.0`. `source_sha256` `8273b9dc32b5eafe0bfc8b2ebec269308c5501783c713ee97fc6a47943a9d387`. `cache_key` `9112f3250677a7a123a3edaf755afd19b6e63784241cdd60f41933d4b916369c`. `duration_ms` 27. `result_characters` 1179. Closed JSON array of 11 levels. Each item has an opaque `element_ref`, `name`, and `elevation`. The names and elevations match the levels reported by the independent context read. No CompilerHost process remained after the call. |
| C | Same enabled file. No restart. Body `return Environment.TickCount;`. | **PASS.** Negotiated 26. `CSHARP_QUERY_POLICY_REJECTED`. `compile_outcome` `policy_rejected`. `source_sha256` `ca37dfdba83278d4cd4babaefd2c7a52ef7c9ad5606339fcae2cd3e97c5f3fde`. `policy_version` 1. `revit_api` name `RevitAPI`, version `26.5.0.0`. Two diagnostics, both code `CSHARP_QUERY_POLICY_REJECTED`, line 1, columns 8 and 20, message `Environment is outside the read-only query policy.` No result. |
| D | File rewritten, without a Revit restart, to schema version 1 with `csharp_query_enabled` `false`. The same Level call was repeated. | **PASS.** Negotiated 26. `CSHARP_QUERY_DISABLED`. No result. No `BimIt.Revit.CompilerHost` process before or after the call. |

After D the capability file was deleted. The local opt-in is absent again.

## What this gate does not validate

`revit_run_csharp_query` is not implemented. Production `tools/list` was not read through an MCP client in this gate and stays 22 in the implementation. CAP-0020 is not fully complete until the Server tool and the official MCP-client gate exist.
