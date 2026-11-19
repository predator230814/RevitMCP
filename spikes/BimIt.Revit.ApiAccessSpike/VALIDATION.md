# Generic Revit API access spike — live benchmark

- Class: **A — promising**
- This is a research spike. It does not change ADR-0023, ADR-0024, production contracts, the MCP catalogue, or Bridge protocol 24.
- Class A is not a production approval. Reflection is not a security boundary.

Run on 2026-11-19 through the production MCP path with a spike-enabled Addin:

`BIM-IT Assistant` → normal Release `BimIt.Revit.Server` → `revit_lookup` / `revit_read` → Bridge protocol 24 → spike Addin → Revit 2026.5.

The record separates three kinds of evidence:

- **Natural agent observations:** what the Assistant did and showed.
- **Independent transcript audit:** facts recomputed from the frozen MCP capture, not from the agent's prose.
- **Human Revit UI:** compared only after the capture was frozen. It was never shown to the agent.

## Environment

| Item | Observed |
|---|---|
| Source SHA | `97f116b167e99db1cad9435dd4a77796a3b4582c` on `spike/generic-revit-api-access` (PR #123) |
| Add-in build | Release, Revit 2026, `-p:BimItApiAccessSpike=true`, output `src/BimIt.Revit.Addin/bin/2026/Spike/Release/net8.0-windows/` |
| Server and Assistant | Normal Release builds, without the spike property |
| Loaded in Revit PID 52200 | `BimIt.Revit.Addin.dll` SHA-256 `11f6d8572d7328ae5482815e425ff21e281eec3efb59dd9e092a1915c112b382`, `BimIt.Revit.Bridge.dll` `f6aee7ce517bab99d57820f8c08a032d0c2b8f976e668859fdda31ff56aa50dd`, `BimIt.Revit.Contracts.dll` `f6a03117494a2bb4f10e36f5ffd5cca5ba8f6e44e22b79ebe213b5351ecd1935`, `BimIt.Revit.Assistant.Ipc.dll` `32e76a220f7e3d073a1885bae4ffe9e42808071f0efd88d2b9b3aaefbd918e03`. All four loaded from that spike output. `RevitAPI.dll` loaded from `C:\Program Files\Autodesk\Revit 2026\RevitAPI.dll`, SHA-256 `b5562d65186f0aadb5b5b578eb6f8a06d62869e6cfcde688b1248442b1939285`. |
| Server process | Assistant PID 22684 launched `BimIt.Revit.Server.exe` (PID 46428), which was a validation tee. The tee started `BimIt.Revit.Server.host.exe` PID 53924, SHA-256 `f8eee5dabd8fe9a9578d36d093ac4cc6cfed7918efd63f4209510bf5f6a7b28a`. `BimIt.Revit.Server.dll` SHA-256 `74b4ecc9567368ad1ade38544a0101b00df2c470c4231f0ea73b9a17087e9053`. The tee is not product code and is not committed. |
| Assistant binaries | `BimIt.Revit.Assistant.exe` SHA-256 `c9a3d4b03be9132171fcf9d13576b740109510825ddd2b34a9a5c5bea90ae486`, `BimIt.Revit.Assistant.dll` `27abd0e7a7a483a7a82ff24163dd9a547b9263ee3898bcbf0d66c6e323111422` |
| Revit | 2026.5, build `26.5.0.55`, `/language FRA`, journal build `20260731_1210(x64)`. One Revit process (PID 52200). |
| Active document | Title `525413_E_EG_DAVE.OTIS`, project, workshared, not cloud, not read-only. Active view `SYNCHRONISATION` (drafting view, element id 595104). Selection count 0. |
| `instance_id` | `d837735b-997d-46bc-ab11-1699d7e2118d` |
| `document_id` | `e4b40bbc-4eb2-4108-88d5-a05a40858636` |
| Bridge protocol | 24. `revit_lookup` and `revit_read` succeeded. The Server calls those RPCs only when `BridgeProtocol.SupportsReadOperations` is true, and that predicate is true only for version 24. No `BRIDGE_PROTOCOL_INCOMPATIBLE` was returned. The MCP protocol string `2026-07-28` in the capture is the MCP revision, not the Bridge integer. |
| Production `tools/list` | 22 tools, from the frozen capture: `revit_apply_mep_device_placements`, `revit_apply_parameter_updates`, `revit_describe_parameters`, `revit_get_context`, `revit_get_elements`, `revit_get_mep_device_types`, `revit_get_mep_topology`, `revit_get_parameter_values`, `revit_get_room_layouts`, `revit_get_selection`, `revit_get_warnings`, `revit_lookup`, `revit_preview_mep_device_placements`, `revit_preview_parameter_updates`, `revit_preview_room_pattern_placements`, `revit_query_elements`, `revit_query_family_types`, `revit_query_parameter_bindings`, `revit_read`, `revit_read_spatial_rooms`, `revit_request_mep_device_placement_review`, `revit_request_parameter_update_review` |
| Spike registry | Unfiltered `revit_lookup` returned exactly three operations: `experimental.revit_api_lookup` v1, `experimental.revit_api_read` v1, `project.user_worksets` v1. No new MCP tool. |
| Provider | Pane chip `GitHub Copilot · auto`. The Copilot session event log names model `gpt-6-luna`. |

## Isolation

- Client: BIM-IT Assistant, fresh conversation, connected only to the Server above. The Assistant command line has one `--server` argument and no other MCP.
- No pyRevit MCP, Autodesk Revit MCP, Nonica, Dynamo, RevitPythonShell, or direct Revit API console supplied model facts. pyRevit processes were present on the machine and were not launched by the Assistant and were not called.
- The model was not given tool names, experimental operation ids, Revit API type names, property names, expected results, or a call sequence.
- The pane footer reported `Used: revit_get_context, revit_lookup, revit_read, Read skill`. `Read skill` is the local `bimit_read_skill` label. It is not an MCP tool and not code execution. The MCP capture contains no tool outside `BimIt.Revit.Server`.
- The skill read was built-in `revit-model-query` (`SKILL.md`, package SHA-256 `bda66ea5168b78ebf1eb37a7c35cbddd5c47471ffc2f14fd928c9ec22d87527b`, matching the repository file under the skill package hash). That skill does not name `experimental.revit_api_lookup`, `experimental.revit_api_read`, `Autodesk.Revit.DB.Level`, `Autodesk.Revit.DB.Phase`, or `Autodesk.Revit.DB.DesignOption`. It mentions level only as one `revit_query_elements` filter concept.

## Natural prompt

Exactly this, with nothing added. The Assistant prepended its skill list before sending the prompt to the model.

> Inspect the currently active Revit project and report its Levels, Phases, and Design Options. For Levels include the name and elevation. For Phases include their names. For Design Options include their names and whether each is primary if that information is available through the Revit API. Discover how to obtain these facts using the available BIM-IT capabilities. Preserve names exactly. Do not generate or execute code and do not modify the Revit model.

## Frozen capture

The MCP capture was copied before the human UI comparison. SHA-256 `ab88ae29ef5c92dbf93c903a8458ea4d410610f675e3dada897504e5977a6b8b` (111016 bytes). It is not committed because it contains model data.

21 client requests. None is a preview, review, or apply call. `isError` is false on every response. There is no JSON-RPC error.

| # | Call | What the audit used |
|---:|---|---|
| 1 | `server/discover` | MCP server `RevitMCP.Server` `0.1.0` |
| 2 | `tools/list` | exactly the 22 names above |
| 3 | `revit_get_context` | document title, `is_modified: true` before any experimental read |
| 4 | `revit_lookup` | `query: levels` → 0 operations |
| 5 | `revit_lookup` | `query: phases` → 0 operations |
| 6 | `revit_lookup` | `query: design options` → 0 operations |
| 7 | `revit_lookup` | offset 0, limit 50, no query → the three operations above, summaries only |
| 8 | `revit_lookup` | exact `experimental.revit_api_lookup` v1, schemas included |
| 9 | `revit_lookup` | exact `experimental.revit_api_read` v1, schemas included |
| 10–12 | `revit_get_context` | same document, `is_modified: true` |
| 13 | `revit_read` | `experimental.revit_api_lookup` v1, `type_name: Autodesk.Revit.DB.Phase` |
| 14 | `revit_read` | same operation, `type_name: Autodesk.Revit.DB.Level` |
| 15 | `revit_read` | same operation, `type_name: Autodesk.Revit.DB.DesignOption` |
| 16–18 | `revit_get_context` | same document, `is_modified: true` |
| 19 | `revit_read` | `experimental.revit_api_read` v1, Phase, `properties: ["Name"]`, `page_size: 200` |
| 20 | `revit_read` | same operation, Level, `properties: ["Name","Elevation"]`, `page_size: 200` |
| 21 | `revit_read` | same operation, DesignOption, `properties: ["Name","IsPrimary"]`, `page_size: 200` |

Calls 13–15 were in flight together, and so were 19–21. Registry search for the English words returned nothing because the operation descriptions do not contain them. The agent then listed the registry, fetched both experimental schemas, and supplied the three API type names from its own knowledge. Exact metadata confirmed the properties before the generic reads. The same `experimental.revit_api_read` operation read all three types. No cursor was required: each result has `complete: true` and `next_cursor: null` because the matched counts are 2, 7, and 3.

Metadata, before the element reads:

- `Autodesk.Revit.DB.Level`, base `Autodesk.Revit.DB.DatumPlane`, `can_collect: true`. `Name` (`System.String`) and `Elevation` (`System.Double`) have `can_project: true`.
- `Autodesk.Revit.DB.Phase`, base `Autodesk.Revit.DB.Element`, `can_collect: true`. `Name` has `can_project: true`.
- `Autodesk.Revit.DB.DesignOption`, base `Autodesk.Revit.DB.Element`, `can_collect: true`. `Name` and `IsPrimary` (`System.Boolean`) have `can_project: true`.
- Listed methods have `executable: false`.

## Returned facts

Elevations are Revit internal feet. The human schedule displays millimeters. `feet × 304.8` matches every displayed millimeter exactly.

Levels, `matched_element_count: 7`, `returned_count: 7`. Order is `UniqueId` order, not elevation order.

| Name | Elevation (ft) | Millimeters |
|---|---:|---:|
| `ED_TRAVAIL_NIVEAU_02` | 97.56233595800525 | 29737 |
| `PLAN DE LIAISON` | 0 | 0 |
| `NIVEAU 1` | 42.42125984251797 | 12930 |
| `NIVEAU 2` | 55.38057742781981 | 16880 |
| `NIVEAU 3` | 68.33989501312165 | 20830 |
| `NIVEAU 4 - APPENTIS` | 81.9553805774261 | 24980 |
| `TOITURE - APPENTIS (P.H.)` | 100.32808398949959 | 30580 |

Phases, `matched_element_count: 2`, `returned_count: 2`: `Existante`, `Nouvelle construction`.

Design options, `matched_element_count: 3`, `returned_count: 3`. The stored `Name` has two spaces before `<principal>`. Each `IsPrimary` is true.

| Name | IsPrimary |
|---|---|
| `Liens  <principal>` | true |
| `Quadrillages  <principal>` | true |
| `Variante 1  <principal>` | true |

The pane prose for `NIVEAU 2` printed `55.38057472781981`. The frozen read is `55.38057742781981`, and that value is the one that converts to 16880 mm. The verdict uses the transcript.

## Human Revit UI

Compared after the capture was frozen. The model was not edited. No option, phase, or level was created.

- Level schedule `AXES_NIVEAUX` (`Nomenclature des niveaux`): the same seven names. Elevations, in schedule order, are 0, 12930, 16880, 20830, 24980, 29737, and 30580. That order is elevation order. The names and millimeter values match the table above.
- Manage > Phases (`Gérer` > `Phases`): `Existante`, then `Nouvelle construction`. No other phase.
- Manage > Design Options (`Gérer` > `Options de conception`): three option sets, `Liens`, `Quadrillages`, and `Variante 1`. Each set contains one option labeled with that stem and `<principal>`. No secondary option is listed. The API rows are those three primary options. The dialog parents are option sets; the reader collected `DesignOption` instances only.

## Document modification

`Document.IsModified` was already `true` on the first `revit_get_context`, before any experimental read. All seven context responses in the capture say `is_modified: true`. None says false. Every experimental read returned success, so the spike guard did not observe the flag flipping during collection or getter calls. The benchmark did not change the flag. The document was already modified when the run started.

The guard does not make a getter pure. A getter can still run arbitrary Revit API code, including a change that does not flip `Document.IsModified`. No `Transaction`, `SubTransaction`, or `TransactionGroup` was opened by the spike. No setter was invoked. No source was accepted or executed.

## Classification

**A.** One generic `FilteredElementCollector.OfClass(Type)` collector and one public-instance scalar projector returned the three benchmark facts. The live success did not add a type-specific reader, branch, or serializer. The spike sources still do not name Level, Phase, or DesignOption. An empty design-option set was not required: the document has three primary options, and the UI matches them.

Not B: nothing in the running spike build special-cases the benchmark classes.

Not C: `OfClass` plus scalar getters obtained the requested names, elevations, and primary flags without a query language and without executing methods.

## Next step

Leave both operations experimental. Do not promote them, do not add a compiled wrapper per class, and do not add generated C# to this result. Before any production design, decide how to bound getter side effects. The `IsModified` check detects a reported modification. It does not prevent one.
