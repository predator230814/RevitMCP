# BIM-IT Assistant document intake validation

Status: pending. This checklist has not been run. It is not a merge verdict.

Scope: one local document attached to the current BIM-IT Assistant conversation. The primary gate is visual analysis of a plumbing or electrical plan PDF on Revit 2026.5. DOCX, XLSX, JSON, and TXT are secondary gates.

The attachment is conversation context. It is not an MCP tool and it does not write to the model.

## Before the run

- Use the Assistant, Server, and Addin built from the document-intake head.
- Confirm the Copilot session still exposes exactly five model-facing tools: `bimit_get_model_context`, `bimit_get_warnings`, `bimit_get_active_view_elements`, `bimit_inspect_selection`, and `bimit_explore_selected_mep`.
- Confirm production `tools/list` is still exactly 11 tools.
- Confirm the attachment chip shows a file name and a short summary, and never a directory path.

## Cases

1. Open BIM-IT Assistant. Attach a plumbing or electrical plan PDF. The chip shows the file name and a page summary. Ask "Analyse ce plan. Qu'est-ce que tu reconnais comme éléments MEP ?" The model analyzes the rendered pages. No path appears in the pane, the transcript, or diagnostics. The Revit model is not modified.
2. Attach a PDF with more than 4 pages. The chip reports that only the first 4 pages were rendered, and the answer does not claim the omitted pages were inspected.
3. Attach a DOCX and ask "Résume les exigences MEP de ce document."
4. Attach an XLSX and ask "Quels équipements et valeurs importantes vois-tu dans ce tableau ?"
5. Attach a JSON file and ask "Explique-moi la structure et les informations importantes."
6. Attach a TXT file and ask "Résume ce fichier."
7. New Chat clears the attachment chip and the conversation.
8. Remove clears the attachment without starting a new chat.
9. With an attachment present, Model Context, Analyze Warnings, Active View Elements, Inspect Selection, and Explore selected MEP still answer.
10. If the provider is unavailable, Model Context, Analyze Warnings, and Active View Elements still answer.

Record the Revit build, the commit SHA, and each case result in this file only after the run.
