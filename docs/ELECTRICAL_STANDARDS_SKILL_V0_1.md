# Electrical Standards Skill v0.1 — design and capture plan

- Status: Plan. Not an approved standard.
- Date: 2026-11-07. Workset and missing-fact sections revised 2026-11-25. The 2026-11-09 staging record is unchanged.
- Governance: [SKILLS.md](SKILLS.md#knowledge-governance-observed-candidate-approved)
- Architecture: [ADR-0022](adr/ADR-0022-agent-skills-and-knowledge-architecture.md), [ADR-0023](adr/ADR-0023-declarative-revit-read-query-architecture.md), [ADR-0024](adr/ADR-0024-registry-driven-compiled-revit-read-operations.md), [ADR-0027](adr/ADR-0027-layered-revit-capability-admission.md), [ADR-0028](adr/ADR-0028-bounded-generated-csharp-read-executor.md)

This document plans how to build the first incremental electrical knowledge library from a good representative modeled project. Dave or another electrical/BIM lead then reviews, corrects, and approves it. It contains no company-specific family or parameter entries. Nothing here is an approved company standard, and the benchmark model is not one.

## Where it lives

The library is a firm-owned EXTERNAL Skill (Git, SharePoint, or OneDrive-synced folder), loaded through the existing EXTERNAL Skill mechanism. It is not placed in `skills/built-in`. No BIM-IT code, parser, or runtime changes.

Suggested content layout. This is a suggestion, not a new BIM-IT format requirement:

```text
electrical-standards/
├── SKILL.md
└── references/
    ├── families.yaml
    ├── parameters.yaml
    ├── worksets.yaml
    └── provenance.md
```

## Knowledge domains

1. Families and types.
2. Parameter definitions and bindings.
3. Targeted parameter observations where useful.
4. Worksets/sub-projects. Read these from the existing `project.user_worksets` primitive. See the workset section.

## `SKILL.md`

Keep it small. It should eventually tell an agent to:

- apply the knowledge only for electrical standards questions (the `description` line says when);
- distinguish `observed`, `candidate`, and `approved` content;
- read only the reference file relevant to the task;
- use live Revit MCP facts for the current model;
- never treat the benchmark model as authority;
- never infer approval from names;
- let explicit user instructions override the Skill;
- follow the existing controlled-write policy for any write. The Skill grants no write authority.

## `references/families.yaml`

Source facts can be discovered with `revit_query_family_types` ([CAP-0014](capabilities/CAP-0014-revit-query-family-types.md)). Candidate content:

- category;
- family;
- type;
- observed placed/unplaced usage;
- a proposed semantic role, only when explicitly marked `candidate`;
- provenance and review status.

Do not copy the entire benchmark model.

## `references/parameters.yaml`

Source facts can be discovered with `revit_query_parameter_bindings` ([CAP-0015](capabilities/CAP-0015-revit-query-parameter-bindings.md)). Targeted element facts come from `revit_describe_parameters` and `revit_get_parameter_values`. Candidate content:

- name;
- shared, local, or built-in identity;
- GUID where shared;
- data type;
- instance or type binding;
- categories;
- a proposed business meaning, only as `candidate` or `approved` content.

## `references/worksets.yaml`

On 2026-11-07 this plan classified user worksets as a missing fact. The 2026-11-09 staging record still says `capture_status: missing_fact` and is left as written. This revision does not open or rewrite the EXTERNAL Skill.

`project.user_worksets` contract version 1 is now the source for those observations. Read it with `revit_lookup` and `revit_read` ([CAP-0016](capabilities/CAP-0016-revit-read-operations.md)). Do not call `revit_run_csharp_query` to copy that inventory. Do not remove the operation. A later reviewed capture may replace `missing_fact` with `knowledge_state: observed` and human-readable provenance. Capture does not set `candidate` or `approved`. Do not persist `document_id`, `instance_id`, or `element_ref`.

## Missing Revit facts after v0.1

A fact that the current primitives and their composition do not express is handled in this order ([ADR-0027](adr/ADR-0027-layered-revit-capability-admission.md)):

1. Use the existing primitive when it answers the question.
2. Compose those facts in the Skill when composition is enough.
3. Evaluate [CAP-0020](capabilities/CAP-0020-bounded-generated-csharp-read.md) `revit_run_csharp_query` when the fact is required, read-only, still not expressible, and a focused query is compatible with [ADR-0028](adr/ADR-0028-bounded-generated-csharp-read-executor.md). Use `revit_api.symbols` only when the signature is uncertain. Skip the query when the fact is unnecessary or outside that policy. A kept result is `observed`, with model title, Revit build, UTC date, method, policy version, Revit API name and version, and the returned `source_sha256`. This creates no capability.
4. Promote to a compiled operation only for an explicit ADR-0027 item 4 reason: stable engineering meaning, repeated use large enough to justify product code, specialized geometry or topology, special identity, performance, a controlled write, or another reason of that kind. A Revit method, or a query used a few times, is not that reason.

The bounded read is live-complete under policy version 1 and default-off. Policy version 2 is the current deny list for `RevitAPI` 26.5.0.0. It is implemented, covered by automated tests, and live-validated by the 2026-11-26 Snowdon dogfood. That gate does not replace the 2026-11-25 policy version 1 records. Policy version 1 is the 2026-11-25 gate inventory. Arbitrary C# stays prohibited ([TECH_WATCH.md](TECH_WATCH.md)). The Assistant does not expose the tool. The Skill stays EXTERNAL.

## `references/provenance.md`

Human-readable evidence log: model state identified, query used, date, who reviewed, and the resulting state of each entry.

## v0.1 success condition

The first usable library does not need to be complete. Success means:

- the Skill loads through the existing EXTERNAL Skill mechanism;
- family and parameter observations are traceable to live or frozen evidence;
- every business rule has an explicit governance state;
- a BIM lead can edit and approve the files directly;
- the agent loads only the knowledge relevant to its task;
- a fact already covered by a primitive is read from that primitive. A required fact that primitives and composition cannot express may be evaluated with CAP-0020 and, when kept, stored as `observed` with provenance. Capture does not promote it to `candidate`, `approved`, or a new capability. A fact that is unnecessary, or outside the CAP-0020 policy, stays unqueried.
