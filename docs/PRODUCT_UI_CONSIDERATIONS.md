# Product UI Considerations

## Status

The trusted approval authority is the native add-in. The primary presentation is a trusted approval card inside the BIM-IT Assistant DockablePane. The card is driven by the Addin-owned `ApprovalReviewRenderModel`. Approve records that exact preview. It does not apply the change. The standalone DockablePane titled "BIM-IT Revit Approval", at `https://revitmcp-approval.local/`, remains a transitional fallback when the Assistant surface cannot be shown. Both surfaces use the same in-process provider session. WebView2 is presentation and input.

BIM-IT Assistant is a DockablePane, WPF and a bundled local WebView2, at `https://bimit-revit-assistant.local/`. The header shows the BIM IT mark and the connected AI provider. GitHub Copilot is the only provider that can be selected today. Connection detail stays in Diagnostics. While a chat turn is running, the pane shows a compact activity card for real Assistant work and removes that card when the turn ends. A trusted approval card can appear on that same pane without a chat message and without the AI provider. Placement actions render only while the Assistant store holds a current proposal, in state `ready` or `needs_calibration`. Pattern actions render beside their own review state. [ADR-0013](adr/ADR-0013-bim-it-assistant-local-architecture.md) separates the Assistant process from the Revit capability core. The inline approval card does not give that process approval authority.

[ADR-0009](adr/ADR-0009-agent-human-interaction-and-trusted-approval-boundary.md) is **Accepted**. It separates model-facing capabilities, human-facing MCP App interaction, and the trusted approval-provider boundary. An MCP App is not implemented. An MCP App may later present an approval screen, but that screen is not automatically a trusted approval provider. Persistent writes remain governed by ADR-0008.

[ADR-0010](adr/ADR-0010-revit-local-trusted-approval-authority.md) is **Accepted**. Native add-in code remains the trusted approval authority. The primary presentation is the inline Assistant card. The standalone Approval pane is transitional fallback.

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
