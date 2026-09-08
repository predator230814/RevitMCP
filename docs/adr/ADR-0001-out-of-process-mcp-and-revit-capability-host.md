# ADR-0001: Out-of-process MCP server and Revit capability host

- Status: Accepted
- Date: 2026-09-08

## Context

RevitMCP must expose useful Autodesk Revit capabilities to AI agents while remaining vendor-neutral, maintainable, and adaptable to a rapidly evolving MCP ecosystem.

The project targets Revit 2025 through 2027 and should support multiple MCP-compatible clients and LLMs whenever technically possible. It must also preserve future paths for local desktop agents, remote/cloud agents, MCP Apps, WebMCP, and other standards without coupling Revit capability logic to any one client, transport, or deployment model.

Existing Autodesk and third-party Revit MCP implementations provide useful reference patterns, but they are benchmarks rather than architectural authorities for RevitMCP.

A key Revit constraint is that Revit API operations must execute inside a valid Revit API execution context and respect transaction and threading requirements.

## Decision drivers

- Vendor neutrality across LLMs and MCP clients.
- Support for Revit 2025, 2026, and 2027 from one maintainable codebase.
- Ability to evolve MCP protocol and transport concerns independently from Revit integration.
- Explicit control over Revit execution context and transactions.
- Support for safe read operations and later controlled write/destructive operations.
- Ability to support both local and future remote/cloud deployment models.
- Compatibility with the official MCP C# SDK and current MCP specification.
- Ability to add MCP Apps, WebMCP, or other interfaces without rewriting the Revit capability layer.
- Testability and clear separation of responsibilities.

## Options considered

### Option A: Run the complete MCP server inside the Revit process

The Revit add-in would directly host the MCP protocol, transports, tool definitions, and Revit API execution.

Advantages:

- fewer processes;
- potentially simpler first prototype;
- no explicit local IPC boundary.

Disadvantages:

- couples MCP lifecycle and dependencies to Revit;
- increases risk of dependency/version conflicts inside the Revit process;
- makes transport evolution and remote deployment harder;
- mixes protocol concerns with Revit execution concerns;
- increases the blast radius of MCP/server faults inside Revit.

### Option B: Separate MCP server and Revit execution add-in

A standalone MCP server handles protocol, transport, schemas, validation, and client-facing concerns. A Revit add-in acts as a capability/execution host and performs Revit API operations in the valid Revit context. The two communicate through a local bridge whose exact IPC mechanism is decided separately.

Advantages:

- clean separation between MCP and Revit lifecycles;
- easier testing and independent evolution;
- lower dependency pressure inside Revit;
- supports multiple transports and future remote gateways;
- allows the same Revit capability contracts to serve MCP, WebMCP, or other adapters;
- makes Revit execution-context and transaction boundaries explicit.

Disadvantages:

- requires an IPC boundary and lifecycle coordination;
- requires connection/error handling between processes;
- slightly more deployment complexity than an in-process prototype.

### Option C: Use an existing Revit MCP implementation as the core

Build RevitMCP primarily as an extension of Autodesk, Nonica, pyRevit, or another existing implementation.

Advantages:

- faster access to existing capabilities;
- reuse of proven implementation patterns.

Disadvantages:

- inherits another project's architecture and release constraints;
- can constrain Revit-version support, deployment, write-operation design, security, or agent usability;
- weakens RevitMCP's ability to evolve independently.

Existing implementations remain valuable benchmarks and may be interoperated with or reused selectively where appropriate, but they do not define the core architecture.

## Decision

RevitMCP will use an **out-of-process architecture**.

### 1. Standalone MCP server

`RevitMCP.Server` will run outside the Revit process and will own:

- MCP protocol behavior;
- MCP transports;
- client-facing tool schemas;
- input validation and orchestration;
- protocol-level errors and diagnostics;
- transport-specific authentication/authorization when applicable.

C#/.NET is the primary implementation stack. The server will use the official MCP C# SDK unless a later ADR supersedes that choice.

The initial local MCP transport will be `stdio`. Streamable HTTP and remote/cloud hosting remain supported architectural paths but are not required for the first implementation.

### 2. Revit capability/execution host

`RevitMCP.Addin` will run inside Revit and will own:

- Revit instance registration and lifecycle integration;
- dispatch into a valid Revit API execution context;
- Revit transactions and transaction groups;
- Revit-specific services and adapters;
- execution of read, write, and destructive capability operations.

The add-in is not the AI client and is not required to host the MCP protocol.

### 3. MCP-independent contracts

A shared contracts layer, conceptually `RevitMCP.Contracts`, will define requests, responses, identifiers, operation classes, and explicit error models used between the MCP-facing layer and the Revit capability layer.

These contracts must not depend on a specific LLM vendor or AI client. Where practical, they should also avoid dependence on MCP-specific types so that other adapters can reuse them.

### 4. Revit version strategy

RevitMCP will target **Revit 2025, 2026, and 2027** from one shared codebase.

Version-specific builds and adapters are acceptable where Revit API or runtime differences require them. The project does not require one universal Revit add-in binary across all three versions.

Target-framework selection for each Revit release may evolve with Autodesk's .NET runtime updates without changing this architectural decision.

### 5. Operation safety classes

Capability contracts will distinguish at least:

- `Read`;
- `Write`;
- `Destructive`.

Write and destructive behavior will receive additional authorization, confirmation, transaction, and audit design in later ADRs.

### 6. Extensibility surfaces

MCP Apps, WebMCP, Streamable HTTP, cloud gateways, and client-specific integrations are optional adapters/extensions around the capability layer. They must not become mandatory dependencies of the Revit core.

### 7. Arbitrary code execution

Executing arbitrary AI-generated C# or scripts inside Revit will **not** be a normal RevitMCP capability.

A future explicitly unsafe developer-only mode could be considered separately, but it must not be part of the standard production capability surface by default.

### 8. Local bridge remains open

This ADR intentionally does **not** choose the IPC mechanism between `RevitMCP.Server` and `RevitMCP.Addin`.

Named pipes, localhost HTTP, and other suitable mechanisms will be evaluated separately based on security, discoverability, multi-instance Revit support, performance, debugging, packaging, and remote-gateway compatibility.

## Conceptual topology

```text
MCP Client / Agent
        |
        | MCP (stdio first; other transports later)
        v
+--------------------------+
| RevitMCP.Server          |
| - MCP adapters           |
| - schemas / validation   |
| - orchestration          |
+-------------+------------+
              |
              | RevitMCP.Contracts over local bridge
              | IPC mechanism: separate decision
              v
+--------------------------+
| RevitMCP.Addin           |
| - instance registration  |
| - execution dispatcher   |
| - transaction control    |
| - Revit capabilities     |
+-------------+------------+
              |
              v
         Autodesk Revit API
```

Future adapters such as WebMCP, MCP Apps, or remote gateways should reuse the same capability contracts rather than bypassing them.

## Consequences

### Positive

- MCP and Revit can evolve independently.
- Revit API dependencies remain isolated from the MCP host.
- Multiple clients and LLM providers can use the same capability layer.
- Revit 2025-2027 can share most implementation while retaining version-specific compatibility where necessary.
- Future WebMCP, MCP Apps, and remote/cloud scenarios remain possible.
- Revit write safety can be designed explicitly rather than emerging accidentally from tool implementations.
- The architecture can selectively adopt useful patterns from Autodesk, Nonica, pyRevit, and other projects without inheriting their full architecture.

### Negative / costs

- A robust local IPC protocol is required.
- Revit instance discovery, connection lifecycle, timeouts, and error recovery must be designed.
- Packaging will include at least a Revit add-in and an external server component.
- Integration testing must cover both the external process and in-Revit execution.

## Deferred decisions

Separate ADRs or design decisions are required for:

- local IPC mechanism;
- Revit instance discovery and addressing;
- exact project/solution structure and multi-version build strategy;
- remote Streamable HTTP/gateway architecture;
- authentication and authorization;
- write/destructive confirmation and audit model;
- MCP Apps integration;
- WebMCP integration;
- packaging and deployment.

## References

- Model Context Protocol specification: https://modelcontextprotocol.io/
- Official MCP C# SDK: https://github.com/modelcontextprotocol/csharp-sdk
- Autodesk Revit API documentation: https://help.autodesk.com/view/RVT/2027/ENU/
- pyRevit documentation: https://docs.pyrevitlabs.io/

