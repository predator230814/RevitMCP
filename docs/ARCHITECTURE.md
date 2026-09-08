# RevitMCP Architecture

## Status

This document describes the current architectural direction. It is intentionally minimal while foundational decisions are still being researched and recorded.

## Architectural intent

RevitMCP should separate concerns so the Revit capability layer can evolve independently from MCP transports, AI clients, and user interfaces.

Target conceptual layers:

1. **Protocol / transport layer**
   - MCP protocol concerns
   - transport-specific hosting
   - client-facing schemas

2. **Application / capability layer**
   - coherent Revit capabilities exposed to agents
   - validation and orchestration
   - structured results and error handling

3. **Revit integration layer**
   - translation between application requests and Revit-specific operations
   - Revit API adapters and services

4. **Revit execution layer**
   - execution in the valid Revit API context
   - transaction boundaries
   - thread and lifecycle constraints

5. **Cross-cutting concerns**
   - authentication and authorization
   - observability and diagnostics
   - security
   - configuration

## Architectural invariants

- Core Revit functionality must not depend on a specific LLM vendor.
- Client-specific integrations must not leak into core Revit logic.
- Read, write, and destructive operations must be distinguishable at the contract level.
- Tool contracts should be deterministic and structured.
- Revit API execution-context constraints must be respected explicitly.
- The project should complement Autodesk's official Revit MCP where appropriate.
- WebMCP, MCP Apps, and remote MCP scenarios should remain possible without forcing the core architecture to depend on them.

## Decisions intentionally not made yet

The following are open architectural questions and must not be treated as settled:

- implementation language and MCP SDK;
- process topology between MCP host and Revit;
- local IPC mechanism, if any;
- supported MCP transports and hosting model;
- authentication and authorization model;
- packaging and deployment strategy;
- supported Revit versions;
- exact relationship with Autodesk's official Revit MCP;
- extent and form of WebMCP integration.

These decisions should be made through research, discussion, and ADRs.

## Change policy

Significant architecture changes require an ADR. If code and architecture documentation conflict, the conflict must be surfaced and resolved rather than silently normalized.
