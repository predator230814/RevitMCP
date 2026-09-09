# RevitMCP Current State

_Last updated: 2026-09-08_

## Phase

Architecture foundation / bridge protocol and Revit execution queue design.

## What exists

- GitHub repository initialized.
- Canonical AI-agent working rules established in `AGENTS.md`.
- Project vision defined in `docs/VISION.md`.
- Architecture baseline defined in `docs/ARCHITECTURE.md`.
- ADR process initialized under `docs/adr/`.
- ADR-0001 accepts an out-of-process architecture with a standalone C#/.NET MCP server and a Revit capability/execution add-in.
- ADR-0002 accepts duplex Windows Named Pipes with JSON-RPC 2.0 for the local bridge, using Microsoft StreamJsonRpc as the initial implementation behind a RevitMCP-owned abstraction.
- ADR-0003 accepts opaque per-process Revit `instance_id` values, ephemeral LocalAppData registration records, bridge validation, and deterministic multi-instance selection behavior.
- ADR-0004 accepts a small SDK-style .NET solution with `Contracts`, `Bridge`, `Server`, and one multi-version `Addin` project.
- Revit 2025, 2026, and 2027 are built from the same add-in project using an explicit `RevitVersion` build property; the initial target matrix is `net8.0-windows` for Revit 2025/2026 and `net10.0-windows` for Revit 2027.
- Revit API references will be externally restored, version-pinned compile-time dependencies; Autodesk Revit API binaries will not be committed to the repository.
- Capability specifications are recorded under `docs/capabilities/` before implementation.
- CAP-0001 accepts `revit_get_context` as the first end-to-end read-only Revit capability. It returns bounded instance, active-document, active-view, and selection-count context without exposing paths or enumerating selection contents.
- The initial MCP transport is `stdio`; Streamable HTTP, MCP Apps, WebMCP, Azure/cloud gateways, and other remote deployment paths remain extensions rather than core dependencies.
- Product UI considerations are recorded separately; universal access, conversational use inside or adjacent to Revit, and reduced context switching remain open product goals rather than settled architecture.

## What does not exist yet

- no Revit add-in implementation;
- no MCP server implementation;
- no solution/project skeleton committed yet;
- no selected concrete compile-time Revit API package/provider;
- no detailed bridge handshake schema or protocol negotiation contract;
- no defined Revit execution queue behavior for capability requests;
- no explicit document identity/addressing model;
- no request scheduling/fairness policy for multiple clients beyond what is required for the first capability;
- no write-locking or transaction concurrency policy;
- no automated tests;
- no deployment or packaging model;
- no finalized user-facing Revit UI strategy.

## Current priorities

1. Define the minimum bridge handshake and protocol-version contract required by CAP-0001 and ADR-0003 instance validation.
2. Define the minimum Revit execution queue behavior required by CAP-0001.
3. Create a small Cursor implementation task for the initial solution/project skeleton and first vertical slice.
4. Let the implementation agent build the agreed slice and automated tests.
5. Review the implementation independently and validate it in Revit 2025, 2026, and 2027 as appropriate.

## Known constraints

- Revit API execution-context and transaction constraints must be respected.
- The solution should remain usable by multiple MCP-compatible clients and LLMs where technically possible.
- Existing Autodesk and third-party Revit MCP implementations should be benchmarked for useful patterns, limitations, interoperability opportunities, and product gaps; they do not determine whether RevitMCP should continue or whether an overlapping capability should exist.
- The architecture should preserve viable paths for both local agents and remote/cloud agents, subject to security, information-governance, and deployment-policy requirements.
- Local Revit IPC is intentionally separate from remote/cloud connectivity; future Azure or other hosted components must not require exposing the local Named Pipe directly.
- Revit process IDs are diagnostics, not the stable RevitMCP client-facing identity. ADR-0003 defines `instance_id` for process-lifetime identity.
- Instance discovery records are candidates only; a validated bridge handshake is required before an instance is considered ready.
- Revit instance identity is distinct from document identity.
- Revit-version-specific API differences should be confined to a compatibility boundary rather than scattered throughout capability or MCP-facing code.
- Cross-version compatibility requires all supported Revit add-in variants to compile in CI; local compilation against one Revit release is insufficient.
- Capability contracts are transport-neutral below the MCP adapter and must not depend on a specific LLM/client.
- `revit_get_context` is bounded by design: no model enumeration, selection enumeration, file paths, usernames, or cloud project identifiers in the base result.
- WebMCP must remain in scope as an emerging integration surface.
- The public repository must not contain confidential internal discussions, project information, credentials, or organization-specific sensitive details.
- Arbitrary AI-generated code execution inside Revit is not part of the normal production capability surface.
- The Revit UI strategy remains open. A future control/approval surface or richer conversational experience may be considered without changing the core capability and bridge architecture.

## Next decision

Define the minimum bridge handshake and protocol-version negotiation needed to validate ADR-0003 registrations and support the first CAP-0001 request without prematurely designing a larger RPC protocol.
