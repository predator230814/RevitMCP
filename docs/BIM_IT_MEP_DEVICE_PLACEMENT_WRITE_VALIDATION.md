# MEP device placement write — live validation

- Status: The focused level-based gate on head `7123b11` returned `applied` with the requested absolute X/Y/Z and orientation. Replay returned the same refs, and one Undo removed both copies. This is not a merge pass.
- Target: Revit 2026.5 build `26.5.0.55`
- Build: focused passing run, Release add-in and server `1.0.0+7123b11c396cb6d5f95435848a3675ac5fcf9179`. Earlier runs used `1.0.0+d267b7cbe7d65b0e250bb219e33180bab617c4bc` and `1.0.0+afac5f03c9113e1729d172a9fd76b0a254d851f8`.
- Bridge protocol: 16
- `tools/list`: 14
- Sample: Snowdon Towers Sample HVAC, project, not read-only, not workshared

## First attempt, before this amendment

On 2026-10-16 the wall-only contract was exercised against the installed Snowdon samples in Revit 2026.5. Those samples did not contain an eligible straight-wall-hosted MEP device for that assumption. The wall-hosted preview failed closed. No elements were created. Creation, replay, and Undo were therefore not tested. That attempt is not a pass or a fail of creation.

## Second attempt, level-based, head afac5f0

The template was a Mechanical Equipment instance, family HeatRecoveryUnit, type Heat Recovery Unit (HRU), level L4. Preview reported `placement_mode=level_based` and `ready=true`. No host was present and `HostFace` was absent. Work-plane creation was not run.

Approved apply committed two instances of the same symbol, level, and orientation. Project-axis X was +1200 mm and +2400 mm, and Y was 0. Z was 9804.4 mm too high, equal to the L4 elevation. The level-based `NewFamilyInstance(XYZ, FamilySymbol, Level, StructuralType.NonStructural)` overload treated the supplied Z as an offset from the level, while the intent stored the absolute project Z. Verification rejected the result. Status was `committed_unverified`, not `applied`. No created refs were returned.

Replay of that intent returned `committed_unverified` again. The mechanical-equipment count stayed at 39, and the audit did not gain a second pair. One Undo removed both instances and returned the count to 37. The document was unmodified after that Undo.

The implementation now converts only the level-point creation Z with `target.Z - level.ProjectElevation`. Wall-hosted and level-work-plane creation are unchanged. Post-commit verification still compares the original absolute project target.

## Focused revalidation, head d267b7c

On 2026-10-17 the reloaded Release add-in and server were `1.0.0+d267b7cbe7d65b0e250bb219e33180bab617c4bc`. Revit was 2026.5 build `26.5.0.55`, the bridge protocol was 16, and `tools/list` contained 14 tools. Snowdon Towers Sample HVAC was opened again and was unmodified, so the earlier template move and Comments write were not in the file.

The original HeatRecoveryUnit on L4 was characterized separately. It is level-based, with no host and no `HostFace`. Its Z was again 12899.6 mm, 3095.2 mm above the L4 project elevation of 9804.4 mm. Comments was empty. Five connectors were present and four were connected: Mechanical Exhaust Air 15, Mechanical Return Air 20, Mechanical Supply Air 38, and Mechanical Supply Air 39. Product preview of that connected device returned `level_based` and `ready=true`. It was not used as the creation template. All 37 original mechanical-equipment instances were that same connected type. Across 546 supported devices, none had zero active connections.

A newly placed HeatRecoveryUnit on L2 was the creation template. It was level-based, with no host, no `HostFace`, five connectors, and zero active connections. Its rotation was π. Its absolute Z was 7133.8 mm. On L2, `Elevation` and `ProjectElevation` were both 2463.8 mm, so this run does not distinguish those two level properties. Preview of X +1200 mm and X +2400 mm, with Y 0 and vertical 0, returned `ready=true`. The approval review started and was approved. The model was already modified by that manual placement.

Approved apply committed two instances and returned `committed_unverified`. No created refs were returned. Both copies used the same symbol, level L2, and rotation π. Their Z was 7133.8 mm, the absolute target. Their X/Y were the exact negation of the requested absolute targets: −24025.2 / +1010.8 mm and −25225.2 / +1010.8 mm, instead of +24025.2 / −1010.8 mm and +25225.2 / −1010.8 mm. That is a 180° rotation about the project origin, the same angle as the template. The mechanical-equipment count went from 38 to 40.

Replay of that intent returned `committed_unverified` again. The count stayed at 40. The audit gained one pair only: `apply_status=committed_unverified`, `transaction_status=committed`, `verification_status=failed`. The replay did not add a pair. The record has hashes only. One Undo of these two copies was not performed; the count was still 40 afterward. Parameter regression, reject, and stale were not repeated.

The level-based path now translates the created instance, inside the same transaction and after orientation, by the absolute target minus the re-read location. Wall-hosted and level-work-plane creation are unchanged. Final verification still compares the absolute target and orientation.

## Focused revalidation, head 7123b11

On 2026-10-18 Snowdon Towers Sample HVAC was reopened without saving, so the previous copies were gone. The loaded Release add-in was `1.0.0+7123b11c396cb6d5f95435848a3675ac5fcf9179`. The reopened file had 37 mechanical-equipment instances, all connected, and was unmodified.

A new isolated HeatRecoveryUnit on L2 was the template. It had no host, no `HostFace`, five connectors, and zero active connections. Its rotation was π. On L2, `Elevation` and `ProjectElevation` were both 2463.8 mm. Preview of X +1200 mm and X +2400 mm, with Y 0 and vertical 0, returned `level_based` and `ready=true`. The review was approved.

Apply returned `applied` with exactly two created refs. Both copies were the same family and type, level L2, with no host and rotation π. Their absolute positions matched the targets with zero measured X/Y/Z delta: X −16104.3 mm and −14904.3 mm, Y −15887.9 mm, Z 7133.8 mm. The mechanical-equipment count went from 38 to 40.

Replay returned `applied` with the same two refs. The count stayed at 40. The audit gained one pair only: `apply_status=applied`, `transaction_status=committed`, `verification_status=passed`. The replay did not add a pair. The record has hashes only. One Undo removed both copies. The count returned to 38, the template remained, and both created refs were gone. Parameter regression, reject, stale, and work-plane creation were not repeated.

- [x] The reloaded build, protocol, and tool list match head `7123b11`.
- [x] The reopened sample had no leftover copies from the previous attempt.
- [x] An isolated level-based template at rotation π, with no active MEP connection, previewed `ready=true` for two placements.
- [x] The placement review was approved locally.
- [x] Apply returns `applied`, exactly two refs, and the requested absolute X/Y/Z and orientation.
- [x] Replay returns the same refs, no added device, and no second audit pair.
- [x] The audit pair is `applied` / `committed` / `passed`, with no raw BIM payload.
- [x] One Undo removes the two copies from this attempt.

- [x] The reloaded build, protocol, and tool list match head `d267b7c`.
- [x] The original connected HRU was characterized separately, and its earlier displacement was not in the reopened file.
- [x] An isolated level-based template with no active MEP connection previewed `ready=true` for two placements.
- [x] The placement review was approved locally.
- [ ] Apply returns `applied`, exactly two refs, and the requested absolute X/Y/Z and orientation. Observed `committed_unverified`: Z matched, and X/Y were negated.
- [ ] Replay returns the same refs and no second audit pair. Observed a repeated `committed_unverified`, no added device, and no second audit pair. No created refs were available to compare.
- [ ] The audit pair is `applied` / `committed` / `passed`. Observed `committed_unverified` / `committed` / `failed`, with no raw BIM payload.
- [ ] One Undo removes the two copies from this attempt. Not performed. The count remained 40.

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
- [x] Apply returns `applied` and exactly two devices of the same family, type, and level, at the requested positions, with matching orientation. Head `7123b11` did. The earlier `d267b7c` attempt returned `committed_unverified`.
- [x] Repeat the same apply. The result is `applied` with the same refs. No additional device and no second mutation. Observed on head `7123b11`.
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
