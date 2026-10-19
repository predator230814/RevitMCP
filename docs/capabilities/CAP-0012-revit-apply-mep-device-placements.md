# CAP-0012: Apply MEP device placements

- Status: Implemented. Focused level-based live validation passed on Revit 2026.5 at 7123b11; wall-hosted and level-work-plane creation were not live-proven. See [BIM_IT_MEP_DEVICE_PLACEMENT_WRITE_VALIDATION.md](../BIM_IT_MEP_DEVICE_PLACEMENT_WRITE_VALIDATION.md).
- Date: 2026-10-16

## Purpose

Create one previously previewed and locally approved batch of MEP devices. The caller supplies only the opaque intent reference. Apply does not accept new coordinates or an approval decision.

## Operation class

Write. One Revit transaction for the whole batch. The transaction name is `BIM-IT Create MEP Device Placements`.

## Sequence

The apply follows accepted ADR-0008: intent lookup, document guard, exclusive apply-attempt ownership, audit preflight, consume trusted approval, durable `apply_started`, stale revalidation, one transaction, commit-status check, post-commit verification, terminal outcome, and `apply_completed`.

Apply before approval returns `approval_required` and does not start a transaction.

Stale revalidation re-resolves the template and its placement strategy immediately before mutation. It fails closed when the active document, template, symbol, category, placement strategy, level, insertion point, orientation, human identity, or target occupancy no longer matches the approved snapshot. It also fails closed when occupancy cannot be re-established. Wall-hosted approval additionally fails when the host or wall geometry changed. Level-work-plane approval additionally fails when the work plane is no longer provably that level or the reference direction changed. A strategy change is stale. The apply never switches strategies. A stale intent requires a new preview and a new approval.

Creation stays inside the one transaction and dispatches on the stored strategy. Level-based uses `NewFamilyInstance(XYZ, FamilySymbol, Level, StructuralType.NonStructural)`, then reproduces the approved rotation about a vertical axis through the insertion point and the approved flips. Wall-hosted keeps the host-and-level overload and the flip check. Level-work-plane uses `Level.GetPlaneReference()` and `NewFamilyInstance(Reference, XYZ, XYZ, FamilySymbol)` with the approved reference direction. The batch does not load a family, create a type, create a host, create a work plane, copy parameters, or route a circuit.

Post-commit verification checks the created instance, symbol, category, level, location, placement strategy, and orientation. Wall-hosted verification also checks the approved wall. Level-work-plane verification checks that the level plane identity is still observable. Any mismatch is `committed_unverified`. The orientation check uses the same `1e-4` radian tolerance and does not accept a mismatch.

## Result

Public statuses are `unavailable`, `approval_required`, `in_progress`, `stale`, `transaction_failed`, `committed_unverified`, `indeterminate`, `audit_failed`, and `applied`.

`applied` returns `created_count` and `created_element_refs` in request order. Other statuses omit those fields. A repeated apply after `applied` returns the same refs and does not start another transaction.

`Committed` is the only committed transaction status. `Pending` is `indeterminate`, not success. Post-commit verification mismatch is `committed_unverified`.

## Audit

The batch reuses the accepted ADR-0011 writer and closed event schema. `item_count` is the number of requested placements. The record excludes family and type names, host names, element refs, created refs, coordinates, offsets, prompts, and paths.

## Deferred

Induced Revit failure-dialog behavior remains the existing deferred validation debt. This capability does not add a generic creation API.
