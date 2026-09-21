# RevitMCP Architecture

## Status

This document describes the current architectural direction. It is intentionally minimal while foundational decisions are still being researched and recorded.

## Architectural intent

RevitMCP should separate concerns so the Revit capability layer can evolve independently from MCP transports, AI clients, deployment models, and user interfaces.

Target conceptual layers:

1. **Protocol / transport layer**
   - MCP protocol concerns
   - transport-specific hosting
   - client-facing schemas

2. **Application / capability layer**
   - coherent Revit capabilities exposed to agents
   - validation and capability/request coordination
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
- Autodesk's official Revit MCP and relevant third-party implementations are reference points and interoperability opportunities, not architectural authorities for RevitMCP.
- Capability overlap with an existing MCP is acceptable when RevitMCP has a clear reason such as broader Revit-version support, stronger agent usability, controlled write operations, deployment flexibility, workflow specialization, security, or reliability.
- The core should not assume that either local-only or cloud-only deployment is universally available; deployment topology must remain separable from Revit capability logic.
- WebMCP, MCP Apps, remote MCP scenarios, and other emerging integration surfaces should remain possible without forcing the core architecture to depend on them.
- RevitMCP remains a specialized Revit capability service. Autodesk cloud capability such as APS, ACC, and Forma is logically separated from the Revit-local MCP boundary when implemented. ADR-0007 does not decide whether those cloud capabilities use one MCP service or several.
- Multi-service workflow orchestration, if introduced, is external and optional. Ordinary MCP clients must be able to use RevitMCP without an orchestrator.
- The agent-facing tool surface for a workflow should remain small and coherent as the capability catalogue grows. The scoping mechanism is deferred; a hard tool-count limit is not an invariant.

## Decisions intentionally not made yet

The following remain open architectural questions and must not be treated as settled:

- remote/additional MCP transports, gateways, and hosting model beyond the accepted initial local `stdio` transport;
- authentication and authorization model, except that passing tokens through model context is not an acceptable design direction (ADR-0007);
- packaging and deployment strategy;
- exact interoperability and overlap strategy with Autodesk and third-party Revit MCP implementations;
- extent and form of WebMCP integration;
- Autodesk-cloud MCP service topology (one coherent service vs several specialized services);
- multi-service orchestration implementation technology, if introduced;
- future tool-surface scoping mechanism.

These remaining decisions should be made through research, discussion, and ADRs. Do not treat them as implementation work authorized by this document.

## Ecosystem evaluation rule

Architecture research must compare relevant implementations critically. The purpose of comparison is to identify useful patterns, failure modes, interoperability opportunities, and product gaps. It must not turn another implementation's current limitations, roadmap, or architecture into a default constraint on RevitMCP.

## Change policy

Significant architecture changes require an ADR. If code and architecture documentation conflict, the conflict must be surfaced and resolved rather than silently normalized.
