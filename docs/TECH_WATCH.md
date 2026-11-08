# Technology watch

Living record of external technology to recheck before architecture-sensitive decisions. It is not an ADR. A row here authorizes nothing unless its last column says so.

| Topic | Authoritative source | Last checked | Maturity | Relevance to BIM-IT | Decision / action authorized |
|---|---|---|---|---|---|
| MCP Skills / SEP-2640 | [ext-skills repository](https://github.com/modelcontextprotocol/ext-skills); [SEP-2640](https://modelcontextprotocol.io/seps/2640-skills-extension); [C# SDK package source](https://github.com/modelcontextprotocol/csharp-sdk/tree/main/src/ModelContextProtocol.Extensions.Skills) | 2026-11-07 | SEP Final (merged 2026-09-13). Extension Beta. C# `ModelContextProtocol.Extensions.Skills` is `[Experimental]`. | Could publish the BIM-IT Skill registry to any MCP client. See the dated note in [ADR-0022](adr/ADR-0022-agent-skills-and-knowledge-architecture.md). | None. No adoption decision. The package is not in the solution. |
| pyRevit Agent Runtime | [pyRevit docs: Agent runtime](https://docs.pyrevitlabs.io/agent-runtime/) | 2026-11-07 | Experimental, off by default. | Reference for progressive disclosure, API lookup, compact results with run records, and token economy. | None. BIM-IT does not adopt its Python execution model. |
| Autodesk Revit MCP | [Revit Public MCP Server](https://help.autodesk.com/cloudhelp/ENU/ADSKMCP-RevitMcp/files/ADSKMCP_RevitMcp_revit_mcp_server_html.html) | 2026-11-07 | Technical Preview, Revit 2027.2 only. | Reference and evidence for the ecosystem (ADR-0007). Not an architecture mandate. | None. |
| WebMCP / MCP Apps / other agent standards | To be identified when next checked | Not rechecked | Unknown here | Keep in scope; do not couple the core. | None. |
| Flexible C# Revit API execution | [ADR-0023](adr/ADR-0023-declarative-revit-read-query-architecture.md); [Microsoft Learn: AssemblyLoadContext](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.loader.assemblyloadcontext) | 2026-11-07 | Research only | Long-tail facts (for example workset) that the declarative kernel does not expose. | None. No implementation and no ADR change. |

## Notes

### MCP Skills / SEP-2640 (as of 2026-11-07)

- SEP-2640 is Final. The extension is Beta.
- The official C# SDK has an experimental `ModelContextProtocol.Extensions.Skills` (NuGet 2.4.0 on 2026-11-07).
- No BIM-IT adoption decision yet. Evaluate separately for interoperability, security, migration cost, and client support.

### pyRevit Agent Runtime (as of 2026-11-07)

- Experimental. It intentionally uses a small MCP surface plus Skills and API lookup and generated Python, rather than hundreds of per-API tools.
- `run_query` rolls model changes back but is not an OS sandbox: the script has full machine access.
- Useful reference for progressive disclosure, API lookup, result and run-record design, and token economy.
- BIM-IT does not adopt its Python execution model.

### Autodesk Revit MCP (as of 2026-11-07)

Revit 2027.2 Technical Preview, with three server options: Read (does not modify the model), Write (Read plus controlled-change tools), and Experimental (Read and Write plus experimental tools). Tools may change before General Availability. Treat as reference and evidence, not as an architecture mandate.

### Flexible C# Revit API execution (research only)

Known constraints:

- ADR-0023 currently prohibits model-supplied C#, Python, or API source in the normal production capability layer.
- Modern .NET `AssemblyLoadContext` provides no security features and is not a security boundary (Microsoft Learn).
- Code Access Security is not supported or honored in modern .NET, so it is not a viable sandbox.
- Any future generated-C# execution design needs a real security and reliability boundary, or an explicitly different trust and product surface.
- No implementation or ADR change is authorized yet.

Workset is the first deliberate research example. See [ELECTRICAL_STANDARDS_SKILL_V0_1.md](ELECTRICAL_STANDARDS_SKILL_V0_1.md).
