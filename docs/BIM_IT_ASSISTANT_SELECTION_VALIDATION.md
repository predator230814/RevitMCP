# BIM-IT Assistant selection validation

Status: the requested live cases were observed on 2026-10-15. This record is not a merge verdict.

Scope: natural language in BIM-IT Assistant to `bimit_inspect_selection` to the existing MCP session to `revit_get_selection` and `revit_get_elements` to Bridge to Revit 2026.5.

## Before the run

- Use the Assistant, Server, and Addin built from the selection-inspection head.
- Confirm the Copilot session exposes exactly four model-facing tools: `bimit_get_model_context`, `bimit_get_warnings`, `bimit_get_active_view_elements`, and `bimit_inspect_selection`.
- Confirm there is no `/selection` quick action.
- Confirm the model schema for inspect-selection has no arguments.

## Cases

1. Select two real pipes. Ask "Qu'est-ce que j'ai sélectionné ?". Copilot chooses `bimit_inspect_selection`. The pane shows `Used: Inspect selection`. The answer names the real pipes. The user supplies no ids. The model is not modified.
2. The get-selection call uses the hosting Revit `instance_id` and `limit` 10. The get-elements call uses the `document_id` returned by get-selection and the returned refs.
3. Empty selection is a successful answer, not an error.
4. More than 10 selected elements reports the full `selected_count` and truthful truncation. Only 10 refs are inspected.
5. New Chat does not retain the previous selection facts without reading again.
6. Model Context, Analyze Warnings, and Active View Elements still answer.
7. A provider failure does not break those deterministic MCP workflows.

Record the Revit build, the commit SHA, and each case result in this file only after the run.

## Live record — 2026-10-15

Host: Revit 2026.5, build `26.5.0.55`. The running instance registered at `2026-10-15T17:44:54Z` as `e851ff64-0cfb-49db-ba7c-f420ea87f9cb`, with `revit_version` 2026 and `revit_build` `26.5.0.55`. The add-in, Assistant, and Server were the local Release outputs built from `496b1be986d1d88a97b3b5a797f8bad36a527dcd`. The development manifest loads `src\BimIt.Revit.Addin\bin\2026\Release\net8.0-windows\BimIt.Revit.Addin.dll`.

| Check | Result |
| --- | --- |
| Two selected elements, "Qu'est-ce que j'ai sélectionné ?" | PASS — the pane identified a round conduit tee fitting on level L3 and a copper pipe on level Parking. The message showed `Used: Inspect selection`. The user supplied no ids. |
| Empty selection | PASS — an empty selection was a successful answer, not an error. |
| More than 10 selected elements | PASS — the answer kept the full selected count and showed that inspection stopped at 10. It did not present those 10 elements as the entire selection. |
| New Chat | PASS — New Chat did not keep the previous selection facts without reading the selection again. |
| Model Context, Analyze Warnings, Active View Elements | PASS — all three quick actions still answered for the open session. |
| Provider-failure isolation | PASS — a provider failure left Model Context, Analyze Warnings, and Active View Elements usable. |
