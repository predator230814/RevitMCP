# RevitMCP Current State

_Last updated: 2026-09-09_

## Phase

Implementation started / local discovery and handshake infrastructure.

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
- The ADR-0004 solution/project skeleton is committed: `RevitMCP.sln`, `global.json`, centralized build/package props, `src/RevitMCP.Contracts`, `src/RevitMCP.Bridge`, `src/RevitMCP.Server`, one multi-version `src/RevitMCP.Addin` project, and corresponding non-Revit test projects.
- Revit 2025, 2026, and 2027 are built from the same add-in project using an explicit `RevitVersion` build property; the initial target matrix is `net8.0-windows` for Revit 2025/2026 and `net10.0-windows` for Revit 2027.
- GitHub Actions compiles the non-Revit projects and the 2025/2026/2027 add-in matrix.
- `RevitMCP.Contracts` defines transport-neutral registration, handshake, discovery-state, and bridge error contracts.
- `RevitMCP.Bridge` implements user-local atomic instance registration, current-user Named Pipe hosting, `bridge.handshake` over StreamJsonRpc, and discovery that classifies candidates as `Ready`, `Unavailable`, `Incompatible`, or `Stale`.
- StreamJsonRpc `2.25.29` is used only inside `RevitMCP.Bridge`, behind RevitMCP-owned abstractions.
- Revit API references will be externally restored, version-pinned compile-time dependencies; Autodesk Revit API binaries will not be committed to the repository.
- Capability specifications are recorded under `docs/capabilities/` before implementation.
- CAP-0001 accepts `revit_get_context` as the first end-to-end read-only Revit capability. It returns bounded instance, active-document, active-view, and selection-count context without exposing paths or enumerating selection contents.
- Bridge specifications are recorded under `docs/bridge/` when accepted ADRs require concrete versioned technical contracts.
- BRIDGE-0001 defines the stable `bridge.handshake` bootstrap contract, identity validation, and integer bridge-protocol version negotiation. The handshake uses cached add-in/process metadata and must not invoke `ExternalEvent` or inspect the Revit model.
- Execution specifications are recorded under `docs/execution/`.
- EXEC-0001 defines one serialized FIFO Revit execution dispatcher per Revit process, backed by one long-lived `ExternalEvent`, asynchronous completion, queued cancellation, non-destructive timeout semantics, failure isolation, and explicit transaction ownership outside the dispatcher.
- The initial MCP transport is `stdio`; Streamable HTTP, MCP Apps, WebMCP, Azure/cloud gateways, and other remote deployment paths remain extensions rather than core dependencies.
- Product UI considerations are recorded separately; universal access, conversational use inside or adjacent to Revit, and reduced context switching remain open product goals rather than settled architecture.

## What does not exist yet

- no Revit `IExternalApplication` or Autodesk Revit API integration;
- no EXEC-0001 execution dispatcher or `ExternalEvent` integration;
- no CAP-0001 `revit_get_context` implementation;
- no MCP server runtime, MCP tools, or `revit_list_instances` MCP exposure;
- no selected concrete compile-time Revit API package/provider;
- no explicit document identity/addressing model;
- no request scheduling/fairness policy for multiple clients beyond FIFO serialization required by EXEC-0001;
- no write-locking or transaction concurrency policy;
- no deployment or packaging model;
- no finalized user-facing Revit UI strategy.

## Current priorities

1. Implement EXEC-0001 execution dispatch on top of the discovery/handshake infrastructure.
2. Keep CAP-0001 `revit_get_context` as the following task. Do not expand into additional capabilities, writes, Azure/cloud, WebMCP, or UI work.
3. Review the implementation independently for contract compliance, architecture boundaries, cross-version build behavior, and failure handling.
4. Validate the implemented slice in real Revit, including supported-version coverage appropriate to the change.
5. Update project state and specifications from observed implementation/validation results before expanding the capability surface.

## Known constraints

- Revit API execution-context and transaction constraints must be respected.
- The solution should remain usable by multiple MCP-compatible clients and LLMs where technically possible.
- Existing Autodesk and third-party Revit MCP implementations should be benchmarked for useful patterns, limitations, interoperability opportunities, and product gaps; they do not determine whether RevitMCP should continue or whether an overlapping capability should exist.
- The architecture should preserve viable paths for both local agents and remote/cloud agents, subject to security, information-governance, and deployment-policy requirements.
- Local Revit IPC is intentionally separate from remote/cloud connectivity; future Azure or other hosted components must not require exposing the local Named Pipe directly.
- Revit process IDs are diagnostics, not the stable RevitMCP client-facing identity. ADR-0003 defines `instance_id` for process-lifetime identity.
- Instance discovery records are candidates only; a validated BRIDGE-0001 handshake is required before an instance is considered ready.
- The handshake is a stable bootstrap contract and must not depend on negotiated capability traffic in order to negotiate a compatible bridge protocol version.
- The handshake must not invoke the Revit execution queue or `ExternalEvent`; it is answered from cached add-in/process metadata.
- Capability requests requiring Revit API access must be dispatched through EXEC-0001 rather than executed on bridge/background threads.
- Revit API work is serialized FIFO in the initial dispatcher. Multiple bridge connections do not imply concurrent Revit API execution.
- Cancellation/timeout before execution prevents queued work from starting; a caller timeout after Revit execution begins must not forcibly abort the Revit thread or an active Revit API operation.
- The execution dispatcher must not automatically create Revit transactions. Transaction policy belongs to capability/application logic and future accepted write specifications.
- Revit instance identity is distinct from document identity.
- Revit-version-specific API differences should be confined to a compatibility boundary rather than scattered throughout capability or MCP-facing code.
- Cross-version compatibility requires all supported Revit add-in variants to compile in CI; local compilation against one Revit release is insufficient.
- Capability contracts are transport-neutral below the MCP adapter and must not depend on a specific LLM/client.
- `revit_get_context` is bounded by design: no model enumeration, selection enumeration, file paths, usernames, or cloud project identifiers in the base result.
- WebMCP must remain in scope as an emerging integration surface.
- The public repository must not contain confidential internal discussions, project information, credentials, or organization-specific sensitive details.
- Arbitrary AI-generated code execution inside Revit is not part of the normal production capability surface.
- The Revit UI strategy remains open. A future control/approval surface or richer conversational experience may be considered without changing the core capability and bridge architecture.

## Next task

Implement EXEC-0001 execution dispatch, with focused automated tests where practical. Keep CAP-0001 `revit_get_context` as the subsequent task. Do not expand into additional Revit capabilities, writes, Azure/cloud, WebMCP, or UI work.
