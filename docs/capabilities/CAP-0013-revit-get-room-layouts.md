# CAP-0013: Read room layouts

- Status: Accepted
- Date: 2026-10-21
- Product Owner: Dave

## Purpose

Return bounded room geometry so a deterministic matcher can compare an interpreted room pattern with real Revit rooms. The caller supplies known room references. The capability does not search the model and does not place devices.

## Operation class

Read. No transaction, subtransaction, or transaction group.

## Input

`document_id` is required and is compared exactly with the active document, including an empty string. `element_refs` contains 1 to 20 opaque room references. Each reference is at most 128 characters. Duplicates are invalid. `instance_id` is Server routing only and is not part of the Bridge request.

## Output

`length_unit` is `m`. `chord_tolerance_m` is `0.05`. Each item is `ok`, `not_found`, `not_a_room`, or `boundary_unavailable`.

An `ok` room includes a safe name, number, and level, a shoelace centroid in metres, and boundary loops from `SpatialElement.GetBoundarySegments` using finish boundaries. Loops are capped at 4. Each loop is capped at 48 vertices. Points closer than half the chord tolerance are collapsed. Extra loops or vertices set the matching truncated flag.

Doors and windows are collected in the active document only, up to 4000 openings. A feature is included when Revit reports the room as its from-room or to-room, including the phase-specific lookup. At most 12 features are returned per room. A feature has kind `door` or `window`, a plan location in metres, an optional facing direction, and an optional width in metres.

Rejected items omit geometry. Areas and spaces are `not_a_room`. A missing boundary is `boundary_unavailable`.

## Errors

`INVALID_ROOM_LAYOUT`, `NO_ACTIVE_DOCUMENT`, `DOCUMENT_CONTEXT_CHANGED`, `REVIT_EXECUTION_TIMEOUT`, and `REVIT_EXECUTION_FAILED`.

## Boundaries

No linked-document traversal. No internal feet in the contract. No path, username, or cloud path. The Assistant strips element and document identities before the model or the WebView sees a pattern review.

## Deferred

Pattern application, linked CAD, placed PDF or image geometry, and Revit exemplar rooms.
