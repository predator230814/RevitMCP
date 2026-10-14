# BIM-IT Assistant Copilot v1 live validation

Primary target: Revit 2026.5 build 26.5.0.55. Snowdon Towers Sample Plumbing is a suitable model.

This checklist is for a person at a running Revit session. Automated tests do not open Revit and do not require a GitHub Copilot account. No model mutation is expected. Do not treat this document as a merge pass.

Package: `GitHub.Copilot.SDK` 1.0.21, referenced only by `BimIt.Revit.Assistant`.

## Dogfood sign-in

BIM-IT does not show a GitHub OAuth window and does not store a token. The adapter uses the Copilot SDK logged-in-user path (`UseLoggedInUser`). Before the check, sign in to GitHub Copilot on this Windows user with the Copilot CLI (`copilot login`) or the GitHub CLI credentials that the Copilot runtime already accepts. A missing sign-in must leave MCP ready and must not crash the Assistant.

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
12. Confirm the model calls the BIM-IT warnings tool.
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
