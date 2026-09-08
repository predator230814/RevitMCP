# Product UI Considerations

## Status

Open product question. No UI architecture has been selected.

## Why this matters

RevitMCP should not assume that every user has access to a dedicated MCP-capable desktop client such as ChatGPT Desktop, Claude Desktop, Cursor, or similar developer-oriented tooling.

A future user-facing experience inside Revit, or tightly adjacent to Revit, may therefore be valuable for three product reasons:

1. **Universal access** — provide a practical path for users who do not have a specialized MCP desktop client.
2. **Conversational experience** — allow users to interact naturally with an AI assistant while still using the same RevitMCP capability layer.
3. **Reduced context switching** — avoid forcing users to converse on one screen or application and then move attention back to Revit to inspect the result.

## Architectural constraint

Any future Revit-hosted or companion conversational UI should be an adapter/client of the RevitMCP capability layer, not a replacement for it.

It must not make the core depend on:

- a specific LLM provider;
- a specific cloud platform;
- a specific enterprise agent;
- a specific chat UI framework.

Existing and future external clients such as enterprise agents, ChatGPT, Claude, Cursor, MCP Apps, WebMCP, or other MCP-compatible clients should remain able to use the same capabilities independently.

## Open questions

- Should the primary Revit experience be a full conversational chat, a control/approval panel, or both?
- Should the Revit UI connect directly to an enterprise/cloud agent, to a local model/client, or through a generic provider abstraction?
- What authentication and enterprise-governance model should apply?
- How should conversational history relate to the active Revit document and selection?
- Which operations require in-Revit confirmation or preview?
- How should this coexist with Tetra Agent or other enterprise agents if available?

These questions are intentionally deferred until the core bridge, instance discovery, and initial capability model are established.
