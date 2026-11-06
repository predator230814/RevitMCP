# ADR-0023 parameter-binding natural-agent validation

- Status: PASS
- Architecture: [ADR-0023](adr/ADR-0023-declarative-revit-read-query-architecture.md)
- Capability: [CAP-0015](capabilities/CAP-0015-revit-query-parameter-bindings.md), `revit_query_parameter_bindings`
- Bridge: [BRIDGE-0019](bridge/BRIDGE-0019-query-parameter-bindings-capability-rpc.md)
- Earlier records: [CAP_0015_PARAMETER_BINDINGS_BRIDGE_VALIDATION.md](CAP_0015_PARAMETER_BINDINGS_BRIDGE_VALIDATION.md) validates the typed Bridge path with a harness that knew the call. This record is the CAP-0015 natural-agent gate, with an MCP client that was told nothing about the tool.
- Independent baseline: [ADR_0023_PARAMETER_BINDINGS_PYREVIT_BASELINE.md](ADR_0023_PARAMETER_BINDINGS_PYREVIT_BASELINE.md)

Run on 2026-11-06 through the complete production path:

`isolated MCP client` → `BimIt.Revit.Server` → `revit_query_parameter_bindings` → Bridge protocol 23 → Addin → Revit 2026.5.

The record separates three kinds of evidence:

- **Natural agent observations:** what the client did and said.
- **Independent transcript audit:** counts recomputed from the raw MCP tool results, not from the agent's prose.
- **Frozen pyRevit baseline:** compared only after the run was frozen. It was never shown to the agent.

## Environment

| Item | Observed |
|---|---|
| Source SHA | `591013f87a9e0bdc1d2576dfd0cc97a2f3c0af21` (main after PR #110) |
| Build | Release `BimIt.Revit.Server` and `BimIt.Revit.Addin` (`RevitVersion=2026`), built from a clean worktree at that SHA with 0 warnings |
| Loaded modules | Read from the running Revit process. `BimIt.Revit.Addin.dll` SHA-256 `83961f05…`, `BimIt.Revit.Bridge.dll` `e160bfbd…`, `BimIt.Revit.Contracts.dll` `97a26fc1…`. All four BIM-IT DLLs loaded from the tested build output. |
| Revit | 2026.5, build `26.5.0.55`. One Revit process (PID 60812). |
| Add-in version | `1.0.0.0` |
| Active document | `525413_E_EG_DAVE.OTIS`, project, workshared, not read-only, `is_modified: true` before the run, active view `SYNCHRONISATION` (drafting view) |
| `instance_id` | `54ea6528-4e1c-47e9-a6b3-5b53681f6864` |
| `document_id` | `913c6a0d-e677-40f5-a1cb-05976423a188` |
| Bridge protocol | Registration 23. Handshake offered `[23..1]`, selected 23, host `[23..1]`. |
| Production `tools/list` | 20 tools, read directly from the Release Server over stdio: `revit_apply_mep_device_placements`, `revit_apply_parameter_updates`, `revit_describe_parameters`, `revit_get_context`, `revit_get_elements`, `revit_get_mep_device_types`, `revit_get_mep_topology`, `revit_get_parameter_values`, `revit_get_room_layouts`, `revit_get_selection`, `revit_get_warnings`, `revit_preview_mep_device_placements`, `revit_preview_parameter_updates`, `revit_preview_room_pattern_placements`, `revit_query_elements`, `revit_query_family_types`, `revit_query_parameter_bindings`, `revit_read_spatial_rooms`, `revit_request_mep_device_placement_review`, `revit_request_parameter_update_review` |

## Isolation configuration

- Client: Claude Code 2.1.260 in print mode, model `claude-sonnet-5-5`, effort `medium`, launched as an independent process with the desktop-session variables removed.
- MCP: `--strict-mcp-config` with one server, `BimIt.Revit.Server` (stdio, Release). No pyRevit MCP, Nonica, Autodesk Revit MCP or BIM-IT Assistant was configured.
- Built-in tools: `--tools ""`, so no shell, file, web or scripting tool. Project settings were the only settings source and the run folder had none.
- Permissions: the 13 BIM-IT read tools were allowed. The 7 preview, review and apply tools were in `--disallowedTools`.
- The client session therefore listed 13 tools (`init` event) and no tool outside `BimIt.Revit.Server`. The Server itself exposes 20, as shown above.
- No schema, tool name, field or count was given. The agent saw the normal MCP descriptions and schemas, which are part of the product.

## Natural prompt

Exactly this, with nothing added:

> Inspect the currently active Revit project and produce a complete inventory of its project parameter definitions and category bindings for a BIM standards audit. Report the total number of bound parameter definitions, how many are shared versus local or built-in, how many are instance-bound versus type-bound, the number of distinct bound categories, and the total number of definition-to-category associations. The inventory must be complete: actually traverse and account for every binding row in the population rather than relying only on aggregate totals. Preserve French and accented names, include a few representative definitions showing identity, binding kind and categories, and explain what the data does and does not prove. Do not infer company-standard or approval status from naming conventions. Do not generate or execute code and do not modify the Revit model.

## Runs

| Run | Result |
|---|---|
| 1 | Setup failure, not a product result. A PowerShell escape in my permission list turned `` `revit_ `` into a carriage return, so the allow rules did not match any tool. The agent's first call was denied by the client, it made no BIM-IT call, and it stopped after 7 s. No Revit data was read. The prompt was unchanged. |
| 2 | The valid run, recorded below. Same prompt, same isolation, permission rules fixed. |

The run 2 client output (`stream-json`) was frozen read-only before any comparison. SHA-256 `4f06ee6594cb46caa56a52684e39d4b9794a4e2b1cedcbbd064d5598af5691c9`. It is not committed because it contains model data. The same applies to the two tool-result payloads, which were extracted unchanged from it.

## Natural agent observations

### Tools called

3 MCP calls, all to BIM-IT read tools, in this order:

| # | Tool | Arguments |
|---:|---|---|
| 1 | `revit_get_context` | none |
| 2 | `revit_query_parameter_bindings` | `document_id`, `instance_id`, `page_size: 100` |
| 3 | `revit_query_parameter_bindings` | same, plus the `next_cursor` from call 2 |

The agent did not call `revit_describe_parameters`, `revit_get_parameter_values`, `revit_query_elements` or any other tool, and no preview, review or apply tool.

**Agent-choice evidence:** the agent chose the new capability on its first inventory call, straight from its description, and did not try to rebuild the inventory from element-scoped parameter discovery. It called `revit_get_context` first to obtain the document id, and it chose `page_size: 100` itself, the schema maximum. After page 1 it passed the continuation unchanged and stopped because `complete` was true. The agent did not state which other tools it considered. The transcript shows no deliberation text, so "considered" is not recorded.

### Agent final answer (summary)

The agent reported 144 bound definitions (73 shared, 71 local, 0 built-in), 138 instance-bound and 6 type-bound, 109 distinct categories and 354 definition-to-category associations. It said the server does not report the association total and that it summed the per-row category lists by hand, so that figure was the one most worth re-checking. It listed the six type-bound names, eight representative definitions with accented names intact, and a data-limits section: no standards or approval status inferred from prefixes, shared does not mean standard, usage unknown, linked models and family-internal parameters excluded, and snapshot only because `is_modified` is true.

## Independent transcript audit

Computed from the two raw `revit_query_parameter_bindings` tool results, not from the agent's text.

### Page table

| Page | `returned_count` | `complete` | `next_cursor` | Tool output (chars) | Elapsed |
|---:|---:|---|---|---:|---|
| 1 | 100 | false | present | 40,212 | under 1 s |
| 2 | 44 | true | `null` | 23,104 | under 1 s |

Elapsed time is from the client transcript timestamps, which have 1-second resolution: each call and its result carry the same second. The typed-Bridge validation measured the same traversal at 5 to 109 ms per page on this model. The `revit_get_context` call also took about 1 s.

### Traversal and identity

| Check | Result |
|---|---|
| Continuation used in call 3 equals `next_cursor` of page 1, exactly | yes |
| Earlier page has `complete: false` and a cursor | yes |
| Final page has `complete: true` and `next_cursor: null` (key present) | yes |
| Sum of `returned_count` / rows actually received | 144 / 144 |
| Distinct `parameter_ref` / duplicates | 144 / 0 |
| Sum of every row's `categories.length` | 354 |
| Rows with zero categories | 0 |

### Summary

Identical on both pages, and identical `context`:

```text
matched_definition_count   144
shared_count                73
local_count                 71
built_in_count               0
instance_bound_count       138
type_bound_count             6
distinct_category_count    109
```

| Check | Result |
|---|---|
| shared + local + built_in = matched | 73 + 71 + 0 = 144 |
| instance + type = matched | 138 + 6 = 144 |
| sum of `returned_count` = matched | 144 |
| Row identity kinds | shared 73, local 71. Only valid kinds. |
| Row binding kinds | instance 138, type 6. Only valid kinds. |

### Unicode and representative rows

23 of the 144 names contain non-ASCII characters, and the agent's accented names occur in the tool results exactly as quoted. Every representative row the agent cited was checked against the raw rows:

| Name | Identity | Binding | Categories |
|---|---|---|---|
| `BOÎTIER D'ALIMENTATION` | shared `494f0089-df96-4b1b-a186-82f0eb05ccd9` | instance | 1 (Security Devices) |
| `CHARGE INTÉGRÉE` | local | type | 1 (Electrical Equipment) |
| `INTERPHASE` | local | instance | 2 (Parts, Security Devices) |
| `Numéro édifice` | shared `28aacf7c-b52a-46b8-ac84-68ee3a86359a` | instance | 1 (Project Information) |
| `Tt_Capacité_Sectionneur` | shared `857e1417-5cec-423a-b077-34ce3b791d84` | instance | 1 (Electrical Fixtures) |
| `Tt_TXT_Notes` | shared `0eae6920-21b9-4766-b8d9-047ffc770e7c` | type | 1 (Electrical Fixtures) |
| `Tt_TXT_Phase_Creation` | shared `1ccf1f1b-3344-413a-a023-4b6a494be435` | instance | 30 |
| `Manual_Reference` | shared `3d3783e4-6a35-4703-86a1-0b54cbf3b187` | instance | 107 |

The agent's other claims were also correct: page 1 has 30 shared and 70 local rows and 216 associations, page 2 has 43 shared and 1 local row and 138 associations, and the six type-bound names are `bp_Description`, `CHARGE INTÉGRÉE`, `ID_Description FR`, `ID_Identification`, `Tt_TXT_Nbr_Phases` and `Tt_TXT_Notes`. The agent's hand-summed 354 matches the recomputed 354.

## Frozen pyRevit baseline comparison

Done only after the run was frozen. The baseline artifact SHA-256 is `c3d1c6d63dc2a200c60d4ca9ee2d2146125f0e7f5d798b646403d2e27ef8b306`, unchanged from the baseline record.

| Fact | Baseline | MCP result |
|---|---:|---:|
| Bound definitions | 144 | 144 |
| Shared | 73 | 73 |
| Local (non-shared) | 71 | 71 |
| Built-in | 0 | 0 |
| Instance-bound | 138 | 138 |
| Type-bound | 6 | 6 |
| Distinct bound categories | 109 | 109 |
| Definition-category associations | 354 | 354 |

The reference model was in the state the baseline recorded, so no re-measurement was needed. A row-by-row comparison of the 144 rows received through MCP with the baseline (name, identity kind, GUID, Forge type id, binding kind, category token and name) gave 144 matched, 0 mismatched, 0 missing, 0 extra. Both independently identified hidden definitions are present as normal rows.

## Failures, limits, safety

| Item | Observed |
|---|---|
| BIM-IT, Bridge, Revit API or runtime failures | none. 3 of 3 MCP calls succeeded. |
| Client-side tool-output limit event | none. Both pages (40,212 and 23,104 characters) were read in full. |
| Arbitrary code | none. The client had no code or shell tool and the transcript contains no code. |
| Model mutation | none. No write tool was permitted or called, and the harness itself called nothing during the run. `is_modified` was already true before the run. |
| Other Revit MCP | none configured or reachable |
| Total MCP calls / wall clock | 3 / 46.7 s for the whole client process. The result event reports 45.2 s, of which 42.4 s was model API time. |

**Client-generated summary anomaly:** the client emitted a `post_turn_summary` event whose status text said "211 bound params, 137 shared/57 local/17 built-in, 158 instance/53 type". These numbers match neither the tool results nor the agent's final answer, and nothing in the transcript produced them. It is a client-side status string, not a tool result or the answer, so it is not part of the gate evidence. It is recorded here so that it is not mistaken for BIM-IT output.

## Gate verdict

| # | Criterion | Result |
|---:|---|---|
| 1 | Isolated client reaches only `BimIt.Revit.Server` | yes |
| 2 | Production `tools/list` has 20 tools | yes |
| 3 | Prompt has no tool or schema coaching and no expected totals | yes |
| 4 | Agent independently selects an appropriate BIM-IT capability | yes |
| 5 | Complete traversal reaches `complete: true` | yes |
| 6 | Every binding row accounted for | yes, 144 of 144 |
| 7 | Distinct `parameter_ref` equals matched count | yes, 144 |
| 8 | No duplicate or gap | yes |
| 9 | Summary identical on every page | yes |
| 10 | Independent baseline reproduced | yes |
| 11 | Association count matches the baseline | yes, 354 |
| 12 | At least one accented name intact | yes, 23 |
| 13 | Zero BIM-IT, Bridge, Revit API or runtime failures | yes |
| 14 | No arbitrary code | yes |
| 15 | No model mutation | yes |
| 16 | No other Revit MCP | yes |
| 17 | Per-page timing recorded | yes, at the client's 1-second resolution |

**PASS.**

## Limitations

- One valid run on one model in one document state. These numbers are evidence, not product constants.
- The agent made two page calls. A larger population with heavier rows would need more pages or a smaller page, and the 120,000-character budget would then matter more.
- Page timing from the MCP client has 1-second resolution. Finer timing comes from the typed Bridge record.
- The association total of 354 is not reported by the tool. The agent summed it by hand, and the audit confirmed it. A client that did not sum every row would not get that figure from the summary.
- The agent did not state which tools it considered, so agent-choice evidence is limited to the calls it made.
- The client session saw 13 of the 20 tools because the write tools were disallowed. The Server's 20-tool `tools/list` was read separately.
- Run 1 was a setup failure and is kept as such. It is not a product result.
- The tool output and the agent's prose do not prove anything about company standards, parameter usage, values or linked documents.
