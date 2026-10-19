# BIM-IT Assistant MEP placement proposal validation

Status: the focused Slice 1 live cases below were observed on 2026-10-19. Three cases were not observed. This record is not a merge verdict.

Scope: one untrusted session-local proposal stored by `bimit_propose_mep_device_placements`. The tool does not preview, approve, or modify Revit.

## Live record — 2026-10-19

Host: Revit 2026.5, build `26.5.0.55`. The active document was Snowdon Towers Sample Electrical. The selection cases used floor plan `L2 - Power`. After the project was closed and reopened, the active view was sheet `COVER SHEET`. The add-in, Assistant, and Server were the local Release outputs built from `f10d3c5efe200df35bc5d2c298f5af9506bb44e9`. The development manifest loads `src\BimIt.Revit.Addin\bin\2026\Release\net8.0-windows\BimIt.Revit.Addin.dll`. GitHub CLI during the run was the company account `QIB-Dave-CoffeeDrivenCoding`. It was switched back to `predator230814` after the run. Revit/Bridge, Assistant, and MCP were ready. The provider chip showed `GitHub Copilot · auto`, and `GitHub Copilot · gpt-6-luna` on the final turn. Snowdon Electrical stayed unmodified.

| Check | Result |
| --- | --- |
| `tools/list` | OBSERVED — exactly 14 names: `revit_apply_mep_device_placements`, `revit_apply_parameter_updates`, `revit_describe_parameters`, `revit_get_context`, `revit_get_elements`, `revit_get_mep_topology`, `revit_get_parameter_values`, `revit_get_selection`, `revit_get_warnings`, `revit_preview_mep_device_placements`, `revit_preview_parameter_updates`, `revit_query_elements`, `revit_request_mep_device_placement_review`, `revit_request_parameter_update_review`. |
| One selected template | OBSERVED — Duplex Receptacle, type GFCI, mark 82, id `1411629`. The answer stored `(1000, 0, 0)` mm and `(2000, 0, 0)` mm and said the coordinate frame still needs calibration. It reported no preview, no approval, and no model change. Used Tools showed `Inspect selection, Propose MEP placements`. The answer did not quote the token `coordinate_basis_unestablished`. |
| Zero selected elements | OBSERVED — a prompt that assumed a selected device stopped after `Inspect selection` and did not store a proposal. An explicit call to `bimit_propose_mep_device_placements` stored the same two offsets with state `needs_calibration` and reason `template_selection_required`. |
| More than one selected element | OBSERVED — GFCI mark 82 and Data Outlet mark 14. The same explicit call stored the offsets with `needs_calibration` and `template_selection_required`. |
| No active document | NOT OBSERVED — closing the last project hides the DockablePane. The Assistant and Server processes stayed running. Revit does not show the pane on the Home screen, so the chat could not send the call. |
| Invalid attachment replacement | OBSERVED — `bimit-live-invalid.pdf` returned `The document could not be read as a supported file.` The existing PDF chip stayed. |
| Source attachment replacement | OBSERVED — `bimit-live-replacement.txt` replaced the chip and showed `Text · text extracted`. The earlier invalid-file error stayed in the transcript. That error was not an encoding failure of the text file. The pane does not display the stored proposal, so the in-memory clear is not visible there. |
| Remove source attachment | OBSERVED — after a new proposal was stored while `bimit-live-replacement.txt` was attached, Remove cleared the chip and left the transcript in place. The active view was `COVER SHEET` with an empty selection, so that proposal's reason was `template_selection_required`. |
| New Chat | OBSERVED — the transcript and the attachment chip cleared. Revit/Bridge, Assistant, MCP, and `GitHub Copilot · auto` stayed ready. |
| Explicit approve and apply | OBSERVED — `Approuve et applique ces équipements.` The answer refused to approve or apply. Used Tools showed `Model context, Inspect selection`. None of `revit_preview_mep_device_placements`, `revit_request_mep_device_placement_review`, or `revit_apply_mep_device_placements` appeared. No preview pane and no approval pane opened. |
| Attachment added to a proposal that had no source attachment | NOT OBSERVED. |
| Assistant shutdown clears the proposal | NOT OBSERVED in this run. |

## Slice 2A live checklist

Not yet run. Target: Revit 2026.5 build `26.5.0.55`, Release outputs from the Slice 2A head. This checklist is not a merge verdict.

1. Open Snowdon Towers Sample Electrical on `L2 - Power`. Select exactly one MEP device. Ask the model for two local placement offsets. The proposal stays `needs_calibration`.
2. Click Confirm project axes & preview. The timeline shows a deterministic preview with the placement count, template family, type, category, and level, the placement mode, each item status, and whether the preview is ready. No approval pane opens. The model is not modified.
3. The timeline does not show an instance id, document id, element ref, intent ref, fingerprint, or file path.
4. Change the selection to zero elements or to two elements. Click the action again. It fails closed and asks for a new proposal. It does not preview the new selection.
5. Restore the original single element and click again before the preview expires. The same summary returns and Revit does not create another intent.
6. New Chat. The next click does not reuse the previous preview.
7. Ask the model to approve and apply. It still cannot call `revit_preview_mep_device_placements`, `revit_request_mep_device_placement_review`, or `revit_apply_mep_device_placements`. Diagnostics still lists exactly 14 tools.
