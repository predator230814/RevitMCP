# BRIDGE-0016: Apply MEP device placements

- Status: Implemented. Focused level-based live validation passed on Revit 2026.5 at 7123b11; wall-hosted and level-work-plane creation were not live-proven. See [BIM_IT_MEP_DEVICE_PLACEMENT_WRITE_VALIDATION.md](../BIM_IT_MEP_DEVICE_PLACEMENT_WRITE_VALIDATION.md).
- Date: 2026-10-16

## RPC

`revit.apply_mep_device_placements` on bridge protocol 16.

Support is the explicit set `{16}`.

Current protocol version is 16. A full production host advertises `[16,15,14,13,12,11,10,9,8,7,6,5,4,3,2,1]` only when apply is present together with the complete v15 prefix.

Unknown protocol 17 supports nothing. Numeric `version >= N` checks are not used.
