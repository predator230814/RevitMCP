# Server specifications

This directory contains accepted technical contracts for the standalone `RevitMCP.Server` process.

Accepted specifications:

- [SERVER-0001](SERVER-0001-stdio-get-context-tool.md) — initial `stdio` MCP server, deterministic local Revit instance routing, and MCP exposure of CAP-0001 `revit_get_context`.

Server specifications refine accepted ADRs and capability contracts. They must not introduce Autodesk Revit API dependencies into the Server or silently change lower-layer Bridge/Addin contracts.
