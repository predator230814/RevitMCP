# Direct Claude Code validation of BIM-IT Revit MCP with the revit-model-query Skill

Status: live run recorded on 2026-10-30 for PR #96 ([ADR-0022](adr/ADR-0022-agent-skills-and-knowledge-architecture.md)). This is not a merge verdict.

Purpose: measure how usable the existing generic MCP surface is to a strong external agent, as it is today. No Server, Bridge, or add-in code was changed during the run. Gaps are recorded, not fixed.

## Setup

- Host: Revit 2026.5, build `26.5.0.55`. Active document **Snowdon Towers Sample Electrical**, unmodified. Five loaded links: Architectural, Structural, HVAC, Plumbing, Site.
- Add-in, Server, and Assistant: Release outputs built from the PR #96 branch (branched from `main` at `4b01f5e`). Server, Bridge, and Contracts code is identical to `main`.
- Client: Claude Code CLI 2.1.260, headless (`claude -p`), model `claude-opus-5[1m]`, launched as an independent process (desktop-session variables removed).
- MCP: only `BimIt.Revit.Server.exe` over stdio, through `--mcp-config` with `--strict-mcp-config`. pyRevit MCP, Nonica, and BIM-IT Assistant were not loaded and could not be reached.
- Tools available to Claude: the built-in `Skill` tool and the 11 BIM-IT read tools. Every preview, review, and apply tool was in `--disallowedTools` and did not appear in the session tool list. No other built-in tool (shell, file, web) was available.
- Skill: `skills/built-in/revit-model-query/SKILL.md` copied unchanged to `.claude/skills/revit-model-query/` in the run folder, with `--setting-sources project`.
- Prompts were the exact sentences below. No schema hint, retry, or follow-up was given.

## Results

| # | Prompt | Skill | MCP tools (in order) | MCP calls | Retries | Schema coaching | Correct |
|---|---|---|---|---|---|---|---|
| 0 | Use the BIM-IT Revit MCP tools to tell me which Revit version and document are active, and whether any Revit links are loaded. | not loaded | `revit_get_context`, `revit_query_elements` (RVT Links), `revit_read_spatial_rooms` | 3 | 0 | none | yes |
| 1 | Find all linked rooms whose name contains Studio and summarize them. | `revit-model-query` | `revit_get_context`, `revit_read_spatial_rooms` (`name_contains: Studio`) | 2 | 0 | none | yes |
| 2 | Compare Studio Unit 203 and Two Story Studio Unit 406. | `revit-model-query` | `revit_get_context`, `revit_read_spatial_rooms` (`name_contains: Studio`) | 2 | 0 | none | yes |
| 3 | Which loaded MEP family types contain Tt_INS? | `revit-model-query` | `revit_get_context`, `revit_get_mep_device_types` (`search: Tt_INS`), `revit_query_elements` (`text_contains`) | 3 | 0 | none | consistent with the tools (see notes) |
| 4 | What Revit data would you need to decide which studio entrance connects to the corridor? | `revit-model-query` | `revit_get_context`, `revit_read_spatial_rooms` (Studio), `revit_read_spatial_rooms` (Corridor) | 3 | 0 | none | yes |

No tool call failed. No permission denial occurred. Claude never asked for an ElementId, never invented a reference, never tried to load a family, and never asked for a specialized tool. In cases 1 to 4 it loaded the Skill first, then used 2 or 3 generic reads.

### 1. Linked studios

Claude found 6 linked rooms, all from the Architectural link: Studio Units 203, 304, 404, 504 and Two Story Studio Units 204, 406, each with level, door count, the door to its level's corridor, and window count. It noted that no host room matched and the result was not truncated. This matches the six linked studios recorded in the PR #94 and PR #95 lives.

It computed areas itself from the boundary loops (about 55.1 m² and 52.9 m²) and said so, because no Area value is returned. It also observed that `centroid` differs between identical loops, so it is a location point, not an area centre.

### 2. Compare 203 and 406

Claude compared level, area, perimeter, vertex count, shape (rectangle vs stepped L), inner loops, doors (corridor, interior, exterior), and windows, and listed what it could not observe. It flagged two window features in 406 at the identical point and compared 406 with 204 to suggest a modelling issue. The corridor doors match the PR #95 live (203 → Corridor 201, 406 → Corridor 402). A dedicated compare tool was not needed.

### 3. Tt_INS types

`revit_get_mep_device_types` returned 0 items, not truncated. Claude then tried a document text query, which also returned 0, and reported "none" with the correct caveat that the catalog only covers types BIM-IT can place as MEP devices. Whether a `Tt_INS` family exists in another category of this document was not checked independently, because no generic read lists all loaded family types.

### 4. Entrance reasoning

Claude named the facts it needed: room identity on both sides, door features on the room boundary with `other_side_name` / `other_side_number` / `other_side_status`, and reciprocity (the same door seen from the corridor). It explained why door elements themselves cannot be read for a linked model. It then read studios and corridors and answered: each studio has exactly one door whose other side is a corridor, confirmed from the corridor side at the same coordinate. It noted that some corridor doors report `other_side_status: unknown`.

## Data volume

`revit_read_spatial_rooms` returned about 10,000 characters for six studios, and about 10,000 for the corridors. All of it was used: boundaries for geometry, features for openings. No case was flooded with unused data. `revit_get_context` (442 characters) was called first every time, as the Skill asks. Case 2 read all six studios to compare two, because there is no exact-name or room-reference filter.

## Missing generic capabilities

These are candidates for a future generic primitive or an improvement to an existing read. None was added.

1. **Room quantities for linked rooms.** `revit_read_spatial_rooms` returns no Area, Perimeter, Volume, or unbounded height. Claude had to compute area itself and could not confirm that a "Two Story" room is double height.
2. **`centroid` meaning.** The field is the Revit location point, not the area centroid. Its name misleads agents. Document it or rename it in a later contract version.
3. **Exact room selection.** No filter by exact name, number, or room reference, so comparing two rooms reads every match of a name fragment.
4. **Loaded type catalog beyond placeable MEP devices.** There is no generic read of all loaded family types by name across categories, so "which loaded types contain X" can only be answered for BIM-IT-placeable MEP device types.
5. **Duplicate opening features.** Two window features at one point in 406 were returned as separate entries without explanation.
6. **Unresolved door other side.** Some corridor doors return `other_side_status: unknown`. A complete corridor adjacency list would need that resolved.

## BIM-IT Assistant with the same Skill: architecture evidence

After the Claude Code run, the product owner exercised the same linked-studio question in BIM-IT Assistant, in the same Revit session. The Assistant loaded `revit-model-query`, but its model could not follow it. The Skill points to `revit_read_spatial_rooms`. The Assistant model only saw the five legacy wrappers (`bimit_get_model_context`, `bimit_get_warnings`, `bimit_get_active_view_elements`, `bimit_inspect_selection`, `bimit_explore_selected_mep`), and none of them reads linked rooms.

This is a model tool-surface mismatch, not a linked-room capability gap: the same Server capability answered correctly for Claude Code. A Skill written against generic MCP tools is not portable to an agent that cannot see them. ADR-0022 was amended before acceptance. The Assistant model now sees the generic read-only Revit MCP tools under their MCP names, discovered from Server metadata, with `instance_id` and `document_id` injected by the Assistant. The shipped `SKILL.md` was not changed.

### Focused Assistant retest after the amendment

Run by the product owner on 2026-10-30 against Release outputs from `9a7e38d`, Revit 2026.5, Snowdon Towers Sample Electrical with the Architectural link loaded. BIM-IT Assistant, New Chat, provider GitHub Copilot (model shown as `gpt-6-luna`). No schema coaching.

| Prompt | Used | Result |
|---|---|---|
| Trouve toutes les pièces liées dont le nom contient Studio et résume-les. | `revit_get_context`, `revit_read_spatial_rooms`, Read skill | **Partial.** Correct tools, correct link, no identity asked. The answer listed 4 studios (203, 204, 304, 406), said "at least 4", and said the full tool response was too large to consult. 404 and 504 were missing. |
| Compare Studio Unit 203 et Two Story Studio Unit 406. | `revit_get_context`, `revit_read_spatial_rooms` | **Pass.** Generic reads only. Levels, plan extents, shape, doors to corridor and outside, the duplicated window in 406, and the absence of area data, matching the direct Claude Code answer. |

Settings → Skills: Disable, Enable, and Reload each worked and showed their message. Undo was empty after both prompts. No mutation.

The tool-surface mismatch is fixed: the Assistant followed the unchanged Skill to `revit_read_spatial_rooms`. The remaining failure is different. Claude Code received the full result for six studios (about 10,000 characters) and listed all six. In the Assistant, the GitHub Copilot runtime applied its large tool-output handling and the model did not see the whole result. This is a provider-side result-size limit, not a missing Revit capability. It was not worked around during the retest.

Cause and fix: `SessionConfigBase.LargeOutput` in SDK 1.0.21 writes a tool result above the runtime threshold to a temp file and gives the model only a reference. The Assistant session has no file tools, so the model saw a partial answer. The Copilot adapter now sets `LargeOutput = { Enabled = false }`. Results stay bounded by the Server, and the read facade refuses any result above 120,000 characters with `result_too_large` and a request to narrow the call, never a silent cut. No Server, Bridge, or add-in change. The linked-studio prompt must be retested against this build.

## Not covered

- Transcripts were kept locally and are not committed.
