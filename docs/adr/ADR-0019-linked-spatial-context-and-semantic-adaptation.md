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

Amendment, 2026-10-26. Visual observations are the required foundation. Semantic rules are optional best-effort enrichment. One rule is one relationship, not one device. One `paired_symmetric` rule is one pair and deterministically produces two placements. A malformed rule or an unknown anchor is dropped with a bounded warning. It does not discard a structurally valid visual pattern, and an unknown anchor is not reinterpreted. The model result reports `semantic_status` as `complete`, `partial`, or `none`. When no rule survives, Pattern Review says that semantic relationships were not established.

Amendment, 2026-10-26, later the same day. Boundary complexity is measured on the cleaned outer loop, after redundant and collinear Revit vertices are removed. A truncated read stays fail-closed. A cleaned loop with more than 16 meaningful corners stays `boundary_too_complex`. Holes are not counted as outer-loop vertices.

Amendment, 2026-10-27. A semantic rule is valid when the drawing supports it or the current user explicitly states it. An explicit instruction does not need visual confirmation and does not require a source-page entrance point. Optional `basis` is `observed` or `user_stated`. A missing or malformed basis does not reject the rule. Pattern Review labels the section Pattern logic and does not present user-stated intent as something seen on the drawing. Target geometry still decides whether `entrance`, `paired_symmetric`, and `opposite_boundary` resolve. Several doors and no deterministic entrance stay unresolved. The model still does not supply final Revit coordinates.

Amendment, 2026-10-27, later the same day. The matching frame converts boundary, entrance, device, and anchor coordinates into the raster’s Cartesian frame. It keeps the coordinate-free rules and the semantic report unchanged. Pattern Review continues to draw the original page-space pattern. Only deterministic matching uses the converted pattern.

Amendment, 2026-10-27, after the linked-studio live. The live showed that a deterministic adapter correctly refuses to guess which of several doors is the dwelling entrance, and that the model which could make that distinction never saw the target room. The earlier decision that there is no second model call in v1 is withdrawn. The correspondence is no longer the source rules alone.

```text
Source interpretation
→ deterministic spatial discovery
→ bounded target context
→ provider-neutral target reasoning
→ deterministic resolution
→ Pattern Review
→ no mutation
```

The second pass is an internal Assistant capability. It is not a model tool, not a server tool, and not a write. It returns only key-based semantic correspondence. It does not return final coordinates, Revit identities, approval, or a mutation. Deterministic code checks every key against the stored target and calculates the preview. A provider failure stays on that pattern and does not mark the Assistant unavailable. Temporary room, door, edge, and rule keys exist only for that request. Door topology may include a safe other-side room label. It still excludes spatial references, unique ids, element ids, and file paths. The opposite wall is the first boundary reached from the entrance when moving into the room perpendicular to the entrance wall. A pair without a bound edge is centred on that point, and boundary placements are inset along the wall's inward normal. A Revit outline with short wall jogs therefore does not select a jog as the opposite wall.

Amendment, 2026-10-27, PR #94. [ADR-0020](ADR-0020-pattern-specification-v2.md) replaces the rule vocabulary above with pattern specification v2. `paired_symmetric` is removed: a pair is an arrangement, not an anchor. The target-reasoning architecture above is unchanged. Its rule keys become pattern keys, and its edge bindings now apply only to `source_correspondence` items.

Pattern Review shows a deterministic “BIM-IT understood” summary before the overlay. Host and link rooms use the same matching path. A link room can be shown and cannot be selected for prepare. The existing write still requires one boundary loop, so a host room that needed an inner loop can be reviewed and cannot be applied in this slice.

## Consequences

- Protocol 21 is outside every current capability set.
- Linked-room placement is a later decision. The validated wall-hosted `NewFamilyInstance` path does not host on a linked wall.
- The six-anchor vocabulary is not a BIM ontology. A new relationship needs an explicit contract change.
