# BIM-IT Assistant document intake validation

Status: the requested live cases were observed on 2026-10-16. The provider-unavailable quick-action case was not re-run. This record is not a merge verdict.

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

## Live record — 2026-10-16

Host: Revit 2026.5, build `26.5.0.55`. The active document was Snowdon Towers Sample Electrical. The active view during Model Context was POWER PLAN L4, a drawing sheet. The add-in, Assistant, and Server were the local Release outputs built from `312ded3ca9584d1309f76e6857d5eaf30323613a`. The development manifest loads `src\BimIt.Revit.Addin\bin\2026\Release\net8.0-windows\BimIt.Revit.Addin.dll`. GitHub CLI was the company account `QIB-Dave-CoffeeDrivenCoding`. The provider chip showed `GitHub Copilot · auto`. Revit/Bridge, Assistant, and MCP were ready. No directory path appeared on any chip or in any answer. Model Context reported `Modified: False`.

| Check | Result |
| --- | --- |
| One-page electrical PDF, "Analyse ce plan. Qu'est-ce que tu reconnais comme éléments MEP ?" | OBSERVED — chip `Snowdon Towers Sample Electrical…`, `PDF · 1 page`. The answer identified POWER PLAN L4, sheet E104: receptacles, GFCI, and circuit tags. It said plumbing and HVAC were not on that sheet. It did not claim a model edit. |
| PDF longer than 4 pages | OBSERVED — chip `PDF · 4 of 26 pages`. The summary said pages 1 to 4 were visible and pages 5 to 26 were not analyzed. |
| DOCX, "Résume les exigences MEP de ce document." | OBSERVED — chip `Word · text extracted`. The answer described an interference-coordination process and did not invent design requirements. |
| XLSX, "Quels équipements et valeurs importantes vois-tu dans ce tableau ?" | OBSERVED — chip `Excel · text extracted`. The answer said the sheet had no MEP equipment: one `userName` column of identifiers. |
| JSON, "Explique-moi la structure et les informations importantes." | OBSERVED — chip `JSON · text extracted` for `insulation_rules.json`. The answer described insulation-thickness rules by system and nominal diameter, and noted missing units and gaps. |
| TXT, "Résume ce fichier." | OBSERVED for UTF-8 — chip `Text · text extracted` for `Test.txt`. The answer summarized workstation assignments. A non-UTF-8 text file was rejected with "The document could not be read as a supported file." and no path. |
| Remove | OBSERVED — the chip disappeared and the TXT answer stayed. |
| Replace | OBSERVED — each newly attached file replaced the previous chip. |
| New Chat | OBSERVED — the conversation and the chip cleared. The provider chip stayed `GitHub Copilot · auto`. |
| Model Context, Analyze Warnings, Active View Elements, with the one-page PDF still attached | OBSERVED — Revit 2026.5, the Electrical project, view POWER PLAN L4, selection count 0. Warnings matched 0. Active view matched 4 and returned 4. The chip stayed. |
| Inspect Selection, with the PDF still attached | OBSERVED — Duplex Receptacle, family Duplex Receptacle, type Standard, view L4 - Power. `Used: Model context, Inspect selection`. The document name was not in Used. |
| Explore selected MEP, with the PDF still attached | OBSERVED — the same receptacle had no physical connector neighbor. `Used: Explore selected MEP`. The electrical circuit to a panel is system data and was not invented. |
| PNG | OBSERVED — chip `Image · 1 page`. The model described the picture. This was an extra check of the bounded image decode. JPG was not run. |
| Provider unavailable, quick actions still answer | NOT OBSERVED — not re-run in this session. |
