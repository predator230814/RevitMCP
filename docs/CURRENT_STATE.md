# RevitMCP Current State

_Last updated: 2026-09-08_

## Phase

Architecture foundation / solution structure and multi-version build design.

## What exists

- GitHub repository initialized.
- Canonical AI-agent working rules established in `AGENTS.md`.
- Project vision defined in `docs/VISION.md`.
- Architecture baseline defined in `docs/ARCHITECTURE.md`.
- ADR process initialized under `docs/adr/`.
- ADR-0001 accepts an out-of-process architecture with a standalone C#/.NET MCP server and a Revit capability/execution add-in.
- ADR-0002 accepts duplex Windows Named Pipes with JSON-RPC 2.0 for the local bridge, using Microsoft StreamJsonRpc as the initial implementation behind a RevitMCP-owned abstraction.
- ADR-0003 accepts opaque per-process Revit `instance_id` values, ephemeral LocalAppData registration records, bridge validation, and deterministic multi-instance selection behavior.
- Revit 2025, 2026, and 2027 are the initial supported product targets from one shared codebase, with version-specific builds/adapters where required.
- The initial MCP transport is `stdio`; Streamable HTTP, MCP Apps, WebMCP, Azure/cloud gateways, and other remote deployment paths remain extensions rather than core dependencies.
- Product UI considerations are recorded separately; universal access, conversational use inside or adjacent to Revit, and reduced context switching remain open product goals rather than settled architecture.

## What does not exist yet

- no Revit add-in implementation;
- no MCP server implementation;
- no finalized solution/project structure or multi-version build implementation;
- no detailed bridge handshake schema or protocol negotiation contract;
- no explicit document identity/addressing model;
- no request scheduling/fairness policy for multiple clients;
- no write-locking or transaction concurrency policy;
- no tool catalog;
- no automated tests;
- no deployment or packaging model;
- no finalized user-facing Revit UI strategy.

## Current priorities

1. Define the initial solution/project structure and multi-version build strategy for Revit 2025-2027.
2. Define the first read-only capability contract.
3. Define the minimum bridge handshake/protocol-version contract required by the initial implementation.
4. Define the Revit execution queue behavior needed for the first capability.
5. Only then create the initial implementation skeleton.

## Known constraints

- Revit API execution-context and transaction constraints must be respected.
- The solution should remain usable by multiple MCP-compatible clients and LLMs where technically possible.
- Existing Autodesk and third-party Revit MCP implementations should be benchmarked for useful patterns, limitations, interoperability opportunities, and product gaps; they do not determine whether RevitMCP should continue or whether an overlapping capability should exist.
- The architecture should preserve viable paths for both local agents and remote/cloud agents, subject to security, information-governance, and deployment-policy requirements.
- Local Revit IPC is intentionally separate from remote/cloud connectivity; future Azure or other hosted components must not require exposing the local Named Pipe directly.
- Revit process IDs are diagnostics, not the stable RevitMCP client-facing identity. ADR-0003 defines `instance_id` for process-lifetime identity.
- Instance discovery records are candidates only; a validated bridge handshake is required before an instance is considered ready.
- Revit instance identity is distinct from document identity.
- WebMCP must remain in scope as an emerging integration surface.
- The public repository must not contain confidential internal discussions, project information, credentials, or organization-specific sensitive details.
- Arbitrary AI-generated code execution inside Revit is not part of the normal production capability surface.
- The Revit UI strategy remains open. A future control/approval surface or richer conversational experience may be considered without changing the core capability and bridge architecture.

## Next decision

Define the repository solution/project structure and the build/reference strategy that lets one maintainable C# codebase target Revit 2025, 2026, and 2027 while isolating Revit-version-specific dependencies and keeping the MCP server and shared contracts independently testable.
