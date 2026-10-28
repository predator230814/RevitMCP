# ADR-0020: Pattern specification v2

- Status: Accepted
- Date: 2026-10-27
- Product Owner: Dave

## Context

ADR-0019 introduced six semantic anchors. One of them, `paired_symmetric`, mixed five separate questions in one word. It named what the devices are, where they sit (on a wall or inside the room), what they relate to, how many there are, and how they are arranged. The linked-studio live showed the cost. A model that saw two ceiling luminaires and two bedside receptacles had only one way to say "pair", so both became wall pairs. Independent receptacle pairs on different source walls collapsed onto the one wall facing the entrance. A room where one relationship failed was demoted as a whole, and the chat answer was written before target reasoning had run.

ADR-0019's target-reasoning architecture is sound and stays. This ADR changes the vocabulary that architecture carries.

## Decision

A pair is an arrangement, not an anchor.

Each pattern item separates five concepts.

| Concept | Values |
| --- | --- |
| Role | `receptacle`, `switch`, `luminaire`, `smoke_detector`, `thermostat`, `data`, `other` |
| Placement | `boundary` or `interior` |
| Anchor | `room_center`, `entrance`, `nearest_boundary`, `opposite_boundary`, `corner`, `source_correspondence` |
| Count | 1 to 4 |
| Arrangement | `single`, `symmetric_pair`, `distributed`, `cluster` |

Optional fields are `offset_m`, `group`, `basis`, `confidence`, and `device_indexes`. `paired_symmetric` is removed. It is an unknown anchor, not a synonym.

Placement is semantic intent. It is not a Revit `FamilyPlacementType`. `room_center` is the only interior anchor. Every other anchor is a boundary anchor. A mismatch is dropped with `incompatible_placement`.

Cardinality is the intended count. It is independent of how many devices were observed. A count outside 1 to 4, or a count that contradicts the arrangement, is dropped with `invalid_count`. A missing arrangement follows the count. `single` and `symmetric_pair` are implemented. `distributed` and `cluster` are recognised and dropped with `unsupported_arrangement`. They are deferred, not reinterpreted.

`bimit_propose_room_pattern` keeps its name and its raw page points. It keeps at most eight items. Each item is still coordinate-free. The tool assigns observed devices to items, using `device_indexes` first and the nearest same-role devices otherwise. For each item it keeps bounded source evidence: the device indexes, the normalized centroid, the spread, the nearest source edge, the relative position along that edge, the drawing side of that edge, and the observed placement. Evidence never carries a Revit identity.

`source_correspondence` means "on the wall of this room that plays the same role as the source wall". It never falls back to the wall facing the entrance. It resolves only when target reasoning binds the item to a target edge. The provider-neutral `IPatternTargetReasoner` request now carries the source outline, its edges with drawing sides, the source entrance edge, and each item with its evidence. Target edges carry plan compass sides. The reply binds temporary pattern keys to temporary target edge keys. Deterministic code rejects an unknown edge, a binding for an interior item, a binding for any anchor other than `source_correspondence`, and conflicting bindings. Those items fail closed. Stale generations are ignored. Temporary keys never reach Pattern Review.

Deterministic adaptation resolves each item independently.

- The room centre is the area centroid of the outer loop. When that point is outside the room or inside a hole, the Revit room location point is used instead. The Revit location point is where the room was placed, not its centre.
- A single interior `room_center` item is placed at the room centre.
- An interior pair is two points symmetric about the room centre. The axis follows the source orientation when the geometric match is trusted, and otherwise the room's principal direction. Spacing follows the source spread within 0.6 m to 5 m. Both points must be inside the outer loop, outside every hole, and at least 0.3 m from every wall and hole edge, or the item is unresolved. It is never moved to a wall.
- A boundary pair with `source_correspondence` is centred on its bound edge, uses the source spacing along the edge, and is inset into the room. Items bound to the same edge share it in equal slots.
- `entrance`, `opposite_boundary`, `nearest_boundary`, and `corner` keep the ADR-0019 behaviour. A `corner` pair is unsupported.

Each candidate room gets an adaptation status that is separate from the geometry band.

| Adaptation status | Meaning |
| --- | --- |
| `resolved` | Every item resolved |
| `partial` | At least one item resolved and at least one did not |
| `unresolved` | No item resolved |

The band remains geometric: `high`, `lower`, or `rejected`. Adaptation does not promote or demote it. A partial room stays visible in Pattern Review with its resolved devices and a per-item reason for each unresolved one. Only a room with no adaptation, or a resolved one, can be selected for prepare.

Pattern Review wording comes from the item's role and arrangement, for example "2 receptacles · symmetric pair · boundary" or "1 data device · opposite entrance". Room reasoning lists `Resolved` items with ✓ and `Unresolved` items with !.

When target reasoning runs in a turn, the Assistant's chat answer is replaced with deterministic text that reflects the result, for example: "BIM-IT found 6 candidate studio rooms. Target adaptation completed for 5 and is partial for 1. Review the Pattern Review panel for the proposed layouts. No Revit changes were made." There is no third model pass.

The model still has seven tools. Server `tools/list` is still 18. Bridge protocol is still 20. This slice adds no write, no linked-wall placement, no family loading, no Pattern Apply, no approval, no preview capability, and no MCP tool. It ends at Pattern Review.

## Consequences

- A sketch with ceiling luminaires and wall receptacles can now be described truthfully. Independent pairs on different walls stay independent.
- `source_correspondence` depends on the target-reasoning pass. When that pass is unavailable, those items are unresolved and the room is partial. This is intended. The alternative was a guess.
- Older stored proposals and model habits that emit `paired_symmetric` lose that item with `unknown_anchor`. There is no compatibility shim.
- `distributed` and `cluster` need their own geometry and validation before they can be enabled.
- The role, placement, anchor, count, and arrangement vocabulary is still not a BIM ontology. A new value needs an explicit contract change.

## References

- [ADR-0019](ADR-0019-linked-spatial-context-and-semantic-adaptation.md) — linked spatial context, semantic adaptation, and target reasoning
- [ADR-0018](ADR-0018-pattern-family-resolution-and-controlled-application.md) — pattern application
- [ADR-0017](ADR-0017-pattern-intelligence-and-spatial-grounding.md) — pattern intelligence and spatial grounding
