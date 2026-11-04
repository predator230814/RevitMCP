# BRIDGE-0018: Query family types

- Status: Implemented. Live validation is a checklist and has not been executed.
- Date: 2026-11-03
- Capability: [CAP-0014](../capabilities/CAP-0014-revit-query-family-types.md)
- Architecture: [ADR-0023](../adr/ADR-0023-declarative-revit-read-query-architecture.md)

## RPC

`revit.query_family_types` on bridge protocol 22.

Support is the explicit set `{22}`.

Protocol 22 preserves every capability from protocol 21 by explicit membership, including `revit.preview_room_pattern_placements` with linked-room references. It adds family-type inventory only on `{22}`.

Current protocol version is 22. A full production host advertises `[22,21,20,19,18,17,16,15,14,13,12,11,10,9,8,7,6,5,4,3,2,1]` only when query-family-types is composed with the complete v21 prefix. A complete v21 host without query-family-types still advertises `[21..1]`. Family-type inventory without that prefix does not advertise 22.

Unknown protocol 23 supports nothing. Numeric `version >= N` checks are not used. A v21 connection does not fall back to `revit.get_mep_device_types` or `revit.query_elements`.

## Request and result

The request and result are the CAP-0014 contracts in `BimIt.Revit.Contracts`. The Bridge method does not accept `instance_id`. `next_cursor` is present and null on the final page.

## Execution

The Addin validates the request shape, then reads through `EnqueueAsync`. It collects `FamilySymbol` elements once and host-document instances once. It does not open a transaction, traverse links, read parameters, or keep an inventory snapshot.

Document query revisions used for stale cursors advance from `DocumentChanged`, `DocumentSynchronizedWithCentral`, and `DocumentReloadedLatest` on documents this process has already identified. Those subscriptions do not by themselves prove that Revit raises each event for every inventory change.
