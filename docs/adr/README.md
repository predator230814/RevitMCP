# Architecture Decision Records

This directory contains Architecture Decision Records (ADRs) for significant RevitMCP decisions.

Accepted ADRs:

- [ADR-0001](ADR-0001-out-of-process-mcp-and-revit-capability-host.md) — out-of-process MCP server and Revit capability host
- [ADR-0002](ADR-0002-named-pipes-json-rpc-local-bridge.md) — Named Pipes and JSON-RPC local bridge
- [ADR-0003](ADR-0003-revit-instance-registration-discovery-and-addressing.md) — Revit instance registration, discovery, and addressing
- [ADR-0004](ADR-0004-solution-structure-and-multi-version-build.md) — solution structure and multi-version build
- [ADR-0005](ADR-0005-agent-context-and-token-efficiency.md) — agent context and token efficiency
- [ADR-0006](ADR-0006-document-and-element-reference-identity.md) — document and element reference identity
- [ADR-0007](ADR-0007-federated-mcp-boundaries-and-orchestration.md) — federated MCP boundaries and optional orchestration
- [ADR-0008](ADR-0008-controlled-write-safety-model.md) — controlled write safety model
- [ADR-0009](ADR-0009-agent-human-interaction-and-trusted-approval-boundary.md) — agent and human interaction, and the trusted approval boundary
- [ADR-0010](ADR-0010-revit-local-trusted-approval-authority.md) — Revit-local trusted approval authority and provider contract
- [ADR-0011](ADR-0011-v1-controlled-write-audit.md) — v1 controlled-write audit
- [ADR-0012](ADR-0012-bim-it-product-family-and-specialized-mcp-services.md) — BIM-IT product family and specialized MCP service identity
- [ADR-0013](ADR-0013-bim-it-assistant-local-architecture.md) — BIM-IT Assistant local architecture and trusted-boundary separation
- [ADR-0014](ADR-0014-provider-neutral-assistant-agent-runtime.md) — provider-neutral Assistant agent runtime and initial GitHub Copilot adapter
- [ADR-0015](ADR-0015-assistant-document-intake.md) — Assistant document intake as conversation context
- [ADR-0016](ADR-0016-assistant-structured-proposals-and-controlled-write-orchestration.md) — Assistant structured proposals and controlled-write orchestration. Accepted. Slice 1 stores an untrusted proposal; preview, review, and apply orchestration remain unimplemented.
- [ADR-0017](ADR-0017-pattern-intelligence-and-spatial-grounding.md) — Pattern intelligence and spatial grounding. Accepted. Replaces the unmerged PR #90 calibration draft. This slice is read-only.
- [ADR-0018](ADR-0018-pattern-family-resolution-and-controlled-application.md) — Pattern family resolution and controlled application. Accepted. Resolves loaded family types, previews pattern placements, and applies them through the existing approval.
- [ADR-0019](ADR-0019-linked-spatial-context-and-semantic-adaptation.md) — Linked spatial context and semantic pattern adaptation. Accepted. Reads host and linked rooms, keeps inner boundary loops, and adapts a stated pattern in review. No new write.
- [ADR-0020](ADR-0020-pattern-specification-v2.md) — Pattern specification v2. Accepted. Separates role, placement, anchor, count, and arrangement; removes `paired_symmetric`; adds per-room adaptation status. Ends at Pattern Review. No new write.
- [ADR-0021](ADR-0021-controlled-pattern-application-against-linked-architecture.md) — Controlled pattern application against linked architecture. Proposed. Places a resolved linked-room pattern in the MEP host through the existing approval and apply path. Linked wall devices host on a converted link face. Bridge protocol 21.

RevitMCP was renamed to BIM-IT Revit MCP. ADR-0001 through ADR-0011 keep the name used when they were accepted. See ADR-0012.

## When to create an ADR

Create an ADR when a decision materially affects architecture, interoperability, security, deployment, protocol behavior, Revit integration, or long-term maintainability.

Examples:

- selecting an MCP SDK or implementation language;
- choosing the process topology between Revit and the MCP host;
- choosing a local IPC or network transport;
- defining write-operation authorization and safety boundaries;
- deciding how RevitMCP interoperates with Autodesk's official Revit MCP;
- adopting an MCP extension, MCP App, or WebMCP integration pattern.

Do not create ADRs for routine implementation details that can be changed locally without architectural impact.

## Naming

Use sequential names:

`ADR-0001-short-decision-name.md`

## Status values

- Proposed
- Accepted
- Superseded
- Rejected

## Template

```markdown
# ADR-XXXX: Decision title

- Status: Proposed
- Date: YYYY-MM-DD

## Context

What problem or decision requires resolution?

## Decision drivers

What requirements and constraints matter most?

## Options considered

### Option A

Description, advantages, and disadvantages.

### Option B

Description, advantages, and disadvantages.

## Decision

What was decided?

## Consequences

What becomes easier, harder, constrained, or possible because of this decision?

## References

Authoritative sources, specifications, issues, or related ADRs.
```

## Policy

ADRs record decisions; they are not immutable truth. If a decision changes, create a new ADR that supersedes the old one rather than silently rewriting history.
