# BRIDGE-0015: Request MEP device placement review

- Status: Implemented. Live Revit validation is pending.
- Date: 2026-10-16

## RPC

`revit.request_mep_device_placement_review` on bridge protocol 15.

Support is the explicit set `{15,16}`.

The call presents an existing `mep_device_placements` intent in the existing Revit-local approval pane. It does not approve the intent. An intent of another kind is unavailable.

A host advertises `[15..1]` only when this RPC is present together with the complete v14 prefix, and apply MEP placements is absent. Unknown protocol 17 supports nothing.
