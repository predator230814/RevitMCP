# Server specifications

This directory contains accepted technical contracts for the standalone `RevitMCP.Server` process.

Accepted specifications:

- [SERVER-0001](SERVER-0001-stdio-get-context-tool.md) — initial `stdio` MCP server, deterministic local Revit instance routing, and MCP exposure of CAP-0001 `revit_get_context`.
- [SERVER-0002](SERVER-0002-stdio-query-elements-tool.md) — adds CAP-0002 `revit_query_elements`, bridge protocol-v3 capability gating, document-context guarding, and preservation of CAP-0001 on explicitly compatible v2/v3 bridges.
- [SERVER-0003](SERVER-0003-stdio-get-elements-tool.md) — adds CAP-0003 `revit_get_elements`, bounded projection/partial-result semantics, bridge protocol-v4 capability gating, and preservation of CAP-0001/CAP-0002 on explicitly compatible v4 bridges.

Server specifications refine accepted ADRs and capability contracts. They must not introduce Autodesk Revit API dependencies into the Server or silently change lower-layer Bridge/Addin contracts.
