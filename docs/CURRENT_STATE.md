# RevitMCP Current State

_Last updated: 2026-09-08_

## Phase

Architecture foundation / local bridge design.

## What exists

- GitHub repository initialized.
- Canonical AI-agent working rules established in `AGENTS.md`.
- Project vision defined in `docs/VISION.md`.
- Architecture baseline defined in `docs/ARCHITECTURE.md`.
- ADR process initialized under `docs/adr/`.
- ADR-0001 accepts an out-of-process architecture with a standalone C#/.NET MCP server and a Revit capability/execution add-in.
- Revit 2025, 2026, and 2027 are the initial supported product targets from one shared codebase, with version-specific builds/adapters where required.
- The initial MCP transport is `stdio`; Streamable HTTP, MCP Apps, WebMCP, and remote/cloud gateways remain future extension paths rather than core dependencies.

## What does not exist yet

- no Revit add-in implementation;
- no MCP server implementation;
- no selected local IPC mechanism between server and Revit;
- no finalized Revit instance discovery/addressing model;
- no tool catalog;
- no automated tests;
- no deployment or packaging model.

## Current priorities

1. Evaluate and choose the local bridge / IPC mechanism between `RevitMCP.Server` and `RevitMCP.Addin`.
2. Define Revit instance discovery and addressing for multiple simultaneous Revit processes.
3. Define the initial solution/project structure and multi-version build strategy.
4. Define the first read-only capability contract.
5. Only then create the initial implementation skeleton.

## Known constraints

- Revit API execution-context and transaction constraints must be respected.
- The solution should remain usable by multiple MCP-compatible clients and LLMs where technically possible.
- Existing Autodesk and third-party Revit MCP implementations should be benchmarked for useful patterns, limitations, interoperability opportunities, and product gaps; they do not determine whether RevitMCP should continue or whether an overlapping capability should exist.
- The architecture should preserve viable paths for both local agents and remote/cloud agents, subject to security, information-governance, and deployment-policy requirements.
- WebMCP must remain in scope as an emerging integration surface.
- The public repository must not contain confidential internal discussions, project information, credentials, or organization-specific sensitive details.
- Arbitrary AI-generated code execution inside Revit is not part of the normal production capability surface.

## Next decision

Choose the local IPC mechanism between the standalone MCP server and the in-process Revit add-in, including its implications for security, multi-instance discovery, lifecycle, debugging, packaging, and future remote gateways.
