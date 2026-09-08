# RevitMCP Current State

_Last updated: 2026-09-08_

## Phase

Project foundation / architecture discovery.

## What exists

- GitHub repository initialized.
- Canonical AI-agent working rules established in `AGENTS.md`.
- Project vision defined in `docs/VISION.md`.
- Minimal architecture baseline defined in `docs/ARCHITECTURE.md`.
- ADR process initialized under `docs/adr/`.

## What does not exist yet

- no Revit add-in;
- no MCP server;
- no selected implementation language or SDK;
- no selected transport or IPC mechanism;
- no tool catalog;
- no automated tests;
- no deployment or packaging model.

## Current priorities

1. Research the current MCP, Autodesk Revit MCP, WebMCP, MCP Apps, SDK, transport, and agent ecosystem.
2. Define the architectural boundaries and system topology.
3. Decide the initial technology stack through ADRs.
4. Define the first read-only capability contract.
5. Only then create the initial implementation skeleton.

## Known constraints

- Revit API execution-context and transaction constraints must be respected.
- The solution should remain usable by multiple MCP-compatible clients and LLMs where technically possible.
- Autodesk's official Revit MCP must be evaluated before duplicating capabilities.
- WebMCP must remain in scope as an emerging integration surface.

## Next decision

Determine the recommended high-level system topology for RevitMCP after current ecosystem research.
