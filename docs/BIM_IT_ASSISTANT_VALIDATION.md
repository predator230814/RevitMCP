# BIM-IT Assistant live validation

Primary target: Revit 2026.5 build 26.5.0.55.

This checklist is for a person at a running Revit session. Automated tests do not open Revit. No model mutation is expected from the three v1 actions. Do not treat this document as a merge pass. The read path and the Approval smoke below were exercised on 2026-10-13. Quick-action visibility remains a UX follow-up.

## Build and load

Build these Release outputs before the check. Assistant and Server target `net10.0` and need the .NET 10 runtime, including when Revit 2026 hosts the add-in.

| Piece | Output |
| --- | --- |
| Revit 2026 add-in | `src/BimIt.Revit.Addin/bin/2026/Release/net8.0-windows/BimIt.Revit.Addin.dll` |
| Assistant | `src/BimIt.Revit.Assistant/bin/Release/net10.0/BimIt.Revit.Assistant.exe` |
| MCP server | `src/BimIt.Revit.Server/bin/Release/net10.0/BimIt.Revit.Server.exe` |

Commands from the repository root:

```text
dotnet build src/BimIt.Revit.Addin/BimIt.Revit.Addin.csproj -c Release -p:RevitVersion=2026
dotnet build src/BimIt.Revit.Assistant/BimIt.Revit.Assistant.csproj -c Release
dotnet build src/BimIt.Revit.Server/BimIt.Revit.Server.csproj -c Release
```

Load `BimIt.Revit.Addin.dll` from that 2026 Release folder. The application class is `BimIt.Revit.Addin.RevitMcpApplication`. This repository does not generate a Revit `.addin` manifest. Point the existing Revit 2026 add-in manifest at that DLL. The usual development manifest location is:

```text
%APPDATA%\Autodesk\Revit\Addins\2026\
```

The pane finds the Assistant and Server by walking up from the loaded add-in directory to the repository build output. Load the DLL from the repository output above so that walk succeeds. `BIMIT_REVIT_ASSISTANT` and `BIMIT_REVIT_SERVER` can override those paths.

## Expected v1 actions

On this v12 host:

- Model Context succeeds and shows the open session. An active document includes `document_id`.
- Analyze Warnings calls `revit_get_warnings` with that `document_id` and renders the real result, including zero warnings and truncation.
- Active View Elements calls `revit_query_elements` with `scope=active_view`, `filters: {}`, and `limit=25`, and renders the real result.

The pane must not invent a document id. If context has a document and no `document_id`, Analyze Warnings reports a Bridge v12 compatibility error and does not call warnings. Document scope still rejects an empty filter object. AI Provider stays "Not configured". Natural language stays in the session timeline and is answered with that fact.

## Checklist

1. Start Revit normally.
2. Confirm the BIM-IT Assistant ribbon button exists.
3. Confirm no Assistant or MCP Server process started merely because Revit started.
4. Click BIM-IT Assistant.
5. Confirm the pane opens and the WebView renders.
6. Confirm status moves truthfully through Revit/Bridge, then Assistant, then MCP. AI Provider stays "Not configured".
7. Open diagnostics and confirm `tools/list` reports the current 10-tool MCP surface.
8. Run Model Context. Confirm the result matches the open session and includes `document_id` when a document is open.
9. Run Analyze Warnings. Confirm a real `revit_get_warnings` result, including a zero-warning model if that is the open document.
10. Run Active View Elements. Confirm a real `revit_query_elements` result for `filters: {}`.
11. Hide, show, and redock the pane, then repeat a call. Confirm there is still one Assistant process.
12. Open New Chat. Confirm the session timeline clears.
13. Close and reopen the pane. Confirm there is still one Assistant process.
14. Kill the Assistant process. Confirm Revit, the Bridge, and the Approval pane stay up, and the Assistant pane becomes unavailable and shows Retry.
15. Click Retry. Confirm a new Assistant process and a successful call.
16. Confirm a normal external MCP client can still launch `BimIt.Revit.Server` directly, without the Assistant.
17. Confirm the BIM-IT Revit Approval pane still works independently. Its GUID stays `C3A91E74-5D28-4B6F-A0E1-9F4C2B7D18A5`.
18. With two Revit processes open, confirm each Assistant uses its own `instance_id` and its own process.
19. Close Revit and confirm the Assistant and its child MCP Server exit.

The three actions are read workflows. They must not change the model.

## Live record — 2026-10-13

Host: Revit 2026.5, build `26.5.0.55`. The first process had Snowdon Towers Sample Plumbing open. The second process had Snowdon Towers Sample Electrical open. Both active views were COVER SHEET. This record is not a merge pass.

| Check | Result |
| --- | --- |
| Model Context | PASS |
| Analyze Warnings | PASS — 7 warnings |
| Active View Elements | PASS — 24/24 |
| kill/retry | PASS |
| external MCP | PASS |
| two Revit instances | PASS |
| per-instance shutdown | PASS |
| quick-action visibility | UX follow-up |
| Approval smoke | PASS |

Observed:

- The ribbon button appears with no icon. No `BimIt.Revit.Assistant.exe` or `BimIt.Revit.Server.exe` starts before the click.
- The pane reaches Revit / Bridge ready, Assistant ready, and MCP ready. AI Provider stays "Not configured".
- Model Context reported Revit 2026 (26.5.0.55), the open project, COVER SHEET, and selection count 0.
- Analyze Warnings displayed a real result: Matched 7, Warning 7, error 0, document corruption 0, other 0, with element truncation. An external `BimIt.Revit.Server` call on the same Plumbing session returned `document_id` `04dc9749-6f0b-426c-bb04-4e10cb4faad6`. The pane did not report `bridge_v12_required`.
- Active View Elements displayed Matched 24, Returned 24 for the active view. That is the v12 `scope=active_view`, `filters: {}` query, under the limit of 25.
- Closing, reopening, and redocking the pane kept one Assistant process. The session timeline survived closing the pane. New chat cleared it.
- Killing the Assistant left Revit / Bridge ready, set Assistant and MCP to unavailable, and showed Retry. Retry returned both to ready with one new pair of processes.
- A second stdio `BimIt.Revit.Server` called `revit_get_context` while the pane stayed ready, then that extra server was closed.
- Two Revit processes produced two Assistant processes and two Server processes. Context stayed on each open document: Plumbing in one, Electrical in the other. Closing the Electrical process left one pair. Closing the last Revit removed both processes.
- After the first result, the three quick-action buttons scroll above the visible pane. `/context`, `/warnings`, and `/active-view` still run. That visibility is a UX follow-up, not a failed read.
- BIM-IT Revit Approval does not appear under View > User Interface. The read workflows did not open it.
- Approval smoke, same Plumbing session, `document_id` `f77533b8-3869-4c55-8770-ef5485a02ebc`: preview of Project Information / Author from `Autodesk` to `RevitMCP-PR77-SMOKE` returned ready, and `is_modified` stayed false. `revit_request_parameter_update_review` returned `Started` and opened a pane titled BIM-IT Revit Approval. The pane showed the current value, the proposed value, and Approve, Reject, and Cancel. Cancel returned the pane to "No approval request is currently active." Approve was not used, and apply was not called. The Approval pane GUID remains `C3A91E74-5D28-4B6F-A0E1-9F4C2B7D18A5`.
- The first click failed because `ShowBimItAssistantCommand` had no `Transaction` attribute. The live click succeeded after the attribute was added. The committed mode is `TransactionMode.Manual`.
