# ADR-0023 natural-agent benchmark validation

- Status: PASS
- Architecture: [ADR-0023](adr/ADR-0023-declarative-revit-read-query-architecture.md)
- Capability: [CAP-0014](capabilities/CAP-0014-revit-query-family-types.md), `revit_query_family_types`
- Companion record: [CAP_0014_FAMILY_TYPES_VALIDATION.md](CAP_0014_FAMILY_TYPES_VALIDATION.md). It validates the tool contract with a client that was told the tool and its parameters. This record is the ADR-0023 acceptance gate with a client that was not.

Two natural-agent runs were made on 2026-11-04. Revit 2026 (build `26.5.0.55`), active document `525413_E_EG_dave.otis` (project, workshared, `is_modified: true`). The Addin was a Release build of PR #101 head `c7b73f7b9a2466f67d3dd2dbe28b035483db4a7f`, which carries the CAP-0014 stale-cursor fix. Revit was started after that build.

| Run | What it showed | Literal ADR gate |
|---|---|---|
| 1. First natural-agent run | The agent found the right tools on its own and reproduced all six totals, with 0 BIM-IT or Revit failures. It did not read every row of two categories. | Not met: not every row was traversed |
| 2. Gate-completion rerun | The agent traversed all 1,205 rows to the explicit final page and reproduced the same six totals. | Met |

Run 1 is kept as historical evidence. It is not edited to look like run 2.

## Gate criteria

ADR-0023 requires an isolated MCP client to reach the full inventory:

| Criterion | Run 1 | Run 2 |
|---|---|---|
| without arbitrary scripts | yes | yes |
| without pyRevit, Nonica or any other Revit MCP | yes | yes |
| without schema coaching in the prompt | yes | yes |
| without modifying the model | yes | yes |
| no failed tool call attributable to Revit API, version or runtime quirks | yes, 0 | yes, 0 |
| every page traversed to an explicit final page | **no.** All rows of seven categories were read. For Electrical Fixtures and Electrical Equipment only the placed rows were read. | **yes.** All 1,205 rows in 21 pages, ending in `complete: true` with no cursor. |

## Result

Both runs produced the same six totals. They equal the independent pyRevit baseline recorded in ADR-0023. The baseline was not shown to the agent and was not used while the data was gathered. The comparison was made after each result was frozen.

| Fact | Run 1 | Run 2 | pyRevit baseline (ADR-0023) |
|---|---:|---:|---:|
| Distinct families | 196 | 196 | 196 |
| `FamilySymbol` types | 1,205 | 1,205 | 1,205 |
| Placed types | 254 | 254 | 254 |
| Placed instances | 8,278 | 8,278 | 8,278 |
| Used families | 87 | 87 | 87 |
| Unused families | 109 | 109 | 109 |

## Conditions (both runs)

- **Client:** Claude Code in print mode with `--strict-mcp-config`. Its only MCP server was `BimIt.Revit.Server` (Release). Built-in tools were disabled, and project settings were the only settings source.
- **Tools offered:** the twelve BIM-IT read tools were pre-approved. The preview, review and apply tools were not, and the client cannot grant permissions non-interactively. The client's tool list contained all 19 BIM-IT tools. No pyRevit, Nonica, Assistant, other Revit MCP, shell, file, script or Revit API access existed.
- **Prompt:** the BIM user's task only. It listed the nine categories and the six facts wanted, and asked for accented-name preservation, no standards inference from `Tt_`, and read-only behavior. It named no tool, field, parameter, category token, cursor mechanism, page size or expected count. It did not say how to establish completeness or recognize the end of a traversal.
- **Server:** run 1 used a Release server build of PR #101. Run 2 used a Release server build of PR #102 head `c3b977172c8b470214afcd3f9b394e56016edb0f`, which differs from the first only by tool and schema description text. The Addin was the same build in both runs.

The natural task of run 1 was:

> Inspect the currently active Revit project and produce a complete inventory of the loaded electrical family content for nine categories (Electrical Fixtures, Electrical Equipment, Lighting Fixtures, Lighting Devices, Data Devices, Communication Devices, Fire Alarm Devices, Security Devices, Nurse Call Devices): distinct families, family types, placed types, placed instances, used families, and loaded but unused families. The inventory must be complete, not a sample. Preserve French and accented names. Say whether the data proves completeness and what limitations apply. Do not infer company-standard or approval status from naming conventions such as `Tt_`. Do not modify the model.

Run 2 used the same task and the same nine categories. It replaced "complete, not a sample" with a stricter requirement:

> For this validation, a complete inventory means you must actually traverse and account for every family-type row in the requested population, not rely only on aggregate totals. You do not need to print every row in your final answer, but you must verify that the full row population was traversed without gaps or duplicates.

## Run 1: first natural-agent run

| Item | Observed |
|---|---|
| Tools the agent chose | `revit_get_context` (1 call) and `revit_query_family_types` (25 calls), out of 19 visible |
| Total MCP calls | 26 |
| BIM-IT / Revit tool failures | 0 |
| Client-side inline limit events | 1 (see below) |
| Arbitrary code generated | none |
| Other Revit MCP used | none |
| Model mutation | none. No preview, review or apply tool was called. |
| Accented French names | preserved. Examples as returned: `Tt_AAM_Afficheur_Alpha_Numérique` (family) and `BOITE DE TIRAGE SALLE MÉCANIQUE` (type, family `Tt_DDO_Equipement_Divers`). |
| Elapsed | about 337 seconds wall clock for the whole run (agent and tool time together) |

The 337 seconds include model reasoning and the agent's own cross-checks. They are not comparable to the roughly 5 seconds of raw tool time recorded in the CAP-0014 record, which measured only the 13 tool calls of a scripted page walk.

### How the agent established completeness in run 1

The agent used the contract fields that the tool returns, and then cross-checked them:

- `summary` over the whole matched set, `complete`, `next_cursor` and `returned_count`. It followed the cursor of the one multi-page category to `complete: true` with `next_cursor: null`.
- The nine single-category queries summed exactly to the nine-category query on all six numbers.
- `usage: placed` and `usage: unused` partitioned `usage: all`: 254 and 951 types make 1,205.
- It read every row of seven categories, and every placed row of the other two (Electrical Fixtures and Electrical Equipment). So all 254 placed types and all 8,278 instances were read row by row. The 706 unplaced rows of those two categories rest on the server summary and the partition identity, and the agent said so.
- It re-added row values by hand for three categories and matched the server summary each time.

That is valid evidence for the totals. It does not satisfy the literal gate, because 706 rows were never read. That is why run 2 was made.

## Run 2: gate-completion rerun

The agent made 24 MCP calls: 1 `revit_get_context`, 1 query with a one-row page to read the summary, 1 query that produced the oversize response described below, and 21 pages of the full nine-category traversal.

| Item | Observed |
|---|---|
| Tools the agent chose | `revit_get_context` (1 call) and `revit_query_family_types` (23 calls), out of 19 visible. It named `revit_get_mep_device_types`, `revit_query_elements` and `revit_get_elements` as unsuitable and did not call them. |
| Total MCP calls | 24 |
| BIM-IT / Revit tool failures | 0 |
| Client-side inline limit events | 1 (see below) |
| Traversal | one nine-category query with the whole population, in 21 pages |
| Rows traversed | 1,205 |
| Distinct row identities (`type_ref`) | 1,205 |
| Duplicates / gaps | 0 / 0 |
| Final page | `complete: true`, `next_cursor: null`, 5 rows. The 20 earlier pages had `complete: false` and a cursor. |
| Arbitrary code generated | none |
| Other Revit MCP used | none |
| Model mutation | none. No preview, review or apply tool was called. |
| Accented French names | preserved. Examples as returned: `ESPACE POUR CÂBLODISTRIBUTION SALLE SERVEUR` (type) and `Tt_AAM_Afficheur_Alpha_Numérique` (family). |
| Elapsed | about 333 seconds wall clock for the whole run (agent and tool time together) |

The agent discovered the mechanism itself. It read the context, chose the family-type tool, paged with the cursor the tool returns, and treated `complete` as the end of the traversal. It then checked that its rows added up to the tool's summary: rows equal the matched count, the placed counts and instance sum equal the summary, and the distinct family and used-family counts equal the summary.

### Independent audit of the run

After the run, the transcript of the client was audited from the tool results only. The audit used no Revit access. It confirmed the agent's claims:

- **Pages:** 21, all with the same query and the same summary. Each page after the first sent exactly the previous page's `next_cursor`. `returned_count` equals the row count on every page. The sum of rows is 1,205, equal to `matched_type_count`.
- **Identities:** 1,205 distinct `type_ref` values and 0 repeats. 196 distinct `family_ref` values.
- **Totals from the rows alone:** 254 rows with a placed instance, 8,278 instances in total, 87 families with a used type, and 109 families whose types are all unplaced. `is_used` agrees with `placed_instance_count > 0` on every row.
- **Order:** the rows are in ordinal order of category token, family name, type name and `type_ref`, with no violation across page boundaries.
- **Rows per category:** Electrical Fixtures 632, Electrical Equipment 181, Lighting Fixtures 102, Data Devices 73, Security Devices 69, Fire Alarm Devices 65, Lighting Devices 56, Nurse Call Devices 14, Communication Devices 13.
- **Accents:** 83 rows have non-ASCII characters in a family or type name. No row has mojibake markers. The two quoted examples each occur exactly once.
- **Largest page:** 25,621 characters, well inside the 120,000-character budget and the client's display limit.

One detail differs from the agent's own report. The agent said it could see 16 BIM-IT tools. The client's tool list contained 19, as in run 1. This record uses 19.

### The client-side limit event, in both runs

In each run the agent's first full-size request returned a valid result of 79,310 serialized characters, which is inside BIM-IT's 120,000-character result budget. Claude Code declined to show it inline because the result exceeded that client's own token limit for tool output. The tool call did not fail, and neither the server nor the Addin produced an error. The agent then used smaller pages of 60 to 80 rows, discovered from the tool contract and its own judgment. In run 2 the agent also noted that the spill file the client offered could not be read, because it had no file tool, and it restarted the traversal instead. This record does not treat the event as a BIM-IT defect, and no page-size or budget change follows from it. Each agent counted the event as one failed call in its own report; this record classifies it as a client display limit.

## Limitations observed by the agents

These come from the agents' reports. They describe the existing contract and are not new findings of this record:

1. The tool reads the active host project only. Content that exists only in linked models is not included in any count, so a family can read as unused here and still be used in a link.
2. The summary is relative to the matched set. In a `usage: unused` query, `unused_family_count` was 171, which counts families that have at least one unplaced type. The loaded-but-unused count of 109 comes from `usage: all`. The run 1 agent noticed this and used the right query. The tool metadata did not state it. The metadata change in PR #102 states it.
3. One response gives one aggregate for all requested categories. A per-category table needed one query per category.
4. The run 1 agent could not tell from the available data whether instance counts include group members, design options or nested shared families. CAP-0014 defines these rules, but the tool description and output schema did not state them. The metadata change in PR #102 states them.
5. A full-size page can be larger than some clients will display, so a client may need smaller pages. Full enumeration of this population took 21 pages.
6. The count of unused families means no placed instance in the host project. It does not say whether content is referenced by schedules, legends, nested families or design options, and it says nothing about purging.
7. No approval, standard, or library-provenance field exists. The agents drew no conclusion from the `Tt_` prefix or other naming conventions.

## Not covered

- Only one project, one Revit version and one client were used. The numbers are evidence from this document state, not product constants.
- The model was `is_modified: true`. The counts are a snapshot of the open session.
- Bridge protocol 22 is inferred from the successful calls, as in the companion record. The v21-only refusal was not run live.
- No stale-cursor or edit test was part of these runs. It is recorded in the companion record.
- The runs do not validate any source named in section 10 of ADR-0023.
