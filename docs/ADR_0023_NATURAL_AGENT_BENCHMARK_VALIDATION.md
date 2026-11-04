# ADR-0023 natural-agent benchmark validation

- Status: PASS
- Architecture: [ADR-0023](adr/ADR-0023-declarative-revit-read-query-architecture.md)
- Capability: [CAP-0014](capabilities/CAP-0014-revit-query-family-types.md), `revit_query_family_types`
- Companion record: [CAP_0014_FAMILY_TYPES_VALIDATION.md](CAP_0014_FAMILY_TYPES_VALIDATION.md). It validates the tool contract with a client that was told the tool and its parameters. This record is the ADR-0023 acceptance gate with a client that was not.

Run on 2026-11-04. Revit 2026 (build `26.5.0.55`), active document `525413_E_EG_dave.otis` (project, workshared, `is_modified: true`). The Addin and server were Release builds of PR #101 head `c7b73f7b9a2466f67d3dd2dbe28b035483db4a7f`, which carries the CAP-0014 stale-cursor fix. Revit was started after the build.

## Result

The first-slice benchmark target of ADR-0023 is achieved. An isolated client reached the full inventory and produced the six reference numbers, with no arbitrary code, no other Revit MCP, no model change, and no failed BIM-IT or Revit call.

| Fact | Natural-agent run | pyRevit baseline (ADR-0023) |
|---|---:|---:|
| Distinct families | 196 | 196 |
| `FamilySymbol` types | 1,205 | 1,205 |
| Placed types | 254 | 254 |
| Placed instances | 8,278 | 8,278 |
| Used families | 87 | 87 |
| Unused families | 109 | 109 |

All six values equal the independent pyRevit baseline recorded in ADR-0023. The baseline was not shown to the agent and was not used while the data was gathered.

## Conditions

- **Client:** Claude Code in print mode with `--strict-mcp-config`. Its only MCP server was `BimIt.Revit.Server` (Release). Built-in tools were disabled, and project settings were the only settings source.
- **Tools offered:** the twelve BIM-IT read tools were pre-approved. The preview, review and apply tools were not, and the client cannot grant permissions non-interactively. `tools/list` showed all 19 BIM-IT tools. No pyRevit, Nonica, Assistant, other Revit MCP, shell, file, script or Revit API access existed.
- **Prompt:** the BIM user's task only. It listed the nine categories and the six facts wanted, and asked for completeness, accented-name preservation, no standards inference from `Tt_`, and read-only behavior. It named no tool, field, parameter, token, cursor mechanism, expected count or baseline. It did not say how to establish completeness.

The natural task was:

> Inspect the currently active Revit project and produce a complete inventory of the loaded electrical family content for nine categories (Electrical Fixtures, Electrical Equipment, Lighting Fixtures, Lighting Devices, Data Devices, Communication Devices, Fire Alarm Devices, Security Devices, Nurse Call Devices): distinct families, family types, placed types, placed instances, used families, and loaded but unused families. The inventory must be complete, not a sample. Preserve French and accented names. Say whether the data proves completeness and what limitations apply. Do not infer company-standard or approval status from naming conventions such as `Tt_`. Do not modify the model.

## Observed behavior

| Item | Observed |
|---|---|
| Tools the agent chose | `revit_get_context` (1 call) and `revit_query_family_types` (25 calls), out of 19 visible |
| Total MCP calls | 26 |
| BIM-IT / Revit tool failures | 0 |
| Client-side inline limit event | 1 (see below) |
| Arbitrary code generated | none |
| Other Revit MCP used | none |
| Model mutation | none. No preview, review or apply tool was called. |
| Accented French names | preserved. Examples as returned: `Tt_AAM_Afficheur_Alpha_Numérique` (family) and `BOITE DE TIRAGE SALLE MÉCANIQUE` (type, family `Tt_DDO_Equipement_Divers`). |
| Elapsed | about 337 seconds wall clock for the whole run (agent and tool time together) |

The 337 seconds include model reasoning and the agent's own cross-checks. It is not comparable to the roughly 5 seconds of raw tool time recorded in the CAP-0014 record, which measured only the 13 tool calls of a scripted page walk.

### The client-side limit event

The agent's second call asked for all nine categories with `page_size: 200`. The server returned a valid result of 79,310 serialized characters, which is inside BIM-IT's 120,000-character result budget. Claude Code declined to show it inline because the result exceeded that client's own token limit for tool output. The tool call did not fail, and neither the server nor the Addin produced an error. The agent then used smaller pages of 60 to 80 rows. This record does not treat the event as a BIM-IT defect, and no page-size or budget change follows from it. The agent counted it as one failed call in its own report; this record classifies it as a client display limit.

### How the agent established completeness

The agent used the contract fields that the tool returns, and then cross-checked them:

- `summary` over the whole matched set, `complete`, `next_cursor` and `returned_count`. It followed the cursor of the one multi-page category to `complete: true` with `next_cursor: null`.
- The nine single-category queries summed exactly to the nine-category query on all six numbers.
- `usage: placed` and `usage: unused` partitioned `usage: all`: 254 and 951 types make 1,205.
- It read every row of seven categories, and every placed row of the other two (Electrical Fixtures and Electrical Equipment). So all 254 placed types and all 8,278 instances were read row by row. The unplaced rows of those two categories rest on the server summary and the partition identity, and the agent said so.
- It re-added row values by hand for three categories and matched the server summary each time.

The agent concluded that the data is sufficient to prove completeness for the active host document, and not for linked models.

## Limitations observed by the agent

These come from the agent's report. They describe the existing contract and are not new findings of this record:

1. The tool reads the active host project only. Content that exists only in linked models is not included in any count.
2. The summary is relative to the matched set. In a `usage: unused` query, `unused_family_count` was 171, which counts families that have at least one unplaced type. The loaded-but-unused count of 109 comes from `usage: all`. The agent noticed this and used the right query. The tool metadata did not state it. The follow-up metadata change in this PR states it.
3. One response gives one aggregate for all requested categories. A per-category table needed one query per category.
4. The agent could not tell from the available data whether instance counts include group members, design options or nested shared families. CAP-0014 defines these rules, but the tool description and output schema did not state them. The follow-up metadata change states them.
5. A full-size page can be larger than some clients will display, so a client may need smaller pages.
6. No approval, standard, or library-provenance field exists. The agent drew no conclusion from the `Tt_` prefix.

## Not covered

- Only one project, one Revit version and one client were used. The numbers are evidence from this document state, not product constants.
- The model was `is_modified: true`. The counts are a snapshot of the open session.
- Bridge protocol 22 is inferred from the successful calls, as in the companion record. The v21-only refusal was not run live.
- No stale-cursor or edit test was part of this run. It is recorded in the companion record.
