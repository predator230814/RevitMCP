# ADR-0021: Controlled pattern application against linked architecture

- Status: Proposed
- Date: 2026-10-28
- Product Owner: Dave

## Context

ADR-0019 and ADR-0020 read Rooms from a loaded top-level architecture link, adapt a semantic pattern to each linked Room, and stop at Pattern Review. A linked Room can be shown and cannot be prepared. ADR-0018 already places a pattern into host Rooms through the accepted ADR-0008 write sequence, but its boundary strategies host on a host-document `Wall`. A wall from a linked document cannot be passed to a host-document creation overload.

An MEP model normally links the architecture. The useful next step is to place the same pattern into linked Rooms, in the MEP host document, without a second write architecture.

## Decision

```text
Linked architecture = spatial and hosting truth
MEP host document   = mutation target
```

The linked document stays read-only. BIM-IT never starts a transaction on `RevitLinkInstance.GetLinkDocument()`.

```text
linked Room
→ linked Wall and its Room-facing face
→ Reference.CreateLinkReference(RevitLinkInstance)
→ host-context Reference
→ Document.Create.NewFamilyInstance(Reference, XYZ, XYZ, FamilySymbol) in the host
```

Autodesk API basis: `RevitLinkInstance.GetLinkDocument()`, `RevitLinkInstance.GetTotalTransform()`, `HostObjectUtils.GetSideFaces(...)`, `Reference.CreateLinkReference(RevitLinkInstance)`, `Document.Create.NewFamilyInstance(Reference, XYZ, XYZ, FamilySymbol)`, `FamilyInstance.HostFace`, and `Reference.LinkedElementId`. The face overload requires a `WorkPlaneBased` family.

### Surface

No model-facing or server tool is added. Assistant model tools stay 7. Server `tools/list` stays 18. The existing `revit_preview_room_pattern_placements`, `revit_request_mep_device_placement_review`, and `revit_apply_mep_device_placements` carry the work. The model still cannot call the preview, review, or apply path.

Bridge protocol 21 extends the semantics of `revit.preview_room_pattern_placements`: a `room_ref` may be a linked spatial room reference. Protocol 21 preserves every protocol 20 capability by explicit set membership and adds linked-room preview on `{21}` only. A host advertises 21 only when its preview service declares linked-room support. A complete v20 host without that support still advertises `[20..1]`. Protocol 20 is not redefined. The Server refuses a linked `room_ref` on a connection that did not select 21.

### Identity

`SpatialRoomRefCodec.TryLink` decodes a linked spatial reference into the link instance `UniqueId` and the linked Room `UniqueId` inside BIM-IT only. Those identities, linked Wall identities, face facts, and the transform never reach the main model, the target reasoner, chat, Pattern Review, the WebView, or audit text. The Assistant still sends the opaque spatial reference and never decodes it.

### Resolution

For every linked target, preview re-reads Revit:

1. the active host document;
2. the exact top-level `RevitLinkInstance` by `UniqueId` in the host document (a nested link is never a host-document element);
3. `GetLinkDocument()` is not null;
4. the exact linked Room, placed, with area;
5. the current total link transform, which must be a plan rotation plus translation;
6. the Room boundary with `SpatialElementBoundaryLocation.Finish`.

The largest valid loop is the outer loop. Smaller loops are holes. An outer loop whose next loop is at least 85 % of its area is ambiguous. The existing bounds are kept: the outer loop has 3 to 32 segments, a hole has 3 to 32, and a Room has at most 4 loops. A missing curve is a truncated boundary. Any of these fails closed. A target point is valid only inside the outer loop and outside every hole. Host Rooms keep the ADR-0018 single-loop rule.

### Boundary devices

The Room boundary segment is re-read. v1 requires that the segment is straight, is backed by a linked `Wall` whose location is a line, and lies within 200 mm of the requested point. The Assistant insets wall devices 150 mm into the Room, so the host-room 150 mm tolerance would accept or reject the same point by rounding. Only boundary edges backed by a linked Wall are candidates, so a short column or separation jog next to a device does not make its wall ambiguous. Two nearby wall-backed edges that are not collinear stay `ambiguous_wall`. Another wall is never guessed.

The Room-facing face is proven geometrically, not by the Interior or Exterior name. Every side face from `GetSideFaces(wall, Interior)` and `GetSideFaces(wall, Exterior)` is read in the link and transformed into host coordinates. A candidate must be planar and vertical, its normal must point into the Room within 5°, and its plane must lie within 20 mm of the Room finish segment. Exactly one candidate must remain. The selected face is kept as immutable facts: shell side, host-space plan normal, and host-space plane offset. At apply, the same facts select the same face again, and the linked Reference is converted with `CreateLinkReference(linkInstance)`. A live `Reference` is never stored.

At preview, the requested point is projected onto the plane of that face, and the projected point becomes the immutable target. The requested point is kept in the intent so revalidation re-composes from the same input. Preview and apply then project the target onto the Revit face itself. Projection must succeed inside the face, within 150 mm in plan and 1 mm in height, or the item is `unsupported_face` at preview and the intent is stale at apply. The reference direction is horizontal along the face, `(-ny, nx, 0)` for the inward face normal `n`. A work-plane-based instance takes its X axis from the reference direction and its Z axis from the face normal, so this keeps the family Y axis up and the device upright. A vertical reference direction lays the device on its side. That was observed live in Revit 2026.5. A face-based instance faces the Room through its work-plane normal (`GetTransform().BasisZ`). Its `FacingOrientation` lies in the face. Verification therefore checks the work-plane normal, and `flipFacing` is not used. The host wall-face strategy of ADR-0018 follows the same rule.

A `OneLevelBased` wall device is placed unhosted, as `BoundaryLevel`. It needs the resolved host Level, as an interior device does. Its target is the requested point projected onto the proven face plane in a linked Room, or onto the Room finish edge in a host Room. It is created on the host Level at the mounting height and turned about its insertion point until `FacingOrientation` points into the Room. It records the same wall, link, and face facts as a linked face device, so stale revalidation is unchanged. Verification checks symbol, category, Level, exact location, and facing. It has no `HostFace`, by design.

For every unhosted `OneLevelBased` instance, interior or `BoundaryLevel`, the mounting height is the Revit `Elevation from Level` (`INSTANCE_ELEVATION_PARAM`), the value the modeler reads in Properties. A family can keep its insertion point above its base: a Bollard Light had its insertion point 762 mm above its base in Revit 2026.5, so placing the insertion point at 2400 mm showed 1638 mm. BIM-IT therefore sets the elevation, then moves the instance in plan only. Verification and occupancy read the same mounting point: the Level elevation plus `Elevation from Level` for such an instance, and the location point for any other. A family without a writable elevation keeps the location point.

| Linked boundary type | Result |
| --- | --- |
| `WorkPlaneBased` | `BoundaryLinkedWallFace` |
| `OneLevelBased` | `BoundaryLevel` (host Level required) |
| `OneLevelBasedHosted` | `linked_host_requires_work_plane_based` |
| other | `unsupported_placement` |

### Interior devices

The linked Room Level elevation is transformed into host coordinates with the exact link transform. A host Level is resolved by elevation within 1 mm, never by name. No match is `host_level_not_found`. More than one is `host_level_ambiguous`. A resolved Level reuses the existing `InteriorLevel` and `InteriorLevelWorkPlane` strategies. An `InteriorLevelWorkPlane` instance is hosted by the Level plane: its `LevelId` is invalid, so verification reads the Level from its host, and it cannot be raised above that plane (no writable offset, and a move keeps it on its work plane, observed in Revit 2026.5). Its mounting height must therefore be 0. The Assistant refuses another height before preview, and the composer fails closed with `unsupported_placement`. A raised interior device needs a `OneLevelBased` type. Linked ceiling or floor hosting, ceiling-based families, and family loading are out of scope.

### Strategies

```text
InteriorLevel
InteriorLevelWorkPlane
BoundaryWallHosted
BoundaryWallFace
BoundaryLinkedWallFace
BoundaryLevel
```

Host-room support: `OneLevelBasedHosted` on a host wall, `WorkPlaneBased` on a host wall face, and `OneLevelBased` unhosted on the Room finish edge (`BoundaryLevel`) for boundary items. Interior items use `OneLevelBased` or a `WorkPlaneBased` level plane.

### Intent and fingerprint

`PatternMepPlacementIntent` is extended, not duplicated. A room binding records its source kind, link instance identity, linked Room identity, sanitized link label, a 12-number transform snapshot, outer loop, holes, resolved host Level, and Level elevation. A linked boundary item records the linked Wall identity, link instance identity, shell side, face normal, plane offset, host-space segment, target, and inward direction. `PatternMepPlacementCanonicalEncoder.SchemaVersion` is 2. Every safety-relevant linked fact is in the canonical bytes. The domain stays `pattern_mep_placements`.

### Stale revalidation and apply

Apply reuses `ApplyMepDevicePlacementOrchestrator`, `ControlledApplyAttemptStore`, the approval provider, and `ControlledWriteAuditWriter`. Before the transaction opens, the Addin re-reads every fact above from current Revit, re-composes the intent, re-probes every linked face, and compares the canonical fingerprint. Any difference, including a moved or unloaded link, a removed Room or Wall, a changed boundary or hole, a different host Level mapping, a changed type classification, or a new occupant, is `stale` before mutation. A placement is never recalculated after approval.

One transaction, `BIM-IT Apply Room Pattern`, creates the batch. Before commit, every created instance is checked: symbol, category, exact count, location, height, and facing into the Room. A linked face device also needs a `HostFace` whose element is the expected link instance and whose `LinkedElementId` is the expected linked Wall, and an origin on that face plane within 5 mm. An interior device needs the resolved host Level. Any failure rolls back the whole batch. `committed_unverified` stays limited to post-commit uncertainty.

Occupancy stays a host-document check of existing instances of the selected symbol. The audit event is unchanged.

### Review and approval

Pattern Review lets the user select a resolved linked Room. A wall role in a selected linked Room can use a `work_plane_based` or `one_level_based` option. It cannot use a `one_level_based_hosted` option, in the WebView and authoritatively in the Assistant configuration check. The human still chooses bounded local option keys.

A role card offers at most 8 suggested types, and the first catalog read is bounded to 80 types per category group. A large project library therefore needs a search. Each role card has a type search. The WebView sends the role and the search text over the local IPC. The Assistant calls `revit_get_mep_device_types` with `search` and the role's categories, adds the matching types to the current generation under new local keys `t0`–`t999`, and returns only those keys and their labels. Keys already offered keep their meaning, and a prepared preview stays valid. The model is not involved, and no Bridge or Server contract changes. A search after the pattern was applied is refused.

The drawing interpretation can propose a role that the person does not want, such as a symbol read from the sheet. Each role card therefore also offers `Omit this role`. An omitted role needs no type and no height, and its devices are left out of the preview request, the intent, and the approval. At least one role must still be placed. The omission is a local configuration choice: the Addin and the Server only see the remaining placements, so no Bridge or Server contract changes.

The native Revit Approval Pane remains the only approval authority. For linked placements it adds the linked Room count, the placement kind, and the sanitized link label. It shows no UniqueId, spatial reference, path, or temporary key.

## Consequences

- An MEP host can receive a pattern in linked architecture with one approval, one transaction, and one Undo.
- A linked wall device needs a loaded `WorkPlaneBased` or `OneLevelBased` type. BIM-IT does not load one and does not fall back to a wall-hosted type. A `OneLevelBased` device is not hosted, so it does not follow the wall if the link moves; the intent becomes stale instead.
- Moving, reloading, or editing the link after approval makes the intent stale.
- Live proof is recorded separately and is not established by automated tests.

## References

- [ADR-0008](ADR-0008-controlled-write-safety-model.md) — controlled write safety model
- [ADR-0011](ADR-0011-v1-controlled-write-audit.md) — v1 controlled write audit
- [ADR-0018](ADR-0018-pattern-family-resolution-and-controlled-application.md) — pattern application
- [ADR-0019](ADR-0019-linked-spatial-context-and-semantic-adaptation.md) — linked spatial context
- [ADR-0020](ADR-0020-pattern-specification-v2.md) — pattern specification v2
