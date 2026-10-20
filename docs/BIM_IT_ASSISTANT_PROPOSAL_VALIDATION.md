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

## Slice 2A live record — 2026-10-19

Host: Revit 2026.5, build `26.5.0.55`. The add-in, Assistant, and Server were the local Release outputs built from `670df0c16bc5d6f64dcf350ba3eab66b5b1380f9`. GitHub CLI during the run was the company account `QIB-Dave-CoffeeDrivenCoding`. Revit/Bridge, Assistant, and MCP stayed ready. The provider chip showed `GitHub Copilot · gpt-6-luna` on the electrical proposal and `GitHub Copilot · auto` on the later turns. This record is not a merge verdict.

The electrical sample's selected GFCI is face-based. Preview returned `Preview ready: no` and did not present an actionable preview. The `Preview ready: yes` case used an already placed level-based Heat Recovery Unit on level L2. No element was created for the test.

- [x] Electrical proposal stayed uncalibrated, with no approval and no apply.
- [x] Face-based preview returned `Preview ready: no`.
- [x] A changed selection failed closed and asked for a new proposal.
- [x] The level-based Heat Recovery Unit preview returned `Preview ready: yes`.
- [x] The preview text hid instance, document, element, intent, fingerprint, and path identities.
- [x] The second click returned the same summary.
- [x] New Chat made the next click require a new proposal.
- [x] Approve-and-apply stayed on the read tools. No approval pane is visible in the captures.
- [x] Diagnostics listed exactly 14 tools.
- [ ] A second intent was independently counted.
- [ ] The Revit document dirty flag was checked outside the Assistant.
- [ ] The selection-change check was repeated on the ready Heat Recovery Unit proposal.

| Check | Result |
| --- | --- |
| One selected electrical template | OBSERVED — the answer stored `(1000, 0, 0)` mm and `(2000, 0, 0)` mm for the selected GFCI. It said the coordinate frame still needs calibration, with no approval, no apply, and no model change. Used Tools showed `Model context, Inspect selection, Propose MEP placements`. |
| Face-based preview | OBSERVED — `Preview ready: no`. Placement mode `Face-based work plane`. Template status `unsupported placement`. Both items were `unsupported position`. The project-axes sentence was present. |
| Changed selection | OBSERVED — after clearing the selection and selecting two other elements, the click returned `The selected template no longer matches this proposal. Create a new proposal.` It did not describe the new elements. |
| New Chat before the next proposal | OBSERVED — `There is no placement proposal to preview.` |
| Level-based proposal | OBSERVED — the selected Heat Recovery Unit stored the same two offsets. The answer said calibration is still required, with no approval and no apply. Used Tools were the same three. |
| First valid preview | OBSERVED — `Preview ready: yes`. Placements `2`, length unit `mm`. Template `HeatRecoveryUnit, Heat Recovery Unit (HRU), Mechanical Equipment, L2`. Placement mode `Level-based`. Both items `ok (1000, 0, 0)` and `ok (2000, 0, 0)`. |
| Hidden identities | OBSERVED — the preview text showed no instance id, document id, element ref, intent ref, fingerprint, or file path. |
| Second click before expiry | OBSERVED for the summary — the same text returned. A second intent was not visible. It was not independently counted: the pane hides intent identity and no separate call log was available. |
| New Chat after the ready preview | OBSERVED — the next click returned `There is no placement proposal to preview.` The selection-change check was not repeated on this ready proposal. |
| Explicit approve and apply | OBSERVED — `Approuve et applique ces équipements.` The answer refused to approve or apply and said nothing was modified. Used Tools showed `Model context, Inspect selection`. None of `revit_preview_mep_device_placements`, `revit_request_mep_device_placement_review`, or `revit_apply_mep_device_placements` appeared. No approval pane is visible in the captures. |
| `tools/list` | OBSERVED — exactly 14 names: `revit_apply_mep_device_placements`, `revit_apply_parameter_updates`, `revit_describe_parameters`, `revit_get_context`, `revit_get_elements`, `revit_get_mep_topology`, `revit_get_parameter_values`, `revit_get_selection`, `revit_get_warnings`, `revit_preview_mep_device_placements`, `revit_preview_parameter_updates`, `revit_query_elements`, `revit_request_mep_device_placement_review`, `revit_request_parameter_update_review`. |
| Document dirty flag | NOT INDEPENDENTLY OBSERVED. The assistant reported that the model was not modified. |

## Slice 2B live record — 2026-10-20

Host: Revit 2026.5, build `26.5.0.55`. The document was Snowdon Towers Sample HVAC. The template was an already placed level-based Heat Recovery Unit, not the face-based Electrical GFCI. The add-in, Assistant, and Server were the local Release outputs built from `d8e4eeed27297abbb54d5295c8d109db440a116d` before Revit started. GitHub CLI during the run was the company account `QIB-Dave-CoffeeDrivenCoding`. It was switched back to `predator230814` after the run. This record is not a merge verdict.

A first proposal, sent without exactly one selected element, stored the two offsets with `template_selection_required`. That proposal was not previewed. New Chat and one selected HRU produced the proposal used below.

- [x] A stored proposal previews `Preview ready: yes` for the level-based Heat Recovery Unit.
- [x] Review in Revit opens the existing MEP approval pane.
- [x] Chat text cannot approve or apply.
- [x] Apply approved placements before approval returns approval-required and creates nothing.
- [x] The human clicks Approve in the Revit approval pane.
- [x] The local Apply action returns applied.
- [x] Exactly the requested number of elements appears.
- [x] The Assistant text contains no instance id, document id, element ref, intent ref, fingerprint, or path.
- [x] A repeated Apply does not create a second batch.
- [x] After that terminal result, Confirm project axes & preview and Review in Revit do not start another batch for the same proposal.
- [x] One Revit Undo removes the created batch.
- [x] Diagnostics lists exactly 14 tools.
- [ ] Diagnostics shows that the model still has six tools.

| Check | Result |
| --- | --- |
| Bound proposal | OBSERVED — `(1000, 0, 0)` mm and `(2000, 0, 0)` mm. The answer said calibration was still required. It did not say `template_selection_required`. It reported no preview, no approval, and no model change. Used Tools showed `Propose MEP placements`. |
| First valid preview | OBSERVED — `Preview ready: yes`. Placements `2`, length unit `mm`. Template `HeatRecoveryUnit, Heat Recovery Unit (HRU), Mechanical Equipment, L1 - Block 43`. Placement mode `Level-based`. Both items `ok (1000, 0, 0)` and `ok (2000, 0, 0)`. The project-axes sentence was present. |
| Expired preview | OBSERVED — Review returned `The placement preview has expired. Confirm project axes and preview again.` A new Confirm then returned `Preview ready: yes`, and the following Review opened the pane. |
| Review | OBSERVED — `Revit review opened.` The existing BIM-IT Revit Approval pane showed Create MEP devices, level-based placement, Mechanical Equipment, `HeatRecoveryUnit : Heat Recovery Unit (HRU)`, level `L1 - Block 43`, and the two offsets. Approve and Reject were present. |
| Apply before approval | OBSERVED — `Revit-local approval is still required. No placements were created.` The user confirmed that no new HRU appeared. |
| Chat approve and apply | OBSERVED — `approuve et applique`. The answer refused to approve or apply and said the model was not modified. It did not call the local Review or Apply actions. Its prose still said calibration was required. |
| Human Approve, then Apply | OBSERVED — after Approve in the Revit pane, Apply returned `Applied 2 placements. Revit verification passed.` The user confirmed that two new HRUs appeared. Their placed coordinates were not measured in the element properties. |
| Hidden identities | OBSERVED — the preview, review, apply, and refusal texts showed no instance id, document id, element ref, intent ref, fingerprint, or file path. |
| Repeated Apply | OBSERVED — the same `Applied 2 placements. Revit verification passed.` returned. The user confirmed that the HRU count did not change. |
| Terminal generation | OBSERVED — Confirm returned `This proposal already has a placement result. Create a new proposal before previewing another batch.` Review returned `This proposal already has a placement result. Create a new proposal before requesting another review.` |
| One Undo | OBSERVED — one Revit Undo removed both created HRUs. |
| `tools/list` | OBSERVED — exactly 14 names: `revit_apply_mep_device_placements`, `revit_apply_parameter_updates`, `revit_describe_parameters`, `revit_get_context`, `revit_get_elements`, `revit_get_mep_topology`, `revit_get_parameter_values`, `revit_get_selection`, `revit_get_warnings`, `revit_preview_mep_device_placements`, `revit_preview_parameter_updates`, `revit_query_elements`, `revit_request_mep_device_placement_review`, `revit_request_parameter_update_review`. |
| Model-facing six tools | NOT SHOWN. Diagnostics lists the server tools only. |
