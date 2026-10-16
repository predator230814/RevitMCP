# BRIDGE-0014: Preview MEP device placements

- Status: Implemented. Live Revit validation is pending.
- Date: 2026-10-16

## RPC

`revit.preview_mep_device_placements` on bridge protocol 14.

Support is the explicit set `{14,15,16}`.

A host advertises `[14,13,12,11,10,9,8,7,6,5,4,3,2,1]` only when preview MEP placements is present together with the complete v13 prefix. A complete v13 host without this RPC still advertises `[13..1]`.

Unknown protocol 17 supports nothing. Numeric `version >= N` checks are not used.

Existing capabilities remain supported on 14, 15, and 16 by explicit membership in each `Supports` method.
