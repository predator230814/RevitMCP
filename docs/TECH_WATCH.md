# Technology watch

Living record of external technology to recheck before architecture-sensitive decisions. It is not an ADR. A row here authorizes nothing unless its last column says so.

| Topic | Authoritative source | Last checked | Maturity | Relevance to BIM-IT | Decision / action authorized |
|---|---|---|---|---|---|
| MCP Core | [modelcontextprotocol repository](https://github.com/modelcontextprotocol/modelcontextprotocol) | 2026-11-11 | Published revision `2026-07-28`. | Baseline protocol and schema evolution. | None. Track compatibility; no architecture change. |
| MCP Apps / WebMCP | [MCP Apps repository](https://github.com/modelcontextprotocol/ext-apps); [SDK v2.1.0 release](https://github.com/modelcontextprotocol/ext-apps/releases/tag/v2.1.0) | 2026-11-11 | MCP Apps specification `2026-01-26` is Stable. WebMCP interoperability landed in SDK v2.1.0 and is recent. | Strong candidate for future BIM-IT web/APS interfaces that expose agent-callable UI capabilities without coupling the Revit execution kernel to a browser host. | None. Observation only; no ADR change and no dependency adoption. |
| MCP Skills / SEP-2640 | [ext-skills repository](https://github.com/modelcontextprotocol/ext-skills); [SEP-2640](https://modelcontextprotocol.io/seps/2640-skills-extension); [C# SDK package source](https://github.com/modelcontextprotocol/csharp-sdk/tree/main/src/ModelContextProtocol.Extensions.Skills) | 2026-11-11 | SEP Final. Extension Beta. C# `ModelContextProtocol.Extensions.Skills` remains experimental (`MCPEXP005`) in SDK 2.4.0. | Could later provide a standards-based adapter over the BIM-IT Skill registry. | None. No adoption decision. The package is not in the solution. |
| MCP Tasks | [ext-tasks repository](https://github.com/modelcontextprotocol/ext-tasks) | 2026-11-11 | Extension schema `2026-07-28` is Stable. | Potential future transport/orchestration for long-running work and human-in-the-loop waits. It must not become the trusted approval authority. | None. Review later with the preview/review/approval lifecycle. |
| pyRevit Agent Runtime | [pyRevit docs: Agent runtime](https://docs.pyrevitlabs.io/agent-runtime/); [pyRevit repository](https://github.com/pyrevitlabs/pyRevit) | 2026-11-11 | Experimental, active development. `develop` observed at `8d6609102e2e1d31ca677220c1cf9137baf3aa0b`. | Reference for progressive disclosure, API lookup, operation registries, compact results, run records, and token economy. | None. BIM-IT does not adopt its arbitrary Python execution model. |
| Autodesk Revit MCP | [Autodesk MCP Help](https://help.autodesk.com/view/ADSKMCP/ENU/) | 2026-11-11 | Local Tech Preview / Public Beta. Current Autodesk setup documentation requires Revit 2027.2. | Competitive reference and evidence for ecosystem direction. Not an architecture mandate. | None. |
| Generated/arbitrary C# Revit API execution | [ADR-0023](adr/ADR-0023-declarative-revit-read-query-architecture.md); [ADR-0024](adr/ADR-0024-registry-driven-compiled-revit-read-operations.md); [Microsoft Learn: AssemblyLoadContext](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.loader.assemblyloadcontext) | 2026-11-11 | Research only. | Long-tail factual reads are now addressed by the compiled read-operation registry. Model-supplied/generated code remains a separate trust and sandboxing problem. | None. No implementation and no ADR change. |

## Notes

### MCP Core (as of 2026-11-11)

- The official repository publishes the current protocol schema under `2026-07-28`.
- No material change was found that requires a RevitMCP architecture migration.

### MCP Apps / WebMCP (as of 2026-11-11)

- MCP Apps SDK v2.1.0 was published 2026-11-10.
- The SDK now bridges tools in both directions between `App.registerTool()` and page tools exposed through `document.modelContext` when WebMCP is available.
- WebMCP tool-list changes are relayed through the MCP Apps tool-change notification path.
- The bridge is enabled by default in the SDK and can be disabled per app.
- This is relevant to future BIM-IT web dashboards, APS-facing applications, and browser-hosted workflows.
- It does not replace the Revit Bridge, the compiled read-operation registry, or the trusted local approval boundary.
- Treat the new WebMCP bridge as an ecosystem observation, not a dependency or architecture decision.

### MCP Skills / SEP-2640 (as of 2026-11-11)

- SEP-2640 is Final, while the Skills extension specification remains Beta.
- The official C# SDK 2.4.0 contains `ModelContextProtocol.Extensions.Skills`, but the public APIs remain experimental under `MCPEXP005`.
- No BIM-IT adoption decision yet. The accepted provider-neutral Skill registry remains the project architecture.
- A future standards adapter can be evaluated separately for interoperability, migration cost, client support, and security.

### MCP Tasks (as of 2026-11-11)

- The official Tasks extension publishes a Stable `2026-07-28` schema.
- Its use cases include long computations, disconnected-client continuation, and approval workflows.
- For BIM-IT, a future Tasks integration could improve MCP-side orchestration of waits or long-running workflows.
- Tasks must not replace the trusted local human approval authority defined by the BIM-IT write architecture.

### pyRevit Agent Runtime (as of 2026-11-11)

- Experimental and actively changing.
- It intentionally uses a small MCP surface plus Skills/API lookup and generated Python rather than hundreds of per-API tools.
- Recent changes continue to harden agent-runtime behavior such as timeout handling.
- Useful reference for progressive disclosure, API lookup, registry design, result/run-record design, and token economy.
- BIM-IT does not adopt its Python execution model because script rollback is not an OS sandbox and non-model side effects remain possible.

### Autodesk Revit MCP (as of 2026-11-11)

- Autodesk documents the Revit Public MCP Server as a local Tech Preview / Public Beta.
- Current setup documentation requires Revit 2027.2 and documents Claude Desktop and Cursor as supported local MCP clients.
- Treat Autodesk's implementation as a reference and source of product ideas, not as an architecture mandate.
- Do not justify a BIM-IT primitive solely by whether Autodesk currently exposes the same fact.

### Generated/arbitrary C# Revit API execution (research only)

Known constraints:

- ADR-0023 prohibits model-supplied C#, Python, macros, arbitrary expressions, or Revit API source in the normal production capability layer.
- ADR-0024 now provides the preferred path for long-tail factual reads: reviewed, compiled C# operations in a closed registry exposed through `revit_lookup` and `revit_read`.
- Modern .NET `AssemblyLoadContext` is not a security boundary.
- Code Access Security is not supported or honored in modern .NET, so it is not a viable sandbox.
- Any future generated-C# execution design would need a genuinely different security boundary and explicit product/architecture review.

## Watch update policy

Tech Watch entries are evidence, not decisions.

- **Observation only:** verified external fact with no architecture or contract change. It may be committed directly to this file when the change is isolated and safe.
- **Review candidate:** may affect RevitMCP. Evidence may be recorded here with no action authorized, then Dave + Tech Lead review the implication.
- **Architecture/action proposal:** do not modify accepted ADRs, capability contracts, security boundaries, dependencies, source code, tests, or Skills automatically. Escalate for review first.

A failed direct update because of permissions, branch protection, or concurrent changes must not automatically create a pull request. Report the proposed change instead.
