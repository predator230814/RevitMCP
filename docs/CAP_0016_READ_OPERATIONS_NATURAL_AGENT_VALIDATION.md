# CAP-0016 read-operations natural-agent validation

- Status: PASS
- Architecture: [ADR-0024](adr/ADR-0024-registry-driven-compiled-revit-read-operations.md)
- Capability: [CAP-0016](capabilities/CAP-0016-revit-read-operations.md)
- Bridge: [BRIDGE-0020](bridge/BRIDGE-0020-read-operations-capability-rpc.md)
- MCP exposure: [SERVER-0015](server/SERVER-0015-stdio-read-operations-tools.md)

Run on 2026-11-19 through the production path:

`BIM-IT Assistant` → `BimIt.Revit.Server` → `revit_lookup` / `revit_read` → Bridge protocol 24 → Addin → Revit 2026.5.

The record separates three kinds of evidence:

- **Natural agent observations:** what the Assistant did and showed.
- **Independent transcript audit:** counts recomputed from the frozen MCP capture, not from the agent's prose.
- **Frozen pyRevit baseline:** compared only after the capture was frozen. It was never shown to the agent.

## Environment

| Item | Observed |
|---|---|
| Source SHA | `15e8cb07279d2a64ac5e429dc7e765e622b16ad0` (`main` after PR #121) |
| Build | Release `BimIt.Revit.Server` and `BimIt.Revit.Addin` (`RevitVersion=2026`), built from a clean detached worktree at that SHA with 0 warnings |
| Loaded add-in modules | Read from Revit PID 30976. `BimIt.Revit.Addin.dll` SHA-256 `ceb4b7502ff7f75bee6a5cd32f4c6cad8dd463e7c364f0096505f211d279432c`, `BimIt.Revit.Bridge.dll` `8324e82d25326e30519b200e4c5b7d8e78dc11e0a79ba0472d3bfd183d76a775`, `BimIt.Revit.Contracts.dll` `d99a9e36a176cc9b325374b5d920cfcbcc29b668e94012eee4ee8463e3c7bcda`, `BimIt.Revit.Assistant.Ipc.dll` `94b9c80ed8332929ad1c1720628fa7ec028251fcfb95c0207cbdc5ddb048e0c4`. All four loaded from the tested add-in output. |
| Server process | Child PID 49808, `BimIt.Revit.Server.host.exe` SHA-256 `622ec8eb7edefdf4f88710e57b9547f71d1af3959e5bed8d5774315e01b842d7`, `BimIt.Revit.Server.dll` `ba8e6eae6c972d980a8a1c530b66176310526295e3bf79353fc4aa05fd24d2b6`, Bridge and Contracts hashes identical to the add-in copies. A validation tee in front of that executable recorded stdio. The tee is not product code. |
| Revit | 2026.5, build `26.5.0.55`. One Revit process (PID 30976). |
| Active document | `525413_E_EG_DAVE.OTIS`, path `C:\REVIT USAGER\525413_E_EG_DAVE.OTIS.rvt`, project, workshared, not read-only, `is_modified: false` before the run and after it, active view `SYNCHRONISATION` (drafting view, element id 595104) |
| `instance_id` | `f64d4758-2483-49fb-b96b-3fc02804577c` |
| `document_id` | `7b0a45a2-e3c1-41ea-8cff-b51a6ae88558` |
| Bridge protocol | 24. `revit_lookup` and `revit_read` succeeded. The Server calls those RPCs only when `BridgeProtocol.SupportsReadOperations` is true, and that predicate is true only for negotiated version 24. No `BRIDGE_PROTOCOL_INCOMPATIBLE` was returned. |
| Production `tools/list` | 22 tools, read from the Release Server's `tools/list` response in the frozen capture: `revit_apply_mep_device_placements`, `revit_apply_parameter_updates`, `revit_describe_parameters`, `revit_get_context`, `revit_get_elements`, `revit_get_mep_device_types`, `revit_get_mep_topology`, `revit_get_parameter_values`, `revit_get_room_layouts`, `revit_get_selection`, `revit_get_warnings`, `revit_lookup`, `revit_preview_mep_device_placements`, `revit_preview_parameter_updates`, `revit_preview_room_pattern_placements`, `revit_query_elements`, `revit_query_family_types`, `revit_query_parameter_bindings`, `revit_read`, `revit_read_spatial_rooms`, `revit_request_mep_device_placement_review`, `revit_request_parameter_update_review` |
| Provider | GitHub Copilot, chip `GitHub Copilot · auto`. GitHub CLI active account `QIB-Dave-CoffeeDrivenCoding`. |

## Isolation configuration

- Client: BIM-IT Assistant, already open in the Revit session. Claude Code was not installed, and the product owner directed this run to use the Assistant with the GitHub account above.
- MCP: the Assistant launched only `BimIt.Revit.Server` (stdio, Release from the SHA above). No pyRevit MCP, Nonica, or Autodesk Revit MCP was configured for the agent.
- The model was not given tool names, the operation id, the contract version, or input field names. The prompt below is the whole instruction.
- The Assistant pane reported `Used: revit_lookup, revit_get_context, revit_read, Read skill`. The skill read is Assistant guidance, not a second Revit MCP and not code execution. The MCP capture contains no tool outside `BimIt.Revit.Server`.

## Natural prompt

Exactly this, with nothing added:

> Inspect the currently active Revit project and produce a complete inventory of its user-created worksets for a BIM coordination audit. Report whether worksharing is enabled, the total number of matched worksets, and for every workset report its name, whether it is the default workset, and whether it is visible by default. Demonstrate that the complete inventory is traversed over multiple batches rather than relying on a single bulk response. Preserve names exactly. Do not infer standards or approval status. Do not generate or execute code and do not modify the Revit model.

## Runs

| Run | Result |
|---|---|
| 1 | The valid run, recorded below. |

The MCP capture was copied and marked read-only before any baseline comparison. SHA-256 `fa2bfa993ff3a99bbd49baf6c03bf3e9058d00fb97bb1e8e780aacb4e47982ce`. It is not committed because it contains model and project data.

## Natural agent observations

### Tools called

13 MCP calls, all to BIM-IT tools, in this order:

| # | Tool | Arguments that matter |
|---:|---|---|
| 1 | `revit_lookup` | search, `query: workset` |
| 2 | `revit_get_context` | none supplied by the model; the Assistant bound the instance |
| 3 | `revit_lookup` | exact, `operation: project.user_worksets`, `contract_version: 1` |
| 4 | `revit_get_context` | same as call 2 |
| 5 | `revit_read` | `project.user_worksets` version 1, `inputs: {}`, `page_size: 1` |
| 6 | `revit_get_context` | same as call 2 |
| 7 | `revit_read` | same operation, version, and inputs, `page_size: 10`, cursor from call 5 |
| 8 | `revit_get_context` | same as call 2 |
| 9 | `revit_read` | same, cursor from call 7 |
| 10 | `revit_get_context` | same as call 2 |
| 11 | `revit_read` | same, cursor from call 9 |
| 12 | `revit_get_context` | same as call 2 |
| 13 | `revit_read` | same, cursor from call 11 |

Search lookup returned one summary and no schemas. Exact lookup returned the full descriptor, including both schemas. The first read used `page_size: 1`. Later reads used `page_size: 10`. Changing page size between pages is allowed. The agent did not call a preview, review, or apply tool.

### Agent final answer

Copied from the Assistant pane after the run. It reports worksharing enabled, 36 matched worksets, and five batches of 1 + 10 + 10 + 10 + 5. Every name, default flag, and visibility flag matches the frozen tool rows and the pyRevit baseline. `Yes` is `true` and `No` is `false`. The only default workset is `Sous-projet 1`.

| Workset name | Default workset | Visible by default |
|---|---:|---:|
| `EL_ALARME_INCENDIE` | No | Yes |
| `EL_BASE_PROPRETE` | No | Yes |
| `EL_CABLE_CHAUFFANT` | No | No |
| `EL_CONDUITS` | No | Yes |
| `EL_CONTRÔLE D'ACCÈS` | No | No |
| `EL_DISTRIBUTION` | No | Yes |
| `EL_ECLAIRAGE` | No | Yes |
| `EL_IMPLANTATION` | No | Yes |
| `EL_IMPLANTATION_SERVICES_AUXILIAIRES` | No | Yes |
| `EL_MANCHONS` | No | Yes |
| `EL_PRISES_DISPOSITIFS` | No | Yes |
| `EL_SERVICES ALIMENTAIRES` | No | Yes |
| `EL_SERVICES_AUXILIAIRES` | No | Yes |
| `EL_TI-EL` | No | Yes |
| `Niveaux et quadrillages partagés` | No | No |
| `Sous-projet 1` | Yes | No |
| `ZG_EL_AXES_NIVEAUX` | No | Yes |
| `ZG_EL_DIAGRAMME` | No | No |
| `ZG_EL_ESPACES` | No | No |
| `ZG_EL_PLANS_REFERENCE` | No | Yes |
| `ZG_EL_TEMPORAIRE` | No | No |
| `ZG_EL_ZONES_DEFINITION` | No | Yes |
| `ZL_525413_A_GE` | No | Yes |
| `ZL_525413_A_SG` | No | No |
| `ZL_525413_C_GE` | No | Yes |
| `ZL_525413_E_LEGENDE` | No | No |
| `ZL_525413_M_CH` | No | Yes |
| `ZL_525413_M_PI` | No | Yes |
| `ZL_525413_M_PL` | No | Yes |
| `ZL_525413_M_SA` | No | Yes |
| `ZL_525413_M_VE` | No | Yes |
| `ZL_525413_S_GE` | No | Yes |
| `ZL_545413_G_AN` | No | No |
| `ZL_DIAGRAMME` | No | No |
| `ZL_DWG_LIER` | No | No |
| `Z_ÉQUIPEMENTS FICTIFS` | No | Yes |

The answer also says the inventory excludes internal worksets and worksets that belong to linked documents. It does not assign standards or approval status. This prose is supporting evidence. The PASS verdict rests on the frozen MCP transcript and the independent pyRevit comparison, not on the prose.

## Independent transcript audit

Computed from the five `revit_read` results in the frozen capture.

### Page table

| Page | Call | `page_size` | `returned_count` | `matched_workset_count` | `worksharing_enabled` | `complete` | `next_cursor` |
|---:|---:|---:|---:|---:|---|---|---|
| 1 | 5 | 1 | 1 | 36 | true | false | present |
| 2 | 7 | 10 | 10 | 36 | true | false | present |
| 3 | 9 | 10 | 10 | 36 | true | false | present |
| 4 | 11 | 10 | 10 | 36 | true | false | present |
| 5 | 13 | 10 | 5 | 36 | true | true | null |

### Traversal and identity

| Check | Result |
|---|---|
| Each continuation cursor equals the previous page's `next_cursor`, exactly | yes, four continuations |
| Earlier pages have `complete: false` and a cursor | yes |
| Final page has `complete: true` and `next_cursor` null | yes |
| Sum of `returned_count` / rows actually received | 36 / 36 |
| Distinct rows / duplicates | 36 / 0 |
| `matched_workset_count` identical on every page | 36 |
| `worksharing_enabled` identical on every page | true |
| MCP responses with `isError` or a JSON-RPC error | 0 of 15 |

The 36 names, in returned order:

`EL_ALARME_INCENDIE`, `EL_BASE_PROPRETE`, `EL_CABLE_CHAUFFANT`, `EL_CONDUITS`, `EL_CONTRÔLE D'ACCÈS`, `EL_DISTRIBUTION`, `EL_ECLAIRAGE`, `EL_IMPLANTATION`, `EL_IMPLANTATION_SERVICES_AUXILIAIRES`, `EL_MANCHONS`, `EL_PRISES_DISPOSITIFS`, `EL_SERVICES ALIMENTAIRES`, `EL_SERVICES_AUXILIAIRES`, `EL_TI-EL`, `Niveaux et quadrillages partagés`, `Sous-projet 1`, `ZG_EL_AXES_NIVEAUX`, `ZG_EL_DIAGRAMME`, `ZG_EL_ESPACES`, `ZG_EL_PLANS_REFERENCE`, `ZG_EL_TEMPORAIRE`, `ZG_EL_ZONES_DEFINITION`, `ZL_525413_A_GE`, `ZL_525413_A_SG`, `ZL_525413_C_GE`, `ZL_525413_E_LEGENDE`, `ZL_525413_M_CH`, `ZL_525413_M_PI`, `ZL_525413_M_PL`, `ZL_525413_M_SA`, `ZL_525413_M_VE`, `ZL_525413_S_GE`, `ZL_545413_G_AN`, `ZL_DIAGRAMME`, `ZL_DWG_LIER`, `Z_ÉQUIPEMENTS FICTIFS`.

`Sous-projet 1` is the only default workset (`is_default: true`, `is_visible_by_default: false`). The other 35 are not the default. Accented names are unchanged.

## Frozen pyRevit baseline comparison

Taken after the capture was frozen, from the same document, with a read-only Revit API query. `FilteredWorksetCollector` of `WorksetKind.UserWorkset`, then ordered by `StringComparer.OrdinalIgnoreCase` and `StringComparer.Ordinal`. The document was still unmodified.

| Fact | Baseline | MCP result |
|---|---:|---:|
| Worksharing enabled | true | true |
| User worksets | 36 | 36 |
| Default workset | `Sous-projet 1` | `Sous-projet 1` |
| Row mismatches | 0 | 0 |

A row-by-row comparison of name, `is_default`, `is_visible_by_default`, and order gave 36 matched, 0 mismatched, 0 missing, 0 extra.

## Failures, limits, safety

| Item | Observed |
|---|---|
| BIM-IT, Bridge, Revit API or runtime failures | none. 13 of 13 MCP tool calls succeeded. |
| Arbitrary code | none in the MCP capture. The Assistant read one skill. |
| Model mutation | none. `is_modified` stayed false. No write tool was called. |
| Other Revit MCP | none in the capture |
| Registered read operations | still only `project.user_worksets` version 1 |

## Gate verdict

| # | Criterion | Result |
|---:|---|---|
| 1 | Production `tools/list` is exactly 22 | yes |
| 2 | Bridge protocol 24 selected | yes |
| 3 | Agent discovers the registered read capability | yes, search lookup for worksets |
| 4 | Agent discovers `project.user_worksets` version 1 through lookup | yes, exact lookup |
| 5 | Agent obtains the operation schema rather than guessing it | yes, schemas present on the exact lookup and absent on the search |
| 6 | Agent executes the read | yes |
| 7 | Traversal uses more than one page | yes, five pages |
| 8 | Every `next_cursor` is followed unchanged until `complete: true` | yes |
| 9 | Concatenated rows have no omission or duplicate | yes, 36 of 36 |
| 10 | `matched_workset_count` is identical on every page | yes, 36 |
| 11 | Names, `is_default`, `is_visible_by_default`, order, and count match the baseline | yes |
| 12 | No arbitrary code | yes |
| 13 | No model mutation | yes |
| 14 | No other Revit MCP | yes |
| 15 | Zero BIM-IT, Bridge, or Revit execution failures | yes |

The agent prose is supporting evidence. This verdict rests on the frozen MCP transcript and the independent baseline comparison.

**PASS.**

## Limitations

- The client was BIM-IT Assistant with GitHub Copilot, not the Claude Code print-mode process used for the CAP-0015 natural-agent record. The product owner directed that substitution. The Assistant also read one skill.
- The negotiated protocol integer is not a field of the MCP tool payload. Protocol 24 is established because those RPCs succeed only for that negotiated version, and every lookup and read succeeded.
- One valid run on one unmodified workshared project. These 36 names are evidence for this document state, not a product constant.
- The agent chose `page_size` 1 and then 10. A client that asked for 100 would have received the same 36 rows in one page. This document has enough user worksets to cross a page at the sizes the agent requested.
