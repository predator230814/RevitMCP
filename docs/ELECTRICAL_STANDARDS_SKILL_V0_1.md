# Electrical Standards Skill v0.1 — design and capture plan

- Status: Plan. Not an approved standard.
- Date: 2026-11-07
- Governance: [SKILLS.md](SKILLS.md#knowledge-governance-observed-candidate-approved)
- Architecture: [ADR-0022](adr/ADR-0022-agent-skills-and-knowledge-architecture.md), [ADR-0023](adr/ADR-0023-declarative-revit-read-query-architecture.md)

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
4. Worksets/sub-projects, as a deliberately unresolved observation need.

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

Current generic BIM-IT reads do not expose workset as a query, filter, or projection fact. This is classified as a **missing-fact research case**. This plan does not create a capability, spec, or DTO field for it. The file records what a reviewer needs and the missing-fact status, not invented values.

The point is to test whether a future flexible Revit API path can answer long-tail facts like this without duplicating the Revit API in BIM-IT. That path is research only (see [TECH_WATCH.md](TECH_WATCH.md)).

## `references/provenance.md`

Human-readable evidence log: model state identified, query used, date, who reviewed, and the resulting state of each entry.

## v0.1 success condition

The first usable library does not need to be complete. Success means:

- the Skill loads through the existing EXTERNAL Skill mechanism;
- family and parameter observations are traceable to live or frozen evidence;
- every business rule has an explicit governance state;
- a BIM lead can edit and approve the files directly;
- the agent loads only the knowledge relevant to its task;
- missing Revit facts are recorded rather than invented or automatically turned into new C# capabilities.
