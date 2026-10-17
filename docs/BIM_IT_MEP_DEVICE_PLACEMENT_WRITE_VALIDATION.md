# MEP device placement write — live validation

- Status: Level-based creation is not accepted. The 2026-10-16 run committed two devices, then post-commit verification returned `committed_unverified`. This is not a merge pass.
- Target: Revit 2026.5 build `26.5.0.55`
- Build: Release add-in and server `1.0.0+afac5f03c9113e1729d172a9fd76b0a254d851f8`
- Bridge protocol: 16
- `tools/list`: 14
- Sample: Snowdon Towers Sample HVAC, project, not read-only, not workshared

## First attempt, before this amendment

On 2026-10-16 the wall-only contract was exercised against the installed Snowdon samples in Revit 2026.5. Those samples did not contain an eligible straight-wall-hosted MEP device for that assumption. The wall-hosted preview failed closed. No elements were created. Creation, replay, and Undo were therefore not tested. That attempt is not a pass or a fail of creation.

## Second attempt, level-based, head afac5f0

The template was a Mechanical Equipment instance, family HeatRecoveryUnit, type Heat Recovery Unit (HRU), level L4. Preview reported `placement_mode=level_based` and `ready=true`. No host was present and `HostFace` was absent. Work-plane creation was not run.

Approved apply committed two instances of the same symbol, level, and orientation. Project-axis X was +1200 mm and +2400 mm, and Y was 0. Z was 9804.4 mm too high, equal to the L4 elevation. The level-based `NewFamilyInstance(XYZ, FamilySymbol, Level, StructuralType.NonStructural)` overload treated the supplied Z as an offset from the level, while the intent stored the absolute project Z. Verification rejected the result. Status was `committed_unverified`, not `applied`. No created refs were returned.

Replay of that intent returned `committed_unverified` again. The mechanical-equipment count stayed at 39, and the audit did not gain a second pair. One Undo removed both instances and returned the count to 37. The document was unmodified after that Undo.

## Characterization

Before creation, characterize one real device in the Snowdon samples or the internal standard model. Record the derived `placement_mode`, category, family, type, level, whether a host exists, and whether `HostFace` exists. Do not record raw ids.

Preference:

1. A real level-based device.
2. A device the implementation classifies as `work_plane_based_level` with `ready=true`.
3. A wall-hosted device only as a regression, not the primary gate.

If the characterized device is `work_plane_based_face` or `work_plane_based_other`, record that fact and stop that scenario. Do not loosen detection to make the run pass.

## Primary scenario — level-based

- [x] Obtain `document_id` and the template `element_ref` through existing reads.
- [x] Preview two clear non-colliding project-axis offsets. `placement_mode=level_based`, `ready=true`, and the family, type, and level match. The model is unchanged.
- [x] Apply before approval returns `approval_required` and creates no elements.
- [x] Request review. The existing approval pane shows Create MEP devices, Placement: Level based, the template category and family : type, the level, and the exact X/Y/vertical offsets.
- [x] Approve locally.
- [ ] Apply returns `applied` and exactly two devices of the same family, type, and level, at the requested positions, with matching orientation. Observed `committed_unverified`: X and orientation matched, Z was high by the level elevation.
- [ ] Repeat the same apply. The result is `applied` with the same refs. No additional device and no second mutation. Observed a repeated `committed_unverified`, no added device, and no second audit pair. No created refs were available to compare.
- [x] One Revit Undo removes both new instances.

## Stale and reject

- [x] A material change to the template position or orientation before apply returns `stale` and creates no devices.
- [x] A fresh preview, request review, and Reject, then apply, returns `approval_required` and creates no devices.

## Work-plane level

Run creation only when characterization already returned `work_plane_based_level` and preview returned `ready=true`. Otherwise stop.

## Regression and audit

- [x] Existing parameter preview, review, and apply still work. Comments on the same template changed from empty to `BIM-IT live check`, read back correctly, and a repeated apply stayed `applied` without a second audit pair.
- [x] `revit_get_context` still works after the committed attempt.
- [x] The two committed instances were inspected through existing reads before Undo. The apply result omitted created refs because the status was `committed_unverified`.
- [x] The committed MEP attempt produced one `apply_started` / `apply_completed` pair. Its status is `committed_unverified`, transaction `committed`, verification `failed`.
- [x] The MEP retry did not produce a second mutation or audit pair.
- [x] The audit contains no family or type names, placement mode, element refs, coordinates, offsets, prompts, or file paths. The later stale attempt and the parameter apply are separate pairs for those separate intents.

Induced Revit failure-dialog behavior remains the existing deferred validation debt.
