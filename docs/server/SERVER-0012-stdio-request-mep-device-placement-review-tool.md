# SERVER-0012: Request MEP device placement review

- Status: Implemented. Live Revit validation is pending.
- Date: 2026-10-16

## Tool

`revit_request_mep_device_placement_review`

Hints match the parameter review tool: not read-only, not destructive, not idempotent, and not open-world.

Input is the exact `instance_id` and opaque `intent_ref`. The call returns `started`, `already_active`, `busy`, `terminal`, or `unavailable`. It does not approve the intent and does not modify the model. Protocol support is `{15,16}`.
