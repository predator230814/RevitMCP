# ADR-0004: Solution structure and multi-version Revit build strategy

- Status: Accepted
- Date: 2026-09-08

## Context

ADR-0001 established a standalone MCP server, an in-process Revit capability/execution add-in, and MCP-independent shared contracts. ADR-0002 established the local Named Pipe + JSON-RPC bridge. ADR-0003 established Revit instance registration and discovery.

RevitMCP now needs a concrete repository and build structure that supports Revit 2025, 2026, and 2027 from one maintainable C# codebase while keeping protocol, bridge, and Revit concerns independently testable.

Revit 2025 and 2026 were introduced on .NET 8, while Revit 2027 uses .NET 10. Autodesk is transitioning supported 2025/2026 releases toward .NET 10, but RevitMCP must remain usable across the supported installations rather than requiring every user to be on the newest point release.

The project should avoid both extremes:

- duplicating nearly identical Revit projects per year; and
- hiding build behavior behind a third-party MSBuild abstraction that becomes an architectural dependency.

## Decision drivers

- One maintainable codebase for Revit 2025-2027.
- Minimal duplication of project files and Revit capability code.
- Clear isolation of MCP, IPC, and Revit API dependencies.
- Ability to unit-test most code without launching Revit.
- Explicit, understandable build configuration.
- Ability to compile all supported Revit variants in CI.
- Ability to isolate version-specific Revit API differences.
- No committed or redistributed Autodesk Revit API binaries in the repository.
- Freedom to replace build-time Revit API package providers later.
- Avoid premature project-layer proliferation.

## Options considered

### Option A: Separate add-in projects per Revit version

For example:

```text
RevitMCP.Addin.2025
RevitMCP.Addin.2026
RevitMCP.Addin.2027
```

Advantages:

- straightforward version-specific references;
- easy to understand each build in isolation;
- fewer conditional MSBuild properties inside a project.

Disadvantages:

- duplicated project files and configuration;
- greater risk of version drift;
- encourages version-specific code to spread instead of being isolated;
- increases maintenance cost for every new supported Revit release.

### Option B: One multi-version add-in project

A single `RevitMCP.Addin.csproj` is compiled once per supported Revit version using an explicit `RevitVersion` MSBuild property. The property selects the appropriate target framework, Revit API reference version, compilation symbol, and output path.

Advantages:

- one source tree and one project definition;
- minimizes drift across Revit versions;
- makes cross-version CI straightforward;
- encourages version differences to be isolated;
- adding a new Revit release extends an existing build matrix instead of creating another copy of the add-in project.

Disadvantages:

- requires conditional MSBuild configuration;
- poorly managed conditions could make the project file hard to maintain;
- version-specific API differences still require an explicit compatibility strategy.

### Option C: Adopt a third-party Revit MSBuild SDK as the build foundation

A specialized SDK can automate Revit target frameworks, API references, manifests, and multi-version configurations.

Advantages:

- less custom MSBuild logic;
- strong developer ergonomics;
- can encode proven Revit build conventions.

Disadvantages:

- places an additional third-party abstraction in the critical build path;
- can hide mechanics that RevitMCP needs to control explicitly;
- creates another architectural dependency to maintain or replace over time.

Third-party Revit build tooling remains a useful benchmark and may be used selectively if later evidence justifies it, but it will not define the initial repository architecture.

## Decision

RevitMCP will use a **small SDK-style .NET solution with four initial source projects and a single multi-version Revit add-in project**.

## 1. Initial solution structure

The initial repository structure is conceptually:

```text
RevitMCP/
├── RevitMCP.sln
├── global.json
├── Directory.Build.props
├── Directory.Packages.props
│
├── src/
│   ├── RevitMCP.Contracts/
│   ├── RevitMCP.Bridge/
│   ├── RevitMCP.Server/
│   └── RevitMCP.Addin/
│
└── tests/
    ├── RevitMCP.Contracts.Tests/
    ├── RevitMCP.Bridge.Tests/
    └── RevitMCP.Server.Tests/
```

Additional projects must be introduced only when a concrete responsibility justifies them. The initial implementation will not pre-create generic `Core`, `Domain`, `Application`, `Infrastructure`, UI, Azure, WebMCP, or similar projects merely to satisfy a layered architecture template.

## 2. RevitMCP.Contracts

`RevitMCP.Contracts` defines transport-neutral data contracts shared across server and Revit-facing components, including concepts such as:

- Revit instance identity;
- registration and handshake records;
- operation classes;
- capability requests/results;
- explicit error contracts;
- bridge protocol metadata.

It must not reference:

- Autodesk Revit API assemblies;
- the MCP SDK;
- StreamJsonRpc;
- a specific LLM or AI client.

The initial target framework is `net8.0` unless implementation evidence requires a later change. Newer .NET projects may consume this shared library.

## 3. RevitMCP.Bridge

`RevitMCP.Bridge` implements the local bridge concerns established by ADR-0002 and ADR-0003, including:

- Named Pipe client/server abstractions;
- JSON-RPC integration;
- StreamJsonRpc implementation details behind RevitMCP-owned interfaces;
- local instance registration/discovery helpers;
- bridge connection lifecycle;
- protocol compatibility checks.

It depends on `RevitMCP.Contracts` but must not depend on:

- Autodesk Revit API assemblies;
- the MCP SDK.

Its core behaviors should be independently unit/integration testable outside Revit.

## 4. RevitMCP.Server

`RevitMCP.Server` owns MCP-facing concerns established by ADR-0001, including:

- official MCP C# SDK integration;
- initial `stdio` transport;
- MCP tool schemas and descriptions;
- MCP-side validation and response mapping;
- local Revit instance selection;
- bridge client orchestration.

The initial target framework is `net10.0`.

The server must not reference Autodesk Revit API assemblies.

## 5. RevitMCP.Addin

There will be one project:

```text
src/RevitMCP.Addin/RevitMCP.Addin.csproj
```

It is compiled separately for each supported Revit release with an explicit MSBuild property:

```text
RevitVersion=2025
RevitVersion=2026
RevitVersion=2027
```

The initial build matrix is:

| Revit | Target framework |
| --- | --- |
| 2025 | `net8.0-windows` |
| 2026 | `net8.0-windows` |
| 2027 | `net10.0-windows` |

This decision intentionally optimizes the Revit 2025 and 2026 builds for compatibility across installations that may be before or after Autodesk's .NET 10 point-release transition. A later ADR or maintenance decision may retarget a supported release if operational evidence makes that preferable.

The project does not require one universal DLL across all supported Revit years. Each build may produce a version-specific artifact from the same source project.

## 6. Explicit RevitVersion-driven build configuration

The `RevitVersion` property selects at least:

- the target framework;
- the compile-time Revit API reference version;
- a version compilation symbol such as `REVIT2025`, `REVIT2026`, or `REVIT2027`;
- version-specific output/artifact paths when needed.

Conditional MSBuild configuration should be centralized in the add-in project and/or repository-level build props rather than duplicated through unrelated project files.

The project must fail clearly when an unsupported or missing `RevitVersion` is supplied where an explicit version is required.

## 7. Version-specific Revit API differences

Version-specific source differences must be confined where practical to a dedicated compatibility area, conceptually:

```text
src/RevitMCP.Addin/
├── Capabilities/
├── Execution/
├── Registration/
├── Bridge/
└── Compatibility/
```

Conditional compilation such as:

```text
#if REVIT2025
#elif REVIT2026
#elif REVIT2027
#endif
```

should not be scattered throughout MCP-facing tool code or capability logic.

If future Revit API divergence becomes substantial, version-specific adapters or projects may be introduced through a later design decision rather than pre-created now.

## 8. Revit API references

Autodesk `RevitAPI.dll` and `RevitAPIUI.dll` binaries must not be committed to the repository as source dependencies.

Builds will use externally restored, version-pinned compile-time Revit API references appropriate to each supported Revit release. The specific package/provider is an implementation dependency, not an architectural commitment, and may be replaced without changing this ADR.

Revit API reference assemblies must not be copied into production add-in output in a way that redistributes Autodesk-owned runtime binaries unnecessarily.

## 9. .NET SDK baseline

The repository will use a `global.json` based on a .NET 10 SDK line for reproducible development and CI.

Using the .NET 10 SDK does not require every project to target .NET 10. The same repository SDK can build the `net8.0` / `net8.0-windows` projects required for shared contracts and Revit 2025/2026.

The exact SDK patch version and roll-forward policy are implementation details that may be maintained without a new ADR unless they materially change compatibility.

## 10. Centralized repository build conventions

Repository-wide compiler/build settings should be placed in `Directory.Build.props` where they genuinely apply across projects.

Package versions should be centrally managed through `Directory.Packages.props` when implementation begins, avoiding repeated package version declarations across projects.

Build configuration should remain explicit and understandable to a contributor without requiring knowledge of a proprietary or project-specific generator.

## 11. CI build matrix

CI must compile all supported add-in variants independently:

```text
RevitVersion=2025
RevitVersion=2026
RevitVersion=2027
```

A change is not considered cross-version build-compatible merely because the developer's locally installed Revit version compiles.

Non-Revit tests for Contracts, Bridge, and Server should run independently of Autodesk Revit being installed where technically possible.

Passing CI compilation does not replace real Revit integration validation for Revit API behavior.

## 12. Dependency direction

Initial dependency direction is conceptually:

```text
RevitMCP.Contracts
       ^
       |
RevitMCP.Bridge
       ^
       |
RevitMCP.Server

RevitMCP.Contracts
       ^
       |
RevitMCP.Bridge
       ^
       |
RevitMCP.Addin
       |
       v
 Autodesk Revit API
```

`RevitMCP.Server` and `RevitMCP.Addin` must not reference each other directly.

The shared bridge library must not become a location for Revit-specific domain logic or MCP-specific tool logic simply because both sides reference it.

## Consequences

### Positive

- One Revit add-in project serves the supported 2025-2027 matrix.
- Cross-version drift is reduced.
- MCP, bridge, contracts, and Revit dependencies remain explicit.
- Most protocol and discovery behavior can be tested without Revit.
- Future Revit releases extend an established build pattern.
- Revit-specific compatibility code has a defined containment boundary.
- Build-time Revit API providers remain replaceable.
- The solution remains small enough to understand before the first capability exists.

### Negative / costs

- The add-in `.csproj` requires carefully maintained conditional MSBuild logic.
- CI must build multiple Revit variants.
- Version-specific compatibility cannot be eliminated entirely.
- Revit integration tests still require actual Revit environments or dedicated test infrastructure.
- A third-party build SDK could provide conveniences that RevitMCP will initially implement more explicitly.

## Deferred decisions

This ADR intentionally does not decide:

- the exact compile-time Revit API NuGet/package provider;
- exact package versions;
- exact `.addin` manifest generation/deployment mechanism;
- installer/packaging strategy;
- code signing;
- exact CI provider/workflow syntax;
- first capability/tool contract;
- Revit document identity/addressing;
- user-facing Revit UI architecture;
- Azure/cloud gateway implementation;
- WebMCP or MCP Apps implementation.

## References

- ADR-0001: Out-of-process MCP server and Revit capability host
- ADR-0002: Named Pipes + JSON-RPC local bridge
- ADR-0003: Revit instance registration, discovery, and addressing
- Autodesk Revit API documentation
- Official MCP C# SDK
