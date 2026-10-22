# SERVER-0014: Get room layouts

- Status: Implemented. Live validation is a checklist and has not been executed.
- Date: 2026-10-21

## Tool

`revit_get_room_layouts`

Hints: `readOnlyHint=true`, `destructiveHint=false`, `openWorldHint=false`.

## Routing

`instance_id` is optional and routes to one discovered Revit instance. Eligibility is `BridgeProtocol.SupportsGetRoomLayouts`, which is protocol 17 only. A selected protocol of 16 or 18 is not eligible. The advertised version list is not the eligibility test.

The Server removes `instance_id` before the Bridge call and forwards `document_id` plus `element_refs`.

## Catalog

Production `tools/list` contains exactly 15 tools. This tool is the fifteenth. No write tool is added.

Accepted capability errors are `NO_ACTIVE_DOCUMENT`, `DOCUMENT_CONTEXT_CHANGED`, `INVALID_ROOM_LAYOUT`, `REVIT_EXECUTION_TIMEOUT`, and `REVIT_EXECUTION_FAILED`.
