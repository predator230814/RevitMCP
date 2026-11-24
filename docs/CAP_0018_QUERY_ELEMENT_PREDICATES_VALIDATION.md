# CAP-0018 query-element predicates live validation

- Status: **PASS**
- Capability: [CAP-0018](capabilities/CAP-0018-revit-query-element-predicates.md)
- Bridge: [BRIDGE-0021](bridge/BRIDGE-0021-query-element-predicates.md)
- Pull request: https://github.com/predator230814/RevitMCP/pull/129

The tested implementation is `e66bb76b702cf89aa963ffdbab7ec4d47d0597fc`. This record is documentation added after that commit. It does not change the validated binaries, and the live gate was not rerun.

Run on 2026-11-23 through the production path:

Official `ModelContextProtocol` 2.2.0 `McpClient` (`StdioClientTransport`)
→ Release `BimIt.Revit.Server.exe`
→ `revit.query_elements`
→ Bridge
→ `RevitQueryElementsService`
→ Revit 2026.5

Phase H used the typed `NamedPipeBridgeClient` from the same Release build. Phase G used a separate read-only pyRevit script after the MCP capture was frozen. The script did not receive the MCP element refs and did not call `RevitQueryQuantityConverter`.

## Environment

| Item | Observed |
| --- | --- |
| Source SHA | `e66bb76b702cf89aa963ffdbab7ec4d47d0597fc` |
| Assembly product version | `1.0.0+e66bb76b702cf89aa963ffdbab7ec4d47d0597fc` |
| Build | Release, `RevitVersion=2026`, SDK 10.0.404. Revit was closed during the build. |
| Revit | 2026.5, build `26.5.0.55`, PID 63176 |
| Loaded add-in modules | Read from that Revit process. `BimIt.Revit.Addin.dll` SHA-256 `7999c89ac0fe9d065f55d74d402067021d6954f3338ee4251ab72ab497cfc594`, `BimIt.Revit.Bridge.dll` `74a3c73d86d47fa8b4201153aefc4bcd7707c9de33f59aea0e6a0b85b5cb17bb`, `BimIt.Revit.Contracts.dll` `13d9e57853c6d4afb2867e9d79ff0f5b217b457f3e119afa489fef48c8fc8d00`, `BimIt.Revit.Assistant.Ipc.dll` `20c6b2a161b094daa4c6183768bc223282a6163308571e4f5f6e329f8d3bf175`. All four loaded from the tested add-in output. `RevitAPI.dll` loaded from the Autodesk install, SHA-256 `b5562d65186f0aadb5b5b578eb6f8a06d62869e6cfcde688b1248442b1939285`, file version `26.5.0.55`. |
| Server | `src\BimIt.Revit.Server\bin\Release\net10.0\BimIt.Revit.Server.exe`, SHA-256 `897d9326e704467cd0e9820cd2fbd1c6c8329ad066f71fda2b81170890c5912f` |
| Host registration | Protocol 25. Pipe `revitmcp.bridge.v1.s1.ad1087cbbdf04e32b7b62f0269225172`. |
| `instance_id` | `ad1087cb-bdf0-4e32-b7b6-2f0269225172` |
| `document_id` | `371d2568-5bba-43d5-9174-5fcc42b58a38` |
| Active document | `525413_E_EG_DAVE.OTIS`, path `C:\REVIT USAGER\525413_E_EG_DAVE.OTIS.rvt`, project, workshared, not cloud, not read-only |
| Active view | `SYNCHRONISATION`, drafting view, element id 595104. Selection count 0. |
| Production `tools/list` | Exactly 22: `revit_apply_mep_device_placements`, `revit_apply_parameter_updates`, `revit_describe_parameters`, `revit_get_context`, `revit_get_elements`, `revit_get_mep_device_types`, `revit_get_mep_topology`, `revit_get_parameter_values`, `revit_get_room_layouts`, `revit_get_selection`, `revit_get_warnings`, `revit_lookup`, `revit_preview_mep_device_placements`, `revit_preview_parameter_updates`, `revit_preview_room_pattern_placements`, `revit_query_elements`, `revit_query_family_types`, `revit_query_parameter_bindings`, `revit_read`, `revit_read_spatial_rooms`, `revit_request_mep_device_placement_review`, `revit_request_parameter_update_review` |

No write, preview, proposal, or approval tool was called.

## Accepted limits

These are recorded limits of the gate. They are not failures.

- `is_modified` was already `true` before the first query. Undo was empty. The BIM-IT ribbon command is `TransactionMode.Manual` and only shows the assistant pane. The flag stayed `true` after every phase, on the same document and view. It is not evidence that the reads left the document unmodified. The evidence kept for that claim is that the flag did not change, that no write, preview, or apply was called, and that the exercised path is read-only.
- `QUERY_CANDIDATE_LIMIT` was not exercised live. The coarse population was 23, under the v1 constant of 10,000. Overflow behavior stays covered by the automated tests.

## Population

Document scope, category `Raccords de chemins de câbles`, limit 100. The `predicates` key was omitted, not `[]`.

- `matched_count` 23
- `truncated` false
- 23 distinct `element_refs`, ordinal order

Later predicate queries used that category. The parameters were:

| Name | `parameter_ref` | Role |
| --- | --- | --- |
| `Commentaires` | `d2b582c0-754c-4296-8e5c-80b7d270e91f` | Present on all 23. Eleven have no value. Twelve equal `EXTRAIT`. |
| `Courbure ou raccord` | `550fce47-31af-4a9e-a3be-6b8cc5428bac` | Absent on 13. Present with integer 2 on 10. |
| `Élévation par rapport au niveau` | `460ce4a4-fbfa-4040-bf42-187d78012a94` | Length spec `autodesk.spec.aec:length-2.0.1`. Values read in millimeters: 2500, 2600, or about 2950. |

No single parameter in this population is both absent on some elements and present without a value on others. Missing and empty were proven on the two parameters above. The model was not edited to combine them.

## Gate

| Phase | Result |
| --- | --- |
| B — legacy CAP-0002, predicates omitted | PASS. 23 refs, not truncated, ordinal. |
| C — missing and empty | PASS against `revit_get_parameter_values`. `Commentaires`: `is_missing` 0, `is_empty` 11, `is_present` 23, `is_not_empty` 12. `Courbure ou raccord`: `is_missing` 13, `is_empty` 0, `is_present` 10, `is_not_empty` 10. No absent element is in `is_empty`. |
| D — string | PASS. `equals` `EXTRAIT` and `contains` `EXT` return the same 12 refs. `equals` `extrait`, `contains` `extrait`, `equals` `AUTRE`, and `contains` `ZZZ` return 0. `not_equals` `EXTRAIT` returns 0, so the 11 empty comments do not match a string comparison. Comparison is ordinal and case-sensitive. |
| E — quantity | PASS. `greater_than` 2700 mm and `greater_than` 2.7 m (`autodesk.unit.unit:meters-1.0.1`) return the same 11 refs, the readings above 2700 mm. Unit `autodesk.unit.unit:amperes-1.0.0` and token `not-a-forge-type-id` both return `INVALID_QUERY`. Neither returns `REVIT_EXECUTION_FAILED`. |
| F — AND | PASS. `EXTRAIT` (12) and elevation `greater_than` 2700 mm (11) return 0, the empty intersection. `EXTRAIT` (12) and elevation `greater_than` 2550 mm (17) return 6, exactly that intersection, in ordinal order. |
| G — independent pyRevit baseline | PASS. See below. |
| H — protocol 24 | PASS. The client offered `24` down to `1` and did not offer 25. The handshake selected 24. The legacy query returned 23. The same query with the `EXTRAIT` predicate failed before the RPC with `BRIDGE_PROTOCOL_INCOMPATIBLE` and `revit.query_elements predicates require bridge protocol 25.` A following legacy query returned 23 again. |

## Phase G

pyRevit 7.1, IronPython 2.7.12, run `37fa1ca759cf`. The run was read-only and rolled back, with no added, modified, or deleted elements. The script imported `pyrevit.api.DB` only. It collected `OST_CableTrayFitting` instances and read the three parameters with `LookupParameter`. Elevation used `Definition.GetDataType()`, `UnitUtils.IsMeasurableSpec`, `IsUnit`, `IsValidUnit`, `AsDouble()`, and `ConvertToInternalUnits` for 2700 mm and 2550 mm.

The MCP refs were frozen before this script ran. CAP-0018 was not called again after the baseline.

The 23 pyRevit `UniqueId`s are exactly the 23 legacy query refs. Each predicate population matches the frozen MCP set:

| Control | MCP | pyRevit | missing | extra |
| --- | ---: | ---: | ---: | ---: |
| `Commentaires` `is_empty` | 11 | 11 | 0 | 0 |
| `Commentaires` `equals` `EXTRAIT` | 12 | 12 | 0 | 0 |
| `Courbure ou raccord` `is_missing` | 13 | 13 | 0 | 0 |
| `Courbure ou raccord` `is_present` | 10 | 10 | 0 | 0 |
| elevation `>` 2700 mm | 11 | 11 | 0 | 0 |
| elevation `>` 2550 mm | 17 | 17 | 0 | 0 |
| `EXTRAIT` AND elevation `>` 2700 mm | 0 | 0 | 0 | 0 |
| `EXTRAIT` AND elevation `>` 2550 mm | 6 | 6 | 0 | 0 |

## Verdict

The Tech Lead accepted phases B, C, D, E, F, and H, and the two limits above, as a conditional pass pending Phase G. Phase G then matched the frozen MCP sets with 0 missing and 0 extra. Live validation of the implementation `e66bb76b702cf89aa963ffdbab7ec4d47d0597fc` is **PASS**.
