# BRIDGE-0015: Request MEP device placement review

- Status: Implemented. Focused level-based live validation passed on Revit 2026.5 at 7123b11; wall-hosted and level-work-plane creation were not live-proven. See [BIM_IT_MEP_DEVICE_PLACEMENT_WRITE_VALIDATION.md](../BIM_IT_MEP_DEVICE_PLACEMENT_WRITE_VALIDATION.md).
- Date: 2026-10-16

## RPC

`revit.request_mep_device_placement_review` on bridge protocol 15.

Support is the explicit set `{15,16}`.

The call presents an existing `mep_device_placements` intent in the existing Revit-local approval pane. The pane names the placement as level based, wall hosted, or level work plane, and shows the project-axis offsets in the caller's length unit. It does not approve the intent. An intent of another kind is unavailable.

A host advertises `[15..1]` only when this RPC is present together with the complete v14 prefix, and apply MEP placements is absent. Unknown protocol 17 supports nothing.
