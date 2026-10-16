# CAP-0011: Preview MEP device placements

- Status: Implemented. Live Revit validation is pending.
- Date: 2026-10-16

## Purpose

Preview one bounded batch of additional wall-hosted MEP devices relative to one existing wall-hosted device. The existing instance is the trusted template for family type, category, wall host, level, and orientation.

This is the preview phase of one controlled write. It creates an ephemeral intent when every placement is eligible. It does not modify the Revit model.

## Operation class

Write preview. `readOnlyHint` is false because preview stores ephemeral intent state. It is not destructive and it does not modify the model.

## Input

`document_id`, `template_element_ref`, `length_unit_id`, and `placements` (1..20). `instance_id` is Server routing only and is not part of the transport-neutral request.

Each placement is a finite signed `offset_along_host` and `vertical_offset` in the caller's length unit. Exact duplicate offset pairs are invalid. After conversion to Revit internal units, `abs(offset_along_host)` must be at most 100 metres and `abs(vertical_offset)` at most 10 metres.

## Template

The template must be a project `FamilyInstance` in a supported MEP device category, with a `LocationPoint`, a straight `Wall` host in the same document, an associated level, and a resolvable `FamilySymbol`. Supported categories are electrical fixtures, lighting devices, lighting fixtures, data devices, communication devices, fire alarm devices, security devices, telephone devices, nurse call devices, electrical equipment, mechanical equipment, plumbing fixtures, and air terminals. Eligibility uses `BuiltInCategory` identity.

V1 placement mode is one-level-based hosted on that same wall. Other placement modes fail closed as `unsupported_placement`.

## Placement

The target is the template location plus the straight wall direction times the converted along-host offset plus global Z times the converted vertical offset. The point must project within the finite wall segment. Occupancy rejects the same symbol on the same host within 1 mm. A zero/zero offset is occupied by the template. If that occupancy scan cannot be completed, preview does not store an intent.

## Result

`ready` is true only when the template status is `ok`, every item status is `ok`, and the immutable intent was stored. Otherwise `ready` is false and `intent_ref`, `intent_fingerprint`, and `expires_at` are omitted. There is no partial intent.

## Deferred

Family discovery, absolute coordinates, a different host, face/ceiling/work-plane/unhosted placement, routing, circuits, loading families, and generic Revit API execution.
