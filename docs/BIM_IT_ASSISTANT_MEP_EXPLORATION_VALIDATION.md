# BIM-IT Assistant MEP exploration validation

Status: the requested live cases that this Plumbing model can show were observed on 2026-10-15. This record is not a merge verdict.

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

## Live record — 2026-10-15

Host: Revit 2026.5, build `26.5.0.55`. The running instance registered at `2026-10-15T23:22:54Z` as `ba6fd9c3-44c7-4585-a2a1-7e1adbf50717`. The active document was Snowdon Towers Sample Plumbing. The add-in, Assistant, and Server were the local Release outputs built from `f0f57649bf458f82c0fce890690f46acf462375f`. The development manifest loads `src\BimIt.Revit.Addin\bin\2026\Release\net8.0-windows\BimIt.Revit.Addin.dll`. GitHub CLI was the company account `QIB-Dave-CoffeeDrivenCoding`. The provider chip showed `GitHub Copilot · auto`.

| Check | Result |
| --- | --- |
| One connected pipe, "Explore ce qui est connecté à cet élément." | OBSERVED — PVC - DWV on L1 - Block 37, sanitary tees, another PVC pipe, and reducers. 7 elements, 6 piping connections. Depth truncation was explained. `Used: Inspect selection, Explore selected MEP`. The user supplied no ids. The first answer also showed shortened element refs. |
| "Qu'est-ce qui est connecté à ce tuyau et jusqu'où peux-tu voir ?" | OBSERVED — the same neighborhood, stopped after 2 connections. The network can continue beyond that depth. |
| Empty selection | OBSERVED — a successful answer that nothing was selected. |
| More than 3 selected elements | OBSERVED — 17 selected, only 3 used as seeds. Depth truncation stayed separate. |
| More than 10 topology nodes | NOT OBSERVED — the largest returned neighborhood was 7 elements, so the 10-description cap was not exercised. |
| No physical connector | OBSERVED after the column was copied into the Plumbing file. W14x109 on Parking has no MEP connector. No circuit was invented. A structural column selected only through the Structural link is invisible to `revit_get_selection`. |
| Inspect Selection | OBSERVED — W14x109, family W Shapes-Column, level Parking. `Used: Model context, Inspect selection`. |
| Model Context, Analyze Warnings, Active View Elements | OBSERVED — Revit 2026.5, the Plumbing project, view `{3D}`, selection count 1. Warnings matched 7. Active view matched 12516 and returned 25, truncated. |
| Copied conduit | OBSERVED — EMT on L4, category Conduits, in the Plumbing file. No physical neighbor. The answer did not invent a connection. `Used: Inspect selection, Explore selected MEP`. |
| Copied lighting fixture | OBSERVED — 1'x4' (2 Lamp) - 120V, family Ceiling Light - Linear Box. No physical connector was exposed. No circuit to the conduit was invented. |

The Plumbing file was modified because a structural column, an EMT conduit, and two lighting fixtures were copied into it from links. The Assistant tools did not write to the model. A connected electrical neighborhood was not available: the copied conduit and luminaires are not physically connected. Linked-element inspection remains future work.
