# ADR-0019: Linked spatial context and semantic pattern adaptation

- Status: Accepted
- Date: 2026-10-25
- Product Owner: Dave

## Context

ADR-0018 can place a pattern into host rooms whose outline matches the sketch. Rooms in an architectural Revit link were invisible, and a room with an inner boundary loop was rejected before any comparison. Exact outline similarity is also the wrong definition of “the same kind of room.” A switch belongs near that room’s entrance, and a centre light belongs at that room’s centre, even when the outlines differ.

This slice is read and review only. It does not add a write, and it does not place devices into a linked room.

## Decision

```text
sketch/PDF/image
→ multimodal semantic interpretation
→ host + linked Revit spatial context
→ semantic + geometric adaptation
→ Pattern Review
→ no mutation
```

`revit_read_spatial_rooms` is one read. Bridge protocol 20. A full host advertises protocol 20. A host that can preview a pattern but cannot read spatial rooms still advertises protocol 19. Server `tools/list` is 18. The model still has seven tools. The new read is an internal Assistant read, not a model tool and not a trusted write.

The read returns host Rooms and Rooms from loaded top-level `RevitLinkInstance`s. Nested links are not traversed. `GetLinkDocument()` returning null is an explicit unavailable link, with no rooms from it. Linked geometry is read-only and transformed into host plan coordinates with the link’s total transform. The result may include a sanitized link display label. It never includes a file path.

Room identity on this read is an opaque BIM-IT spatial reference. It is not an ADR-0006 host `element_ref`. A host reference can be resolved back to the host `UniqueId` only inside BIM-IT, so the existing pattern write can still target a host room. A link reference cannot. The model result and Pattern Review reject spatial refs, unique ids, and `.rvt` paths.

A room with several boundary loops is not rejected for that reason alone. The largest loop is the outer loop used for shape comparison. Smaller loops are holes. Two loops of nearly equal area, no measurable outer loop, or a boundary that exceeds the existing complexity bounds still fail closed.

`bimit_propose_room_pattern` keeps the raw page points and may also return rules. The v1 anchors are only `room_center`, `entrance`, `nearest_boundary`, `opposite_boundary`, `corner`, and `paired_symmetric`. A rule names a role, an anchor, an optional offset, an optional pair group, and optional confidence. It cannot carry page coordinates or a Revit identity. Raw points remain untrusted evidence.

There is no second model call and no temporary candidate-key tool in v1. The rules are the correspondence. Deterministic code resolves each rule onto each candidate room and validates the point. Geometry score remains evidence. When every rule resolves and the shape score would have rejected the room, the room stays reviewable as a semantic match. An unresolved rule stays unresolved. The model does not supply final Revit coordinates.

Pattern Review shows a deterministic “BIM-IT understood” summary before the overlay. Host and link rooms use the same matching path. A link room can be shown and cannot be selected for prepare. The existing write still requires one boundary loop, so a host room that needed an inner loop can be reviewed and cannot be applied in this slice.

## Consequences

- Protocol 21 is outside every current capability set.
- Linked-room placement is a later decision. The validated wall-hosted `NewFamilyInstance` path does not host on a linked wall.
- The six-anchor vocabulary is not a BIM ontology. A new relationship needs an explicit contract change.
