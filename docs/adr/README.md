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
