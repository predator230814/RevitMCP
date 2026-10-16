# MEP device placement write — live validation

- Status: Pending. Do not treat this record as PASS. Do not treat it as a creation failure.
- Target: Revit 2026.5 build `26.5.0.55`
- Build: Release outputs from the amended PR head
- Record when run: commit SHA, addin version, selected Bridge protocol, `tools/list` count

## First attempt, before this amendment

On 2026-10-16 the wall-only contract was exercised against the installed Snowdon samples in Revit 2026.5. Those samples did not contain an eligible straight-wall-hosted MEP device for that assumption. The wall-hosted preview failed closed. No elements were created. Creation, replay, and Undo were therefore not tested. That attempt is not a pass or a fail of creation.

## Characterization

Before creation, characterize one real device in the Snowdon samples or the internal standard model. Record the derived `placement_mode`, category, family, type, level, whether a host exists, and whether `HostFace` exists. Do not record raw ids.

Preference:

1. A real level-based device.
2. A device the implementation classifies as `work_plane_based_level` with `ready=true`.
3. A wall-hosted device only as a regression, not the primary gate.

If the characterized device is `work_plane_based_face` or `work_plane_based_other`, record that fact and stop that scenario. Do not loosen detection to make the run pass.

## Primary scenario — level-based

- [ ] Obtain `document_id` and the template `element_ref` through existing reads.
- [ ] Preview two clear non-colliding project-axis offsets. `placement_mode=level_based`, `ready=true`, and the family, type, and level match. The model is unchanged.
- [ ] Apply before approval returns `approval_required` and creates no elements.
- [ ] Request review. The existing approval pane shows Create MEP devices, Placement: Level based, the template category and family : type, the level, and the exact X/Y/vertical offsets.
- [ ] Approve locally.
- [ ] Apply returns `applied` and exactly two devices of the same family, type, and level, at the requested positions, with matching orientation.
- [ ] Repeat the same apply. The result is `applied` with the same refs. No additional device and no second mutation.
- [ ] One Revit Undo removes both new instances.

## Stale and reject

- [ ] A material change to the template position or orientation before apply returns `stale` and creates no devices.
- [ ] A fresh preview, request review, and Reject, then apply, returns `approval_required` and creates no devices.

## Work-plane level

Run creation only when characterization already returned `work_plane_based_level` and preview returned `ready=true`. Otherwise stop.

## Regression and audit

- [ ] Existing parameter preview, review, and apply still work.
- [ ] `revit_get_context` still works after the successful creation.
- [ ] The created refs can be inspected through existing reads.
- [ ] One successful mutation produced one `apply_started` / `apply_completed` pair.
- [ ] The retry did not produce a second mutation or audit pair.
- [ ] The audit contains no family or type names, placement mode, element refs, coordinates, offsets, prompts, or file paths.

Induced Revit failure-dialog behavior remains the existing deferred validation debt.
