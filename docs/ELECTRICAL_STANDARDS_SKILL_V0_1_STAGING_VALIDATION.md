# Electrical Standards Skill v0.1 — staging validation record

- Status: Validation record. Not an approved standard and not a merge verdict.
- Date: 2026-11-09
- Plan: [ELECTRICAL_STANDARDS_SKILL_V0_1.md](ELECTRICAL_STANDARDS_SKILL_V0_1.md)
- Governance: [SKILLS.md](SKILLS.md#knowledge-governance-observed-candidate-approved)

This record documents the first local staging build of the firm-owned `electrical-standards` EXTERNAL Skill and what a live Assistant test did and did not show. The Skill content is firm and model data. It is not in this repository and is not published. This record contains no family, type, or parameter names from it.

## What was built

- One EXTERNAL Skill folder, kept outside this repository: `SKILL.md` plus 16 reference files (17 files, about 186 KB).
- Every entry has `knowledge_state: observed`. Nothing is `candidate` or `approved`.
- Nine category tokens in scope: `OST_ElectricalFixtures`, `OST_ElectricalEquipment`, `OST_LightingFixtures`, `OST_LightingDevices`, `OST_DataDevices`, `OST_CommunicationDevices`, `OST_FireAlarmDevices`, `OST_SecurityDevices`, `OST_NurseCallDevices`.
- Facts came only from the BIM-IT Revit MCP tools `revit_query_family_types` (CAP-0014) and `revit_query_parameter_bindings` (CAP-0015), paged to `complete=true`. Revit 2026.5 build `26.5.0.55`.
- Raw responses were frozen and hashed (SHA-256 list). The Skill was shaped deterministically by a small .NET tool, not Python. Families are sharded by category, and `OST_ElectricalFixtures` is split in two. The largest reference file is 36,072 characters, under the 50,000 limit.
- No `instance_id`, `document_id`, `family_ref`, `type_ref`, or `parameter_ref` value was persisted. A scan of the output against every captured value found none.
- The model reported the same `is_modified` value before and after capture. No model write was made.
- `worksets.yaml` records `capture_status: missing_fact`. Workset is still not exposed by any generic read.

## Results

| Check | Result |
|---|---|
| Structural validation with the existing `BimIt.Revit.Skills` registry | Ready. All 17 files readable through the model-facing read, none truncated. |
| Added through Settings → Skills → Add skills folder | `ready`. The Assistant showed the same SHA-256 as the structural validation. |
| Family question (first five lighting-fixture types, placed or unplaced, approval state) | Passed. The five types and their placed counts match the reference file. The agent said the entries are observed, not approved. |
| Parameter question (five shared parameters with data type and categories) | Passed on content. See limitation 4 for one name fidelity issue. The agent said the entries are observed. |

## Limitations observed

1. **Progressive disclosure is not verifiable from the Assistant panel.** Each answer shows only "Used: Read skill". The panel does not list which reference files were read. The answers match the expected category and shard, but that this agent loaded only the index plus one file is not proven by this test. Verification needs the per-call detail from Diagnostics or logs.
2. **Markdown tables do not render.** The Assistant panel shows table answers as raw `|` text. This is a panel display limit, not a Skill defect.
3. **The panel does not show which GitHub account backs the Copilot provider.** A free-plan account produced a licence error. A Business-licensed account worked. The user had to check the account outside the Assistant. A visible account or plan indicator would help.
4. **The agent can alter identifiers when it restates them.** In the parameter answer one name was returned without its accent (`Capacité` became `Capacite`). The Skill file has the exact name and a GUID. Agents should use the GUID or a live read to match a parameter, not a restated name.
5. **Adding the folder needs a manual UI step.** The folder picker is trusted UI. Nothing here automates around it.
6. **Observed is not approved.** The Skill describes one benchmark model. It is not a firm standard until a BIM lead reviews and promotes entries.
7. **Build environment.** The repository `global.json` requires SDK 10.0.204. The capture machine had SDK 8.0.427 only, so the existing source-equivalent Release binaries were used. Their hashes match the CAP-0015 validation record.

## Not claimed

- No claim that the Skill is complete, correct as a standard, or ready for the firm.
- No claim about per-file read behaviour (limitation 1).
- No Standard Pack, no MCP Skills extension adapter, and no workset capability exist or are proposed here.
