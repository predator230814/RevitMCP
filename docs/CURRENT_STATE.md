# RevitMCP Current State

_Last updated: 2026-09-08_

## Phase

Architecture foundation / Revit instance discovery design.

## What exists

- GitHub repository initialized.
- Canonical AI-agent working rules established in `AGENTS.md`.
- Project vision defined in `docs/VISION.md`.
- Architecture baseline defined in `docs/ARCHITECTURE.md`.
- ADR process initialized under `docs/adr/`.
- ADR-0001 accepts an out-of-process architecture with a standalone C#/.NET MCP server and a Revit capability/execution add-in.
- ADR-0002 accepts duplex Windows Named Pipes with JSON-RPC 2.0 for the local bridge, using Microsoft StreamJsonRpc as the initial implementation behind a RevitMCP-owned abstraction.
- Revit 2025, 2026, and 2027 are the initial supported product targets from one shared codebase, with version-specific builds/adapters where required.
- The initial MCP transport is `stdio`; Streamable HTTP, MCP Apps, WebMCP, Azure/cloud gateways, and other remote deployment paths remain extensions rather than core dependencies.

## What does not exist yet

- no Revit add-in implementation;
- no MCP server implementation;
- no finalized Revit instance identity/discovery/addressing model;
- no finalized Named Pipe endpoint naming or stale-registration cleanup model;
- no finalized solution/project structure or multi-version build implementation;
- no tool catalog;
- no automated tests;
- no deployment or packaging model;
- no finalized user-facing Revit UI strategy.

## Current priorities

1. Define Revit instance identity, discovery, addressing, endpoint naming, and stale-instance cleanup for multiple simultaneous Revit processes.
2. Define the initial solution/project structure and multi-version build strategy.
3. Define the first read-only capability contract.
4. Define bridge protocol/version negotiation details needed for the initial implementation.
5. Only then create the initial implementation skeleton.

## Known constraints

- Revit API execution-context and transaction constraints must be respected.
- The solution should remain usable by multiple MCP-compatible clients and LLMs where technically possible.
- Existing Autodesk and third-party Revit MCP implementations should be benchmarked for useful patterns, limitations, interoperability opportunities, and product gaps; they do not determine whether RevitMCP should continue or whether an overlapping capability should exist.
- The architecture should preserve viable paths for both local agents and remote/cloud agents, subject to security, information-governance, and deployment-policy requirements.
- Local Revit IPC is intentionally separate from remote/cloud connectivity; future Azure or other hosted components must not require exposing the local Named Pipe directly.
- WebMCP must remain in scope as an emerging integration surface.
- The public repository must not contain confidential internal discussions, project information, credentials, or organization-specific sensitive details.
- Arbitrary AI-generated code execution inside Revit is not part of the normal production capability surface.
- The Revit UI strategy remains open. A future control/approval surface or richer conversational experience may be considered without changing the core capability and bridge architecture.

## Next decision

Define how running Revit instances register themselves, how `RevitMCP.Server` discovers them, how each instance is identified and addressed, and how stale registrations are cleaned up without exposing raw operating-system process IDs as the only long-term client-facing identity.
