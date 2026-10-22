# ADR-0017: Pattern intelligence and spatial grounding

- Status: Accepted
- Date: 2026-10-21
- Product Owner: Dave

## Context

A user can attach a sketch, PDF, or image of one room and ask BIM-IT to find rooms of the same type and show how that pattern would be applied. The useful split is semantic interpretation versus geometric truth.

An unmerged pull request, PR #90, drafted an ADR-0017 for manual two-point document-to-Revit calibration. That draft is not accepted. This ADR replaces it. Global page-to-project calibration is not required for semantic pattern transfer. Manual point calibration remains a possible later fallback, not the normal workflow.

## Decision

```text
LLM = semantic interpretation
Pattern Engine = deterministic matching and adaptation
Revit = geometric source of truth
Controlled Write = mutation authority
WebView2 = human review surface
```

The model may describe what it sees on one attached page. It does not receive Revit identities, and it does not decide which rooms match or what the match score is.

The Pattern Engine builds a room-local shape, independent of global project X/Y. It compares that shape with Revit room boundaries, including rotation and an explicit mirror. It projects detected devices into room-local coordinates for review. It does not mutate Revit.

The model reports positions in normalized image coordinates. `x` and `page_x` run left to right. `y` and `page_y` run top to bottom. Those values are what the WebView overlays on the attached page. Before matching or projection, the Assistant converts each source point with the trusted raster size of that visual page: `x = page_x * raster_width` and `y = (1 - page_y) * raster_height`. The conversion preserves page aspect and turns image Y-down into Cartesian Y-up. It does not reflect the room merely because the source is an image. The model does not supply width, height, or aspect ratio. Revit candidate geometry stays in metres.

Revit room geometry comes from a bounded read, `revit_get_room_layouts`. Coordinates leave Revit in metres. Boundary loops keep enough topology for matching and stay capped. Doors and windows that touch the room are bounded features. Linked documents are not traversed in this slice.

A future pattern source may be `assistant_document`, `linked_cad`, `placed_pdf_image`, or `revit_exemplar`. This slice implements only `assistant_document`.

The local Assistant WebView shows the interpreted page, the candidate rooms, and a simple projection. JavaScript has no filesystem path, no hidden Revit identity, and no write or approval authority.

Pattern application is a later controlled write. It is not part of this decision's implementation.

## Consequences

- Bridge protocol 17 adds `revit.get_room_layouts` and preserves every earlier capability by explicit set membership.
- Server `tools/list` gains one read-only tool and contains 15 tools.
- The Assistant model surface gains `bimit_propose_room_pattern` and contains 7 tools. The three raw MEP write tools stay hidden.
- One stored pattern is session-local and untrusted. Replacing or removing its attachment, New Chat, and shutdown clear it.
- Ordinary single-loop, mostly straight-edged rooms are the v1 matching case. Unsupported geometry is rejected or scored low.
- No new write capability, approval change, arbitrary Revit API, shell, HTTP transport, or MCP App is introduced.
