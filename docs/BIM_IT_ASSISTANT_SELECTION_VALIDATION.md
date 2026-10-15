# BIM-IT Assistant selection validation

Status: pending. This checklist is not a pass.

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
