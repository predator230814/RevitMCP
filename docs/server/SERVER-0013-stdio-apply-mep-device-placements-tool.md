# SERVER-0013: Apply MEP device placements

- Status: Implemented. Focused level-based live validation passed on Revit 2026.5 at 7123b11; wall-hosted and level-work-plane creation were not live-proven. See [BIM_IT_MEP_DEVICE_PLACEMENT_WRITE_VALIDATION.md](../BIM_IT_MEP_DEVICE_PLACEMENT_WRITE_VALIDATION.md).
- Date: 2026-10-16

## Tool

`revit_apply_mep_device_placements`

Hints match parameter apply: `readOnlyHint=false`, `destructiveHint=true`, `idempotentHint=false`, `openWorldHint=false`.

Input is the exact `instance_id` and opaque `intent_ref`. Protocol support at this specification was `{16}`. [SERVER-0014](SERVER-0014-stdio-get-room-layouts-tool.md) later preserves eligibility on protocol 17 and makes production `tools/list` 15.

No generic `create_family_instance`, `run_revit_api`, or `run_csharp` tool is exposed.
