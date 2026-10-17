# CAP-0011: Preview MEP device placements

- Status: Implemented. Live Revit validation is pending.
- Date: 2026-10-16

## Purpose

Preview one bounded batch of additional MEP devices relative to one existing MEP device. The existing instance is the trusted template for family type, category, level, placement strategy, and orientation.

This is the preview phase of one controlled write. It creates an ephemeral intent when every placement is eligible. It does not modify the Revit model.

## Operation class

Write preview. `readOnlyHint` is false because preview stores ephemeral intent state. It is not destructive and it does not modify the model.

## Input

`document_id`, `template_element_ref`, `length_unit_id`, and `placements` (1..20). `instance_id` is Server routing only and is not part of the transport-neutral request.

Each placement is a finite signed `offset_x`, `offset_y`, and `vertical_offset` in the caller's length unit. These are Revit project-axis offsets from the template insertion point. They are not shared, geographic, PDF, or wall-local coordinates. Exact duplicate triples are invalid. After `UnitUtils` conversion with a valid `SpecTypeId.Length` unit, `abs(offset_x)` and `abs(offset_y)` must each be at most 100 metres and `abs(vertical_offset)` at most 10 metres. There is no public internal-feet field.

## Template

The template must be a project `FamilyInstance` in a supported MEP device category, with a `LocationPoint`, an associated level, a finite position, and a resolvable `FamilySymbol`. Supported categories are electrical fixtures, lighting devices, lighting fixtures, data devices, communication devices, fire alarm devices, security devices, telephone devices, nurse call devices, electrical equipment, mechanical equipment, plumbing fixtures, and air terminals. Eligibility uses `BuiltInCategory` identity.

The public `placement_mode` is closed: `level_based`, `wall_hosted`, `work_plane_based_level`, `work_plane_based_face`, or `work_plane_based_other`.

- `FamilyPlacementType.OneLevelBased` is `level_based`. No wall is required.
- `FamilyPlacementType.OneLevelBasedHosted` is `wall_hosted` only when the host is a same-document straight bounded wall. A curved host, a non-wall host, or a missing host stays fail-closed.
- `FamilyPlacementType.WorkPlaneBased` is `work_plane_based_face` when `FamilyInstance.HostFace` is not null. That mode is characterized and is not creatable.
- The same placement type is `work_plane_based_level` only when `HostFace` is null and `SketchPlane.GetPlaneReference()` from `SKETCH_PLANE_PARAM`'s element id is equal, by `Reference.EqualTo`, to `Level.GetPlaneReference()` of the associated level. Level display names, parameter text, and localized strings are not proof.
- Every other work plane, including a reference plane or an unreadable identity, is `work_plane_based_other` and is not creatable.

Other `FamilyPlacementType` values remain `unsupported_placement` without a placement mode. Face and other work-plane templates still return their characterized `placement_mode` with status `unsupported_placement`. Wall-hosted results additionally include `host_element_ref` and `host_name`. The result does not include numeric element ids, raw work-plane values, reference stable representations, geometry, or paths.

## Placement

The target is the template insertion point plus the converted project-axis offsets: X, Y, and Z independently.

Level-based occupancy is the same symbol on the same level within 1 mm. Wall-hosted occupancy is the same symbol on the same wall within 1 mm, and the horizontal target must already lie on the same wall placement line, within 1 mm and within the finite wall segment. An off-wall point is `unsupported_position`. A point past the finite segment is `out_of_host_bounds`. The implementation does not project, clamp, change host, extend the wall, or choose another level. Work-plane-level occupancy is the same symbol on the same proven level work plane within 1 mm.

A zero/zero/zero offset is occupied by the template. If the occupancy scan cannot be completed, preview does not store an intent.

Level-based and level-work-plane intents bind `LocationPoint.Rotation` together with facing and hand flips. Wall-hosted intents bind the flips and the wall. The angular tolerance used later for verification is `1e-4` radians.

## Result

`ready` is true only when the template status is `ok`, every item status is `ok`, and the immutable intent was stored. Otherwise `ready` is false and `intent_ref`, `intent_fingerprint`, and `expires_at` are omitted. There is no partial intent.

## Deferred

Family discovery, shared or PDF coordinates, a different host, face-based creation, manufacturing a reference plane, routing, circuits, loading families, and generic Revit API execution.
