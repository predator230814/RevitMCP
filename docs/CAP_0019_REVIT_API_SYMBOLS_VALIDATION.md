# CAP-0019 Revit API symbol discovery live validation

- Status: **PASS**
- Capability: [CAP-0019](capabilities/CAP-0019-revit-api-symbol-discovery.md)
- Architecture: [ADR-0027](adr/ADR-0027-layered-revit-capability-admission.md)
- Pull request: https://github.com/predator230814/RevitMCP/pull/132

The tested implementation is `889e5bebe0f87d51e224fbb8ac9f517006908133`. This record is documentation added after that commit. It does not change the validated binaries, and the live gate was not rerun.

Run on 2026-11-24 through the production path:

Official `ModelContextProtocol` 2.2.0 `McpClient` (`StdioClientTransport`)
→ Release `BimIt.Revit.Server.exe`
→ `revit_lookup` / `revit_read`
→ Bridge protocol 25
→ `revit_api.symbols` v1
→ Revit 2026.5

The client is not in the repository. It called `revit_get_context`, `revit_lookup`, and `revit_read` only. An independent pyRevit query read `Document.IsModified` and `clr.GetClrType(DB.Element).Assembly.GetName()` before and after that session. pyRevit did not call `revit_api.symbols`.

## Environment

| Item | Observed |
| --- | --- |
| Source SHA | `889e5bebe0f87d51e224fbb8ac9f517006908133` |
| Assembly product version | `1.0.0+889e5bebe0f87d51e224fbb8ac9f517006908133` |
| Build | Release add-in `RevitVersion=2026`, loaded from `src\BimIt.Revit.Addin\bin\2026\Release\net8.0-windows`. Release server `net10.0`. |
| Revit | 2026.5, build `26.5.0.55`, language French |
| Loaded add-in modules | `BimIt.Revit.Addin.dll` SHA-256 `4c7b3d8a53775c260b0838d77d166f4c0e827539941041ee838f7d3c7c81f1a0`, `BimIt.Revit.Bridge.dll` `c16139a9ad28d33ee0b49aba6fe7d25111ad5050f27cc57d64a98d4165c2d665`, `BimIt.Revit.Contracts.dll` `35fcd7322c80bb264ce0c83987763b8f8522690ac68fd98bcf7bd4d82c266284`, `BimIt.Revit.Assistant.Ipc.dll` `e6db89ec38142ed31240a800cb179c40561b1e61403779a207078a25f5c2bd56`. All four loaded from that Release output. |
| Loaded Revit API | `RevitAPI`, assembly version `26.5.0.0`, SHA-256 `b5562d65186f0aadb5b5b578eb6f8a06d62869e6cfcde688b1248442b1939285`. The symbol results name the assembly and version only. |
| Server | `src\BimIt.Revit.Server\bin\Release\net10.0\BimIt.Revit.Server.exe`, SHA-256 `1c06756f5fdd47d5079357d5cecd19cd41da3289fc0e323738265c8e87fc8628` |
| Host registration | Protocol 25. |
| Active document | Project document, workshared, not cloud, not read-only. Selection count 0. |
| Production `tools/list` | Exactly 22: `revit_apply_mep_device_placements`, `revit_apply_parameter_updates`, `revit_describe_parameters`, `revit_get_context`, `revit_get_elements`, `revit_get_mep_device_types`, `revit_get_mep_topology`, `revit_get_parameter_values`, `revit_get_room_layouts`, `revit_get_selection`, `revit_get_warnings`, `revit_lookup`, `revit_preview_mep_device_placements`, `revit_preview_parameter_updates`, `revit_preview_room_pattern_placements`, `revit_query_elements`, `revit_query_family_types`, `revit_query_parameter_bindings`, `revit_read`, `revit_read_spatial_rooms`, `revit_request_mep_device_placement_review`, `revit_request_parameter_update_review` |

No write, preview, proposal, apply, or save tool was called.

## Accepted limits

`Document.IsModified` was `true` on the pyRevit read taken before the MCP client started. `revit_get_context` at the start of the MCP session reported `is_modified` `false`. The pyRevit read taken after the eight symbol calls also reported `false`. The flag did not become `true`. The eight symbol reads did not change it. The earlier `true` is not evidence that this gate cleared a dirty document. The evidence kept for the read-only claim is the tool list above and the fact that the flag stayed `false` from `revit_get_context` through the after-check.

## Gate

| Check | Result |
| --- | --- |
| `tools/list` | PASS. Exactly the 22 names above. |
| `revit_lookup` exact `revit_api.symbols` contract 1 | PASS. Mode `exact`. Descriptor present with `input_schema` and `output_schema`. |
| Exact `Autodesk.Revit.DB.FilteredElementCollector` | PASS. `kind` `class`, `assembly_name` `RevitAPI`, `assembly_version` `26.5.0.0`. `methods_omitted_count` 11 is explicit. |
| Exact member `OfClass` | PASS. One instance method, `signatures_omitted_count` 0: `FilteredElementCollector OfClass(Type type)`. |
| Exact `Autodesk.Revit.DB.XYZ` | PASS. `kind` `class`, namespace `Autodesk.Revit.DB`, same assembly identity. Eight properties, `properties_omitted_count` 0, including `double X { get; }`, `double Y { get; }`, and `double Z { get; }`. |
| Exact `Autodesk.Revit.DB.UnitUtils` | PASS. `kind` `static class`, same assembly identity. Twelve method groups, `methods_omitted_count` 2. The first groups are `Convert`, `ConvertFromInternalUnits`, and `ConvertToInternalUnits`, each static, with a real `ForgeTypeId` signature. |
| Exact `Autodesk.Revit.DB.BuiltInCategory` | PASS. `kind` `enum`. Twenty-four fields returned, `fields_omitted_count` 1189. Order starts `const BuiltInCategory INVALID = -1`, then `const BuiltInCategory OST_AbutmentFoundationTags = -2006208`. `OST_Walls` is not in that bounded list. |
| Exact member `OST_Walls` | PASS. One field, `text_truncated` false, `signatures_omitted_count` 0: `const BuiltInCategory OST_Walls = -2000011`. No other fields or methods are returned with it. |
| Search query `OfClass` | PASS. Mode `search`, `matched_type_count` 1, `complete` true. The hit is `Autodesk.Revit.DB.FilteredElementCollector` with `matched_members` `OfClass` and omitted count 0. |
| Negative member `ofclass` | PASS. `INVALID_READ_OPERATION_INPUT`, message `member was not found.` No structured result. |

## Independent assembly identity

pyRevit 7.1, IronPython 2.7.12 read the assembly loaded by that Revit process before and after the MCP session:

- `Name` `RevitAPI`
- `Version` `26.5.0.0`

That name and version match `assembly_name` and `assembly_version` on the collector, `XYZ`, and `UnitUtils` results. The loaded assembly is the Revit 2026.5 installation assembly, version `26.5.0.0`, not the Nice3point reference `26.4.10.0` used at build time. The after read also listed declared `OfClass` methods on the loaded `FilteredElementCollector`: one instance method, not generic, parameters `Type type`, return `FilteredElementCollector`. That is the same signature the MCP member read returned.
