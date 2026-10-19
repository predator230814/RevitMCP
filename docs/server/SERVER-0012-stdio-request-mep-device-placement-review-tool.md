# SERVER-0012: Request MEP device placement review

- Status: Implemented. Focused level-based live validation passed on Revit 2026.5 at 7123b11; wall-hosted and level-work-plane creation were not live-proven. See [BIM_IT_MEP_DEVICE_PLACEMENT_WRITE_VALIDATION.md](../BIM_IT_MEP_DEVICE_PLACEMENT_WRITE_VALIDATION.md).
- Date: 2026-10-16

## Tool

`revit_request_mep_device_placement_review`

Hints match the parameter review tool: not read-only, not destructive, not idempotent, and not open-world.

Input is the exact `instance_id` and opaque `intent_ref`. The call returns `started`, `already_active`, `busy`, `terminal`, or `unavailable`. It does not approve the intent and does not modify the model. Protocol support is `{15,16}`.
