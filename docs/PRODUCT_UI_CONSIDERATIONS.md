# Product UI Considerations

## Status

The trusted approval presentation is implemented. It is a DockablePane titled "BIM-IT Revit Approval", hosted as WPF and a bundled local WebView2 at `https://revitmcp-approval.local/`. The pane GUID is unchanged. The native add-in remains the only approval authority. WebView2 is presentation and input.

BIM-IT Assistant v1 is a second DockablePane, also WPF and a bundled local WebView2, at `https://bimit-revit-assistant.local/`. It is a session-only conversational shell for three existing read workflows. Its AI Provider indicator says "Not configured". It is not a trusted approval surface, and it has no approval action. [ADR-0013](adr/ADR-0013-bim-it-assistant-local-architecture.md) separates the two panes.

[ADR-0009](adr/ADR-0009-agent-human-interaction-and-trusted-approval-boundary.md) is **Accepted**. It separates model-facing capabilities, human-facing MCP App interaction, and the trusted approval-provider boundary. An MCP App is not implemented. An MCP App may later present an approval screen, but that screen is not automatically a trusted approval provider. Persistent writes remain governed by ADR-0008.

[ADR-0010](adr/ADR-0010-revit-local-trusted-approval-authority.md) is **Accepted**. The approval presentation direction `DockablePane -> WPF -> bundled local WebView2` is what the Approval pane implements. Native add-in code remains the trusted approval authority.

The Assistant follows the system light/dark preference through CSS `prefers-color-scheme`. Binding the pane to the Revit UI theme is a follow-up, left undone so this slice does not take a multi-version theme API dependency.

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
