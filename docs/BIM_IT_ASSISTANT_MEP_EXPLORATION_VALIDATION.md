# BIM-IT Assistant MEP exploration validation

Status: pending. Do not treat this checklist as a pass.

Scope: natural language in BIM-IT Assistant to `bimit_explore_selected_mep` to the existing MCP session to `revit_get_selection`, `revit_get_mep_topology`, and `revit_get_elements` to Bridge to Revit 2026.5.

The tool is physical connectivity only. Electrical circuits, logical systems, loads, panels, and phase or voltage membership are out of scope. A receptacle with no eligible physical connector can return `no_connectors` without failing the tool.

## Before the run

- Use the Assistant, Server, and Addin built from the MEP-exploration head.
- Confirm the Copilot session exposes exactly five model-facing tools: `bimit_get_model_context`, `bimit_get_warnings`, `bimit_get_active_view_elements`, `bimit_inspect_selection`, and `bimit_explore_selected_mep`.
- Confirm there is no `/mep` or `/selection` quick action.
- Confirm the model schema for explore-selected-MEP has no arguments.
- Confirm production `tools/list` is still exactly 11 tools.

## Cases

1. Select one real connected MEP element, ideally a pipe, duct, conduit, cable tray, or physically connected equipment. Ask "Explore ce qui est connecté à cet élément." Copilot chooses `bimit_explore_selected_mep`. The pane shows `Used: Explore selected MEP`. The answer identifies the selected object and real physically connected neighbors. The user supplies no ids. The model is not modified.
2. Ask "Qu'est-ce qui est connecté à ce tuyau et jusqu'où peux-tu voir ?" The answer explains the bounded neighborhood: selection seeds capped at 3, topology depth 2, at most 25 nodes and 40 edges, and descriptions for at most 10 nodes.
3. Empty selection is a successful answer. Topology and element inspection are not required for that answer.
4. More than 3 selected elements reports the full selected count and explains that only 3 seeds were explored.
5. A topology result with more than 10 nodes does not imply that every node received a name, category, family, type, and level description.
6. A selected device with no eligible physical connector is explained honestly. The answer does not invent an electrical circuit.
7. Inspect Selection still answers, and Model Context, Analyze Warnings, and Active View Elements still answer.
8. If a convenient sample model contains a conduit or cable tray, run one electrical-domain example. Absence of that sample does not by itself block the automated electrical and cable-tray coverage.

Record the Revit build, the commit SHA, and each case result in this file only after the run.
