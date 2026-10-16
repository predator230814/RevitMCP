# MEP device placement write — live validation

- Status: Pending. Do not treat this record as PASS.
- Target: Revit 2026.5 build `26.5.0.55`
- Build: Release outputs from the PR head
- Record when run: commit SHA, addin version, selected Bridge protocol, `tools/list` count

Primary sample: Snowdon Towers Sample Electrical, or another disposable model with one straight-wall-hosted receptacle.

## Primary scenario

- [ ] Identify one existing wall-hosted Duplex Receptacle and obtain its `element_ref` and `document_id` through existing reads.
- [ ] Preview `+1200 mm` and `+2400 mm` along the host, vertical `0`. `ready=true`. The preview identifies family, type, host, and level. The model is unchanged.
- [ ] Apply before approval returns `approval_required` and creates no elements.
- [ ] Request review. The approval pane shows Create MEP devices, the template family and type, the wall and level, and the two requested offsets.
- [ ] Approve locally.
- [ ] Apply returns `applied`, exactly two created refs, and exactly two new devices of the same family, type, wall, level, placement, and orientation.
- [ ] Repeat the same apply. The result is `applied` with the same refs. No additional device and no second mutation.
- [ ] One Revit Undo removes both new instances.

## Stale and reject

- [ ] A fresh preview, then a material move of the template before apply, returns `stale` and creates no devices.
- [ ] A fresh preview, request review, and Reject, then apply, returns `approval_required` and creates no devices.

## Regression and audit

- [ ] Existing parameter preview, review, and apply still work.
- [ ] `revit_get_context` still works after the successful creation.
- [ ] The created refs can be inspected through existing reads.
- [ ] One successful mutation produced one `apply_started` / `apply_completed` pair.
- [ ] The retry did not produce a second mutation or audit pair.
- [ ] The audit contains no family or type names, element refs, coordinates, offsets, prompts, or file paths.

Induced Revit failure-dialog behavior remains the existing deferred validation debt.
