# Capability Specifications

This directory contains product and technical specifications for RevitMCP capabilities exposed to agents.

Capability specifications define behavior before implementation. They are not Architecture Decision Records: ADRs record durable architectural choices, while capability specifications define individual agent-usable behaviors within the accepted architecture.

## Current specifications

- `CAP-0001-revit-get-context.md` — bounded current Revit application/document/view/selection context.
- `CAP-0002-revit-query-elements.md` — bounded active-document/view element query returning opaque `element_ref` values.
- `CAP-0003-revit-get-elements.md` — bounded inspection of known `element_ref` values with explicit basic-field and named-parameter projection.
- `CAP-0004-revit-describe-parameters.md` — bounded visible-parameter discovery returning opaque `parameter_ref` identity and data-type semantics, without values.

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
