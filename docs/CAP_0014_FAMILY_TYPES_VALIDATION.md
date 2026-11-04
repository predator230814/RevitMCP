# CAP-0014 family-type inventory validation

- Status: FAIL
- Capability: [CAP-0014](capabilities/CAP-0014-revit-query-family-types.md)
- Bridge: [BRIDGE-0018](bridge/BRIDGE-0018-query-family-types-capability-rpc.md)
- Automated tests do not update the reference numbers below.

Live validation ran on 2026-11-03 against PR #100 head `7b9efd6e6456eadc1291fe8645a6c73e111b3ebc`.

| Part | Result |
|---|---|
| Primary inventory benchmark | PASS |
| Semantic spot checks | PASS |
| Stale cursor after a human model edit | FAIL |
| v21-only host returns `BRIDGE_PROTOCOL_INCOMPATIBLE` | NOT RUN |

The status is FAIL because a required CAP-0014 behavior failed live. After a committed human edit, an old cursor did not return `STALE_CURSOR`. The inventory itself was complete and matched the reference.

## Reference model

The accepted benchmark for the first live run is:

```text
196 distinct families
1,205 FamilySymbol types
254 placed types
8,278 placed instances
87 used families
109 unused families
```

`used_family_count + unused_family_count` is `196`.

## Isolated MCP client

Run from an MCP client that talks only to BIM-IT Revit MCP. Do not use the Assistant, pyRevit, or another Revit automation host in the same measurement.

Conditions from CAP-0014:

- one Revit instance;
- the benchmark project is the active host project document;
- no second document is active;
- the call is `revit_query_family_types` with `document_id` from `revit_get_context`;
- `category_tokens` is exactly these nine tokens:

```text
OST_ElectricalFixtures
OST_ElectricalEquipment
OST_LightingFixtures
OST_LightingDevices
OST_DataDevices
OST_CommunicationDevices
OST_FireAlarmDevices
OST_SecurityDevices
OST_NurseCallDevices
```

- no `family_name_contains` filter;
- no `type_name_contains` filter;
- `usage` is `all` or omitted;
- follow `next_cursor` until `complete` is true;
- record the query-level summary from the first page and confirm later pages repeat that same summary;
- confirm the concatenated type rows contain 1,205 rows, no skipped row, and no repeated `type_ref`;
- confirm `tools/list` contains `revit_query_family_types` and the production count is 19;
- confirm a v21-only host returns `BRIDGE_PROTOCOL_INCOMPATIBLE` and does not call `revit_get_mep_device_types` or `revit_query_elements`.

## Live run, 2026-11-03

### Environment

| Item | Observed |
|---|---|
| Revit | 2026 (`revit_version` "2026"), Revit 2026.5 |
| Revit build | `26.5.0.55` |
| Add-in version | Not exposed by MCP. The add-in and server were Release builds of the PR head source. No source file changed after the build, and Revit started after the build. |
| PR head | `7b9efd6e6456eadc1291fe8645a6c73e111b3ebc` |
| Instance | `a9914761-0403-49ec-9d7b-dd51db4988a8` |
| Active document | `525413_E_EG_DAVE.OTIS`, project, workshared, local, not read-only, `is_modified: true` before the first query |
| `document_id` | `84ec27d5-908b-4c15-ac2d-3a6c03cd7c72` |
| Active view | `SYNCHRONISATION` (drafting view) |
| Bridge protocol | 22, inferred. MCP does not expose the negotiated version. The server returns `BRIDGE_PROTOCOL_INCOMPATIBLE` before calling the add-in unless `BridgeProtocol.SupportsQueryFamilyTypes` accepts the negotiated version, and only version 22 passes. Every `revit_query_family_types` call succeeded. |
| `tools/list` | 19 BIM-IT tools, including `revit_query_family_types`. No other MCP server or tool was loaded. |

### Client

- The client was Claude Code in print mode with `--strict-mcp-config`, and its only server was `BimIt.Revit.Server` (Release, PR head).
- Built-in tools were disabled (`--tools ""`), and project settings were the only settings source.
- Only `revit_get_context` and `revit_query_family_types` were pre-approved. Every other tool would have needed a permission the non-interactive client cannot give.
- Revit facts came only from MCP responses.
- Counts, duplicate checks and ordering checks were computed outside the model from the MCP responses recorded in the client transcript. No Revit API, script, Dynamo, pyRevit or file was read for model data.

### Primary inventory benchmark: PASS

The client made one `revit_get_context` call, then followed `revit_query_family_types` with the nine tokens, no name filters, no `usage` and the default page size, page by page until the end.

| Page | `returned_count` | `complete` | `next_cursor` | Elapsed (ms) | Serialized chars |
|---:|---:|---|---|---:|---:|
| 1 | 100 | false | present | 1,767 | 39,739 |
| 2 | 100 | false | present | 413 | 40,129 |
| 3 | 100 | false | present | 251 | 39,675 |
| 4 | 100 | false | present | 222 | 39,463 |
| 5 | 100 | false | present | 254 | 40,151 |
| 6 | 100 | false | present | 217 | 41,437 |
| 7 | 100 | false | present | 222 | 41,767 |
| 8 | 100 | false | present | 441 | 41,609 |
| 9 | 100 | false | present | 201 | 41,223 |
| 10 | 100 | false | present | 291 | 39,428 |
| 11 | 100 | false | present | 289 | 39,291 |
| 12 | 100 | false | present | 205 | 39,100 |
| 13 | 5 | true | `null` | 213 | 2,267 |

Elapsed time is measured by the MCP client, from the tool call to the tool result. Page 1 also includes the first Bridge connection.

All 13 pages returned the same summary:

```text
matched_type_count     1205
distinct_family_count   196
placed_type_count       254
placed_instance_count  8278
used_family_count        87
unused_family_count     109
```

| Check | Result |
|---|---|
| All six summary numbers equal the reference | yes |
| Summary is identical on every page | yes, 13 of 13 |
| Sum of `returned_count` | 1,205, equal to `matched_type_count` |
| Rows walked / distinct `type_ref` / repeated `type_ref` | 1,205 / 1,205 / 0 |
| Distinct `family_ref` in the rows | 196 |
| Rows with `placed_instance_count > 0`; instance sum | 254; 8,278 |
| Families with at least one placed type / none | 87 / 109 |
| `is_used` disagrees with `placed_instance_count > 0` | 0 rows |
| Ordinal order (`category_token`, `family_name`, `type_name`, `type_ref`) across pages | 0 violations |
| Final page | `complete: true`, `next_cursor: null` |
| Largest page | 41,767 characters, under the 120,000-character budget |
| Accented French names | 73 rows. Examples exactly as returned: `Tt_ACO_Equipement_Divers` / `SYSTÈME AUDIO/VIDÉO`; `Tt_DDO_Equipement_Divers` / `ESPACE POUR CONTRÔLE D'ACCÈS SALLE D'ACCÈS` |
| Tool errors | none, 14 of 14 calls succeeded |

Rows per category:

- OST_ElectricalFixtures: 632
- OST_ElectricalEquipment: 181
- OST_LightingFixtures: 102
- OST_DataDevices: 73
- OST_SecurityDevices: 69
- OST_FireAlarmDevices: 65
- OST_LightingDevices: 56
- OST_NurseCallDevices: 14
- OST_CommunicationDevices: 13

Placement tokens observed were `work_plane_based` (927 rows) and `one_level_based` (278 rows). No row was in-place.

### Semantic spot checks: PASS

These ran in a separate isolated client process, using only `revit_query_family_types` after `revit_get_context`. Every multi-page query was walked to `complete: true` and `next_cursor: null`.

| Check | Query | Pages | Rows | Result |
|---|---|---:|---:|---|
| `OST_Walls` accepted | `category_tokens: ["OST_Walls"]` | 1 | 0 | Accepted with no error. All summary counts are 0 because wall types are not `FamilySymbol`. |
| Family-name filter | nine tokens, `family_name_contains: "Tt_DDO"` | 1 | 73 | All 73 rows contain `Tt_DDO`. This equals the 73 matching rows in the full walk. Summary: 4 families, 21 placed types, 664 instances. |
| Type-name filter | nine tokens, `type_name_contains: "salle"` | 1 | 9 | All 9 rows contain `SALLE`, so case-insensitive matching works. This equals the 9 matching rows in the full walk. |
| `usage: placed` | nine tokens | 3 | 254 | Every row has `placed_instance_count > 0`. Summary 254 / 87 families / 8,278 instances. |
| `usage: unused` | nine tokens | 10 | 951 | Every row has `placed_instance_count == 0`. 951 = 1,205 − 254. |

The spot-check client made 17 calls (1 context and 16 query pages), and none failed. No `type_ref` repeated within a walk, and every page of a walk had the same summary.

Family counts in a usage-filtered summary describe that filtered set, so they do not add up across filters. For example, the `unused` walk reports 171 families with at least one unused type. This follows the CAP-0014 summary definition.

Tt naming was used only as a filter value. It does not imply that a family is standards-approved.

### Stale cursor: FAIL

Procedure:

1. A client walk with `page_size: 5` on the nine tokens recorded page 1 and its `next_cursor` (206 characters).
2. **Control, before any edit:** a fresh client and server process reused that cursor. Page 2 came back. A restart without a cursor then returned page 1, with a `next_cursor` byte-identical to the saved one.
3. Dave made a harmless manual edit in Revit, in the active document `525413_E_EG_DAVE.OTIS`, outside the MCP client.
4. **Reuse after the edit:** the old cursor was accepted and returned page 2 with no error. The restart without a cursor succeeded and again returned a `next_cursor` byte-identical to the old one. Expected: `STALE_CURSOR`.
5. Dave confirmed the edit was committed, and then made a second committed edit.
6. **Reuse after the second edit:** same result. The old cursor was accepted, and the restarted query's cursor was byte-identical to the old one.

| Step | Expected | Observed |
|---|---|---|
| Reuse the old cursor after a committed edit | `STALE_CURSOR` | success, page 2 (twice) |
| Restart without a cursor | success | success |

No MCP write tool was used to change the model.

The add-in's document query revision did not advance after the edits. That is why the minted cursor stayed byte-identical. The revision is advanced by the add-in's `DocumentChanged`, `DocumentSynchronizedWithCentral` and `DocumentReloadedLatest` subscription in `RevitDocumentQueryChangeEventSource`. This run did not determine whether the subscription is missing in production, whether it fires but fails to resolve the event's document to its `document_id`, or something else. No product code was changed.

### Safety observations

- No MCP write, preview, review or apply tool was called. No approval UI was requested.
- None of the query responses changed. The six summary numbers stayed the same before and after the human edits, and only Dave's manual edits changed the document.
- MCP alone cannot prove that no transaction was opened. The document was already `is_modified: true` before the first query.
- No linked document was needed. Every row came from the active host document.
- The client wrote no Python, C# or Dynamo script, and no Revit API code.
- No tool call failed because of a Revit API, runtime or version quirk. Across all six client runs there were 42 BIM-IT calls (6 context, 36 query pages), with 0 failures.

### Comparison with the pyRevit reference run

| | pyRevit MCP reference | BIM-IT, this run |
|---|---|---|
| Revit access | Generated Python through `run_query` | Typed MCP calls only |
| Attempts for the inventory | 10 `run_query` attempts, 5 failed (API, serialization and scope errors) | 1 context call and 13 query pages, 0 failed |
| Output shape | Different for each script | One schema on every page |
| Completeness | Complete final inventory; assembled by the agent | Complete; shown by `complete: true`, `next_cursor: null`, and 1,205 unique `type_ref` values |
| Arbitrary code | yes | none |
| Summary numbers | 196 / 1,205 / 254 / 8,278 / 87 / 109 | Same six numbers |
| Elapsed | Not comparable; it was measured with a different method | About 5.0 s of tool time over 13 pages |

BIM-IT produced the same inventory with no failed calls and no generated code. The stale-cursor guarantee, which pyRevit does not offer, did not work live in this run.

## Checklist

- [x] Revit version and build recorded
- [x] Commit SHA recorded
- [x] Active document identity recorded
- [x] Summary for the nine benchmark categories matches the reference numbers
- [x] Full cursor walk returns every type once
- [ ] A model change after the first page returns `STALE_CURSOR` — FAIL, the old cursor was accepted after two committed human edits
- [x] Restart without a cursor succeeds after that change
- [x] `OST_Walls` is accepted even though it is outside the MEP placement allowlist
- [ ] No transaction, link traversal, or parameter read is observed — partly: no link traversal and no write tool observed; MCP cannot observe transactions
- [x] Result stays inside the 120,000-character read budget
- [ ] v21-only host returns `BRIDGE_PROTOCOL_INCOMPATIBLE` — NOT RUN live

Do not mark this document PASS from automated tests.
