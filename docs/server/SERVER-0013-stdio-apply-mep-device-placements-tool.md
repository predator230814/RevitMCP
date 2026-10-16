# SERVER-0013: Apply MEP device placements

- Status: Implemented. Live Revit validation is pending.
- Date: 2026-10-16

## Tool

`revit_apply_mep_device_placements`

Hints match parameter apply: `readOnlyHint=false`, `destructiveHint=true`, `idempotentHint=false`, `openWorldHint=false`.

Input is the exact `instance_id` and opaque `intent_ref`. Protocol support is `{16}` only. Production `tools/list` contains exactly 14 tools. Live validation is pending and is not a merge pass.

No generic `create_family_instance`, `run_revit_api`, or `run_csharp` tool is exposed.
