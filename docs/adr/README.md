# Architecture Decision Records

This directory contains Architecture Decision Records (ADRs) for significant RevitMCP decisions.

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
