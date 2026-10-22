# BRIDGE-0017: Get room layouts

- Status: Implemented. Live validation is a checklist and has not been executed.
- Date: 2026-10-21

## RPC

`revit.get_room_layouts` on bridge protocol 17.

Support is the explicit set `{17}`.

Protocol 17 preserves every capability from protocol 16 by explicit membership, including `revit.apply_mep_device_placements`. It adds room layouts only on `{17}`.

Current protocol version is 17. A full production host advertises `[17,16,15,14,13,12,11,10,9,8,7,6,5,4,3,2,1]` only when get-room-layouts is composed with the complete v16 prefix. A complete v16 host without get-room-layouts still advertises `[16..1]`. Room layouts without that prefix do not advertise 17.

Unknown protocol 18 supports nothing. Numeric `version >= N` checks are not used.

## Request and result

The request is the CAP-0013 document guard and room references. The result is the CAP-0013 metre layout. The Bridge method does not accept `instance_id`.

## Execution

The Addin validates the request, then reads through `EnqueueAsync`. It does not open a transaction and does not open a linked document.
