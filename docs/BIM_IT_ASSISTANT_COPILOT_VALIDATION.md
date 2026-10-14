# BIM-IT Assistant Copilot v1 live validation

Primary target: Revit 2026.5 build 26.5.0.55. Snowdon Towers Sample Plumbing is a suitable model.

This checklist is for a person at a running Revit session. Automated tests do not open Revit and do not require a GitHub Copilot account. No model mutation is expected. Do not treat this document as a merge pass.

Package: `GitHub.Copilot.SDK` 1.0.21, referenced only by `BimIt.Revit.Assistant`.

## Dogfood sign-in

BIM-IT does not show a GitHub OAuth window and does not store a token. The adapter uses the Copilot SDK logged-in-user path (`UseLoggedInUser`) in `CopilotClientMode.Empty`. Empty mode disables keytar, so credentials saved by `copilot login` in the system keychain are not guaranteed to be visible. Before the check, sign in with GitHub CLI:

```powershell
gh auth login
```

A missing sign-in must leave MCP ready and must not crash the Assistant.

Build the same Release outputs as [BIM_IT_ASSISTANT_VALIDATION.md](BIM_IT_ASSISTANT_VALIDATION.md). Load the Revit 2026 add-in from the repository output so the Assistant and Server can be found beside it.

## Checklist

1. Start Revit.
2. Confirm no Assistant process and no Copilot runtime start before opening BIM-IT Assistant.
3. Open BIM-IT Assistant.
4. Revit / Bridge shows ready.
5. Assistant shows ready.
6. MCP shows ready.
7. AI Provider shows `GitHub Copilot · auto` when signed in, or `GitHub Copilot · unavailable` with a sign-in message. A concrete model name appears only when the SDK reports one.
8. Model Context still returns the open session.
9. Analyze Warnings still returns the real warning result.
10. Active View Elements still returns the real active-view query.
11. Type `Analyse les warnings de ce modèle.`
12. Confirm the answer includes `Used: Analyze warnings`. That line is the evidence that the model invoked `bimit_get_warnings`.
13. Confirm that tool uses the existing MCP Server and Bridge v12 path.
14. Confirm the natural-language answer matches the real warning result.
15. Ask `Combien y en a?` Confirm the count stays on the warning result already retrieved.
16. Choose New Chat.
17. Ask `Combien y en avait?` Confirm the previous conversation is not still in effect.
18. Kill the Assistant process.
19. Confirm Revit and the Bridge stay up.
20. Retry. Confirm MCP and the provider recover.
21. Confirm BIM-IT Revit Approval still works on its own. Its GUID stays `C3A91E74-5D28-4B6F-A0E1-9F4C2B7D18A5`.
22. Close Revit. Confirm the Assistant, its MCP Server child, and the Copilot runtime child exit.

The three quick actions stay beside the composer after messages exist. No Revit model mutation should occur.

## Live record — 2026-10-14

Host: Revit 2026.5, build `26.5.0.55`. One process had Snowdon Towers Sample Plumbing open. The active view was sheet `P000 - COVER SHEET`. The add-in, Assistant, and Server were the local Release outputs built from `3b565be310b977ee65fb830d64cbe5074e8b7853`. This record is not a merge pass.

| Check | Result |
| --- | --- |
| Startup before the pane | PASS — no Assistant or Server process |
| Revit / Bridge, Assistant, MCP | PASS — ready |
| AI Provider | PASS — `GitHub Copilot · auto` after the Copilot Business account was active |
| Model Context | PASS — Revit 2026 (`26.5.0.55`), Snowdon Towers Sample Plumbing, COVER SHEET, selection 0, not modified |
| Analyze Warnings | PASS — Matched 7, Warning 7, error 0, document corruption 0, other 0, element truncation |
| Active View Elements | PASS — Matched 24, Returned 24 |
| `Analyse les warnings de ce modèle.` | PASS — answer matched those 7 warnings, and the message showed `Used: Analyze warnings` |
| `Combien y en a?` | PASS — 7 |
| New Chat, then `Combien y en avait?` | PASS — the count came from a new warnings read. The message showed `Used: Analyze warnings` |
| Kill Assistant | PASS — Revit / Bridge stayed ready. Assistant and MCP became unavailable. Retry appeared |
| Retry | PASS — Assistant, MCP, and `GitHub Copilot · auto` returned |
| Approval smoke | PASS — Cancel only. The model stayed unmodified |
| Close Revit | PASS — Revit, Assistant, Server, and the Copilot runtime processes exited |

Observed:

- The ribbon button still has no icon. No `BimIt.Revit.Assistant.exe` or `BimIt.Revit.Server.exe` started before the click.
- The first natural-language turn used GitHub account `predator230814`. Windows Security blocked `individual.githubcopilot.com`. The pane returned a session error for `https://api.individual.githubcopilot.com/models` (`SEC_E_ILLEGAL_MESSAGE`, os error `-2146893018`). Revit stayed open. That account does not hold the company Copilot seat.
- The successful turns used GitHub account `QIB-Dave-CoffeeDrivenCoding`, which has a GitHub Copilot Business seat managed by tetra-tech. `gh auth login` was added beside the personal account. Revit was closed and reopened so the Assistant would read the new `gh` user. After the session, `gh auth switch` restored `predator230814` as the active account. The Business account stayed logged in and inactive.
- `api.business.githubcopilot.com` answered from this workstation. A direct `gh` call to `/copilot_internal/v2/token` returned HTTP 403. The Revit turn still completed.
- `Analyse les warnings de ce modèle.` described the same three plumbing groups as the quick action: flow calculation on Domestic Cold Water 5, 16, 30, and 31 and Domestic Hot Water 5; a disconnected Building Sanitary network; and a Domestic Cold Water 1 flow-direction mismatch. It reported no errors, no document corruption, and element truncation. `Used: Analyze warnings` was under the message.
- `Combien y en a?` answered that there were 7 warnings.
- New Chat cleared the visible timeline. `Combien y en avait?` answered 7 again and showed `Used: Analyze warnings`, so the count was a new tool read rather than the cleared transcript.
- Killing `BimIt.Revit.Assistant` left Revit / Bridge ready, set Assistant and MCP to unavailable, and showed Retry. The provider chip kept the last SDK label, `GitHub Copilot · gpt-6-luna`. Process exit does not clear that label. Retry hid the button and returned Revit / Bridge, Assistant, and MCP to ready, with AI Provider `GitHub Copilot · auto`.
- BIM-IT Revit Approval is not on the ribbon or under View > User Interface. A separate stdio `BimIt.Revit.Server` previewed Project Information / Author from `Autodesk` to `BIM-IT-APPROVAL-SMOKE`. Preview was ready, and `is_modified` stayed false. `revit_request_parameter_update_review` returned `started`. Cancel left the pane titled BIM-IT Revit Approval on “No approval request is currently active.” Approve was not used, and apply was not called. The pane does not display its GUID. The GUID in source remains `C3A91E74-5D28-4B6F-A0E1-9F4C2B7D18A5`.
- Closing Revit removed the Revit, Assistant, and Server processes. The Copilot runtime processes were gone. Their directories remained under `%LOCALAPPDATA%\BIM-IT\Assistant\CopilotRuntime`.
