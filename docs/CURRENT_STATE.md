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

1. Research the current MCP, Autodesk Revit MCP, Nonica, pyRevit-based approaches, WebMCP, MCP Apps, SDK, transport, and agent ecosystem.
2. Define the architectural boundaries and system topology.
3. Decide the initial technology stack through ADRs.
4. Define the first read-only capability contract.
5. Only then create the initial implementation skeleton.

## Known constraints

- Revit API execution-context and transaction constraints must be respected.
- The solution should remain usable by multiple MCP-compatible clients and LLMs where technically possible.
- Existing Autodesk and third-party Revit MCP implementations should be benchmarked for useful patterns, limitations, interoperability opportunities, and product gaps; they do not determine whether RevitMCP should continue or whether an overlapping capability should exist.
- The architecture should preserve viable paths for both local agents and remote/cloud agents, subject to security, information-governance, and deployment-policy requirements.
- WebMCP must remain in scope as an emerging integration surface.
- The public repository must not contain confidential internal discussions, project information, credentials, or organization-specific sensitive details.

## Next decision

Determine the recommended high-level system topology for RevitMCP after current ecosystem research.
