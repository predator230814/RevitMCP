# CAP-0012: Apply MEP device placements

- Status: Implemented. Live Revit validation is pending.
- Date: 2026-10-16

## Purpose

Create one previously previewed and locally approved batch of MEP devices. The caller supplies only the opaque intent reference. Apply does not accept new coordinates or an approval decision.

## Operation class

Write. One Revit transaction for the whole batch. The transaction name is `BIM-IT Create MEP Device Placements`.

## Sequence

The apply follows accepted ADR-0008: intent lookup, document guard, exclusive apply-attempt ownership, audit preflight, consume trusted approval, durable `apply_started`, stale revalidation, one transaction, commit-status check, post-commit verification, terminal outcome, and `apply_completed`.

Apply before approval returns `approval_required` and does not start a transaction.

Stale revalidation fails closed when the template, symbol, category, host, level, location, straight host geometry, orientation, or target occupancy no longer matches the approved snapshot. A stale intent requires a new preview and a new approval.

## Result

Public statuses are `unavailable`, `approval_required`, `in_progress`, `stale`, `transaction_failed`, `committed_unverified`, `indeterminate`, `audit_failed`, and `applied`.

`applied` returns `created_count` and `created_element_refs` in request order. Other statuses omit those fields. A repeated apply after `applied` returns the same refs and does not start another transaction.

`Committed` is the only committed transaction status. `Pending` is `indeterminate`, not success. Post-commit verification mismatch is `committed_unverified`.

## Audit

The batch reuses the accepted ADR-0011 writer and closed event schema. `item_count` is the number of requested placements. The record excludes family and type names, host names, element refs, created refs, coordinates, offsets, prompts, and paths.

## Deferred

Induced Revit failure-dialog behavior remains the existing deferred validation debt. This capability does not add a generic creation API.
