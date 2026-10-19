# SERVER-0011: Preview MEP device placements

- Status: Implemented. Focused level-based live validation passed on Revit 2026.5 at 7123b11; wall-hosted and level-work-plane creation were not live-proven. See [BIM_IT_MEP_DEVICE_PLACEMENT_WRITE_VALIDATION.md](../BIM_IT_MEP_DEVICE_PLACEMENT_WRITE_VALIDATION.md).
- Date: 2026-10-16

## Tool

`revit_preview_mep_device_placements`

Hints: `readOnlyHint=false`, `destructiveHint=false`, `idempotentHint=false`, `openWorldHint=false`.

The tool routes like the parameter preview: optional `instance_id`, required active-document guard, and bridge protocol `{14,15,16}`. The strict input requires `offset_x`, `offset_y`, and `vertical_offset`. `offset_along_host` is rejected. Malformed shape, range, duplicate offset triples, and non-finite numbers are `INVALID_REQUEST` before discovery. Semantic template and placement problems stay in the success-shaped preview statuses. The template output may include the closed `placement_mode`.

The Bridge RPC name is not an MCP tool name.
