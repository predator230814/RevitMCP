# ADR-0018: Pattern family resolution and controlled application

- Status: Accepted
- Date: 2026-10-22
- Product Owner: Dave

## Context

ADR-0017 lets the model interpret one attached room sketch and lets deterministic code match Revit rooms. It does not place devices. The useful next step is to resolve a loaded Revit family type, let the user choose the target rooms and mounting height, preview the placements, and apply them through the existing Revit-local approval.

The user should not have to know JSON schemas, coordinate contracts, Revit ids, family ids, or MCP tool names.

## Decision

```text
LLM = visual/semantic interpretation
Pattern Engine = deterministic room matching and projection
Type Resolver = deterministic loaded-project family/type resolution
WebView2 = human configuration and review
Revit = geometric and type truth
Approval Pane = write authority
Controlled Write = mutation
```

Company and project standards are deferred. The Assistant owns one resolver seam, `IPatternTypeStandardSource`, so a future standard can precede the heuristic without changing the MCP or write architecture. The precedence that seam is shaped for is:

```text
project/company standard
→ explicit local mapping
→ deterministic loaded-project heuristic
```

Only the explicit local mapping and the loaded-project heuristic exist now. This decision does not add a standards database, family library, legend database, or family loading.

Every page point on `bimit_propose_room_pattern` is `{"page_x","page_y"}`. Coordinates stay normalized image coordinates: X left to right, Y top to bottom. The trusted raster conversion from ADR-0017 stays inside the Assistant. A validation failure returned to the model includes a compact reason so the model can correct the same turn.

`revit_get_mep_device_types` is a read-only catalog of loaded `FamilySymbol` types for the supported MEP-device categories. Bridge protocol 18. Category filters use stable BIM-IT tokens backed by `BuiltInCategory`. Results carry an opaque `family_type_ref`, the canonical category token, display names, placement classification, and whether the symbol is active. The result has no numeric ElementId and no path. The model never supplies `family_type_ref`.

The Assistant resolver ranks loaded types from the pattern role, an optional untrusted `family_hint`, and that catalog. A unique strong match may be preselected. Ambiguity stays explicit. A hint influences ranking and grants no authority. An unresolved role blocks preview. The model does not decide match confidence for Revit types.

The pattern store keeps one hidden immutable grounding snapshot for the current generation: instance and document binding, attachment and page identity, the source pattern, candidate room refs, transforms, projected positions, match score and band, rotation and mirror, and the generation identity. Revit refs do not enter model, chat, or WebView output. Replacing or removing the attachment, a new pattern, New Chat, or shutdown clears that state.

When the user names a room type, such as every studio, `bimit_propose_room_pattern` accepts an optional `room_name`. BIM-IT then reads rooms whose names contain that word and adapts the pattern into them. A named room that can receive the pattern stays selectable even when its shape score is low. A room that cannot receive the pattern stays rejected. One batch is still at most 8 rooms. Omitting `room_name` keeps the shape match. The model still cannot send a Revit identity.

Pattern Review shows match bands, target-room selection, one card per pattern role, loaded type choices, placement classification, and a mounting height in millimetres. Height is local configuration. High-confidence rooms may be preselected. Rejected rooms cannot be selected. A role without a type and a height cannot be prepared. Changing the selection, type, or height invalidates an existing preview. The WebView receives only the generation id, room indexes, and opaque option keys.

`revit_preview_room_pattern_placements` is bridge protocol 19. The Assistant calls it through a narrow internal client. The model cannot call it. There is no new review or apply MCP tool. `revit_request_mep_device_placement_review` and `revit_apply_mep_device_placements` dispatch on the MEP payload stored under the existing `ControlledWriteIntentKind.MepDevicePlacements`.

The pattern payload is a sibling of `MepDevicePlacementIntent`. It has its own canonical fingerprint domain, `pattern_mep_placements`. The single-template MEP intent and its preview, review, and apply behavior stay unchanged. Server `tools/list` is 17. Model tools stay 7.

Preview re-reads Revit. Interior placement supports `OneLevelBased` and a level work plane. Boundary placement supports one straight wall from the current room boundary, either `OneLevelBasedHosted` or `WorkPlaneBased` on a deterministically resolved room-facing face. Ambiguous, curved, or unsupported hosts fail closed. Ceiling-hosted placement, floor-hosted placement, family loading, new hosts, circuits, parameters, and generic Revit API execution are out of scope.

Apply runs the accepted approval, stale, occupancy, and audit sequence, then creates the batch in one Revit transaction. Inactive symbols are activated inside that transaction. The transaction regenerates, then checks every created instance before commit. Interior points must match within 1 mm. A wall-face origin may sit within 150 mm in plan of its target, with height within 1 mm. A wall-hosted origin is accepted on the closest chosen wall. The gap between the sketch point and the wall is ignored, because matching rooms are not identical. The position along that wall stays within 150 mm of the calculated pattern point, and the configured height stays within 1 mm. After regeneration, a hosted device that faces out of the room is flipped back toward the room before the check. Ceiling hosting is unchanged and remains out of scope. Any failed check rolls the batch back. `committed_unverified` remains only when a transaction has already committed and can no longer be rolled back. Repeated apply returns the stored result and creates nothing. One Revit Undo removes a committed batch.

The audit event remains the ADR-0011 schema. ADR-0018 widens the numeric range of the existing `item_count` field from 1..20 to 1..80 so a pattern batch can be audited. No audit field is added. The log still excludes room names, family and type names, coordinates, refs, prompts, and paths.

## Consequences

- Bridge protocols 18 and 19 preserve every earlier capability by explicit set membership.
- A pattern generation that reaches a terminal apply result cannot create another preview.
- Chat text cannot select hidden Revit identities, approve, or apply.
- Live Revit proof is recorded separately and is not established by automated tests.
