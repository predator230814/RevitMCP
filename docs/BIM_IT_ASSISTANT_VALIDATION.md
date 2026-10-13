# BIM-IT Assistant live validation

Primary target: Revit 2026.5 build 26.5.0.55.

Build `BimIt.Revit.Assistant` and `BimIt.Revit.Server` in Release before this check. Both processes target `net10.0` and need the .NET 10 runtime, including when the pane is hosted by Revit 2025 or 2026. Load the 2026 add-in from its repository output. The pane finds those executables by walking up from the add-in directory to the repository build output.

This checklist is for a person at a running Revit session. Automated tests do not open Revit. No model mutation is expected from the three v1 actions.

Expected contract behavior, before the click-through:

- Analyze Warnings on a model with an active document reports `document_id_unavailable`. `revit_get_context` does not return `document_id`, and `revit_get_warnings` requires one. The pane must not invent an id.
- Active View Elements sends `filters: {}`. The accepted `revit_query_elements` contract requires at least one filter, so the pane shows `INVALID_REQUEST` and explains that limit.
- AI Provider stays "Not configured". Natural language is kept in the session timeline and answered with that fact.

## Checklist

1. Start Revit normally.
2. Confirm the BIM-IT Assistant ribbon button exists.
3. Confirm no Assistant or MCP Server process started merely because Revit started.
4. Click BIM-IT Assistant.
5. Confirm the pane opens and the WebView renders.
6. Confirm status moves truthfully through Revit/Bridge, then Assistant, then MCP. AI Provider stays "Not configured".
7. Open diagnostics and confirm `tools/list` reports the current 10-tool MCP surface.
8. Run Model Context. Confirm the result matches the open session and document.
9. Run Analyze Warnings on a known model. Confirm the `document_id_unavailable` result above, or a real warnings result only if a future context payload actually contains `document_id`.
10. Run Active View Elements. Confirm the empty-filter contract error above.
11. Hide, show, and redock the pane, then repeat a call.
12. Open New Chat. Confirm the session timeline clears.
13. Close and reopen the pane. Confirm there is still one Assistant process.
14. Kill the Assistant process. Confirm Revit stays stable and the pane reports failure and retry.
15. Retry and confirm the Assistant starts again.
16. Confirm a normal external MCP client can still launch `BimIt.Revit.Server` directly.
17. Confirm the existing RevitMCP Approval pane still works independently.
18. With two Revit processes open, confirm each Assistant uses its own `instance_id`.
19. Close Revit and confirm the Assistant and its child MCP Server exit.

The three actions are read workflows. They must not change the model.
