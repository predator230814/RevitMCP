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
| Bounded generated C# read, distinct from arbitrary C# execution | [ADR-0028](adr/ADR-0028-bounded-generated-csharp-read-executor.md); [CAP-0020](capabilities/CAP-0020-bounded-generated-csharp-read.md); [ADR-0023](adr/ADR-0023-declarative-revit-read-query-architecture.md); [Microsoft Learn: AssemblyLoadContext](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.loader.assemblyloadcontext) | 2026-11-26 | CAP-0020 is live-complete under policy version 1. Policy version 2 is implemented, covered by automated tests, and live-validated by the 2026-11-26 Snowdon dogfood. That gate does not replace the 2026-11-25 policy version 1 records. Arbitrary C# execution is not authorized. | Evaluate `revit_run_csharp_query` when a required read-only long-tail fact is not expressible by existing primitives or composition and fits ADR-0028. The compiled registry is for an explicit ADR-0027 item 4 reason. The tool is default-off. It is not an OS sandbox. | None. This row records the accepted ADR-0028 exception. It does not authorize another executor, another Revit API identity, arbitrary C#, or an automatic query for every missing fact. |

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

### Bounded generated C# read, distinct from arbitrary C# execution (as of 2026-11-26)

Repository evidence, not a new external-source review. The 2026-11-25 live gates passed under policy version 1. Policy version 2 is the current deny list for new executions on the same `RevitAPI` 26.5.0.0 identity. It is implemented, covered by automated tests, and live-validated by the 2026-11-26 Snowdon dogfood. That gate does not replace the 2026-11-25 policy version 1 records. The 2026-11-11 wording that called all generated C# "research only", and that named the compiled read registry as the long-tail path, is superseded for current status by the accepted contracts below. This note does not change those contracts.

- **Bounded generated C# read.** [ADR-0028](adr/ADR-0028-bounded-generated-csharp-read-executor.md) authorizes one read surface, [CAP-0020](capabilities/CAP-0020-bounded-generated-csharp-read.md) `revit_run_csharp_query`. The pipeline is live-complete under policy version 1. The official MCP-client gate is **PASS** for that policy in [CAP_0020_CSHARP_QUERY_MCP_VALIDATION.md](CAP_0020_CSHARP_QUERY_MCP_VALIDATION.md). Production `tools/list` is 23. Bridge protocol is 26. The BIM-IT Assistant model tools do not expose this tool. Evaluate it when a required read-only long-tail fact is not expressible by existing primitives or by composing them, and the query is compatible with ADR-0028. Skip it when the fact is unnecessary or outside that policy.
- **Compiled reads stay for a stated reason.** [ADR-0024](adr/ADR-0024-registry-driven-compiled-revit-read-operations.md) still hosts `revit_lookup` and `revit_read`. `project.user_worksets` v1 remains supported. A new compiled operation requires an explicit [ADR-0027](adr/ADR-0027-layered-revit-capability-admission.md) item 4 reason. The registry is not the default path for the next inventory.
- **Arbitrary C# execution remains prohibited.** ADR-0023 §1 still applies outside CAP-0020. Python, Dynamo, macros, expressions, arbitrary assemblies, caller-supplied IL, full C# projects, and generated writes are not authorized. [ADR-0025](adr/ADR-0025-bounded-revit-api-property-access.md) generated C# remains Research Only as a substitute for ordinary Element property reads.
- **Policy version 2 is the current deny list for one loaded identity.** The reviewed assembly remains `RevitAPI` 26.5.0.0. Policy version 1 is the inventory that passed the 2026-11-25 Revit gates. Version 2 keeps that inventory and adds declaring-type method denials plus Revit property writes. Version 2 is implemented, covered by automated tests, and live-validated by the 2026-11-26 Snowdon dogfood. That gate does not replace the 2026-11-25 policy version 1 records. The version participates in the cache key, so the same source and the same API identity hash differently under version 2. Any other assembly name or version returns `CSHARP_QUERY_API_UNSUPPORTED` and does not compile. Revit 2025, Revit 2027, and any other build need their own reviewed manifest. Policy version 2 is not an OS sandbox.
- **Default-off local opt-in.** The Addin reads `%LOCALAPPDATA%\RevitMCP\capabilities.json` on every call. A missing or unreadable file stays disabled and returns `CSHARP_QUERY_DISABLED` before the compiler starts. The product does not create or enable that file. Enabling the query grants no write authority. There is no per-call approval.
- **Residual risks from ADR-0028.** The collectible `AssemblyLoadContext` is not an operating-system sandbox, and Code Access Security is not a sandbox. No open transaction stops ordinary transactional edits. Policy version 2 is the current deny list for `RevitAPI` 26.5.0.0 only. It does not name every externally effectful member. A blocking Revit API call is not interrupted. The executor does not guarantee protection against every CPU, memory, or blocking-call denial of service.

## Watch update policy

Tech Watch entries are evidence, not decisions.

- **Observation only:** verified external fact with no architecture or contract change. It may be committed directly to this file when the change is isolated and safe.
- **Review candidate:** may affect RevitMCP. Evidence may be recorded here with no action authorized, then Dave + Tech Lead review the implication.
- **Architecture/action proposal:** do not modify accepted ADRs, capability contracts, security boundaries, dependencies, source code, tests, or Skills automatically. Escalate for review first.

A failed direct update because of permissions, branch protection, or concurrent changes must not automatically create a pull request. Report the proposed change instead.
