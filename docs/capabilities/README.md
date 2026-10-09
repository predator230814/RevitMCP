# Capability Specifications

This directory contains product and technical specifications for RevitMCP capabilities exposed to agents.

Capability specifications define behavior before implementation. They are not Architecture Decision Records: ADRs record durable architectural choices, while capability specifications define individual agent-usable behaviors within the accepted architecture.

## Current specifications

- `CAP-0001-revit-get-context.md` — bounded current Revit application/document/view/selection context.
- `CAP-0002-revit-query-elements.md` — bounded active-document/view element query returning opaque `element_ref` values.
- `CAP-0003-revit-get-elements.md` — bounded inspection of known `element_ref` values with explicit basic-field and named-parameter projection.
- `CAP-0004-revit-describe-parameters.md` — bounded visible-parameter discovery returning opaque `parameter_ref` identity and data-type semantics, without values.
- `CAP-0005-revit-get-parameter-values.md` — bounded typed reads of explicit `element_ref + parameter_ref` pairs. Implemented. Official MCP-client-to-Revit-2026.5 live validation is **PASS**.
- `CAP-0006-revit-get-mep-topology.md` — bounded physical MEP element-to-element topology from known seeds. Implemented through Contracts/Addin/typed Bridge and the MCP tool `revit_get_mep_topology`. Typed Bridge live validation on Revit 2026.5 is **PASS**. Official MCP live validation is **PASS**.
- `CAP-0007-revit-preview-parameter-updates.md` — **Accepted**. Contracts implemented. Addin preview implemented. Typed local Bridge protocol v8 implemented. Server/MCP tool `revit_preview_parameter_updates` is implemented. Official MCP-client to stdio Server to Bridge v8 to Revit 2026.5 validation is **PASS**. The 2026-09-30 write-eligibility amendment is implemented in the Addin. Typed Bridge live replay on Revit 2026.5 at SHA `47465bdcd2ac569307648e9a37ba7053ac8fd56a` is **PASS** for A/B/C/E. Case D was not run because the selected candidate was a string. No apply capability.
- `CAP-0008-revit-apply-parameter-updates.md` — **Accepted** on 2026-10-07. Conforms to Accepted ADR-0011. Includes `audit_failed`. The apply-attempt store and the local ADR-0011 audit writer exist. The CAP-0008 apply implementation, Bridge protocol v10, and `revit_apply_parameter_updates` are implemented. Official MCP live functional validation on Revit 2026.5 build `26.5.0.55` at SHA `517bca8ddb8ecf3bab11cb14738ef8e0c9937353` is **PASS**. `tools/list` was exactly 9. Induced Revit failure-dialog behavior remains deferred validation debt.
- `CAP-0009-revit-get-warnings.md` — **Accepted** on 2026-10-09 by Dave, Product Owner. Bounded read of persistent document failures from `Document.GetWarnings()`. Implemented as `revit_get_warnings` on bridge protocol 11. Live typed-Bridge validation and official MCP live validation have not been run.

## Required sections

A capability specification should define, where applicable:

- purpose and agent-facing usage;
- operation class (`Read`, `Write`, or `Destructive`);
- MCP-facing input and output shape;
- transport-neutral request/result contracts;
- Revit execution behavior;
- deterministic errors;
- security and data-minimization considerations;
- performance/context-size expectations;
- acceptance criteria;
- explicitly deferred behavior.

## Naming

Use sequential names:

`CAP-0001-short-capability-name.md`

## Policy

Capability implementations must conform to the accepted specification. If implementation needs materially different behavior, update and review the specification before normalizing the difference in code.
