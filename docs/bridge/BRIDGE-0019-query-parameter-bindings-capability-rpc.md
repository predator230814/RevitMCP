# BRIDGE-0019: Query parameter bindings

- Status: Implemented in the Addin and Bridge, and exposed through the Server as the MCP tool `revit_query_parameter_bindings`. Live Bridge-to-Addin-to-Revit validation passed on Revit 2026.5 (`26.5.0.55`) at `393b12f`; see [CAP_0015_PARAMETER_BINDINGS_BRIDGE_VALIDATION.md](../CAP_0015_PARAMETER_BINDINGS_BRIDGE_VALIDATION.md).
- Date: 2026-11-05
- Capability: [CAP-0015](../capabilities/CAP-0015-revit-query-parameter-bindings.md)
- Architecture: [ADR-0023](../adr/ADR-0023-declarative-revit-read-query-architecture.md)

## RPC

`revit.query_parameter_bindings` on bridge protocol 23.

Support is the explicit set `{23}`.

Protocol 23 preserves every capability from protocol 22 by explicit membership, including `revit.query_family_types` and `revit.preview_room_pattern_placements` with linked-room references. It adds parameter-binding inventory only on `{23}`.

Current protocol version is 23. A full production host advertises `[23,22,21,20,19,18,17,16,15,14,13,12,11,10,9,8,7,6,5,4,3,2,1]` only when the parameter-binding service is composed with the complete v22 prefix. A complete v22 host without the parameter-binding service still advertises `[22..1]`. A parameter-binding service without that prefix does not advertise 23.

Unknown protocol 24 supports nothing. Numeric `version >= N` checks are not used. A connection below 23 is refused with `BRIDGE_PROTOCOL_INCOMPATIBLE`, and the service is not invoked. There is no fallback to `revit.describe_parameters`, `revit.get_parameter_values`, or `revit.query_family_types`.

## Request and result

The request and result are the CAP-0015 contracts in `BimIt.Revit.Contracts` (`QueryParameterBindingsRequest`, `QueryParameterBindingsResult`). The Bridge method does not accept `instance_id`. `next_cursor` is present and null on the final page.

## Execution

The Addin validates the request shape, then reads through `EnqueueAsync` (EXEC-0001). Inside that work item it requires an active project document, applies the `document_id` guard, reads the document query revision, and prepares the query: defined category tokens, then cursor integrity, source, instance, document and fingerprint, then revision. Only then does it traverse `Document.ParameterBindings` completely with its forward iterator. An invalid or stale query does not traverse the map.

Identity, data type, and `parameter_ref` reuse the CAP-0004 helpers and the per-process `OpenDocumentParameterIdentityService`. The cursor codec and the document query revision are the same per-process instances that CAP-0014 uses; the cursor is tagged with the `parameter_bindings` source, so a family-type cursor is `INVALID_CURSOR` here and the reverse.

It does not open a transaction, traverse links, read a shared-parameter file, read parameter values, or keep an inventory snapshot. An unexpected failure is reported as `REVIT_EXECUTION_FAILED` with a compact message.

Document query revisions advance from `DocumentChanged`, `DocumentSynchronizedWithCentral`, and `DocumentReloadedLatest` on documents this process has already identified. The revision is per document, so a family-type change also invalidates an outstanding binding cursor. Those subscriptions do not by themselves prove that Revit raises each event for every binding change. The live validation showed it for one manual binding change (a category added to a project parameter): the old cursor returned `STALE_CURSOR`, and the restart succeeded with a different cursor.

## Server and MCP exposure

The Server application service, the MCP schemas, and the `revit_query_parameter_bindings` tool consume this RPC without changing it. Without `instance_id`, the Server selects only a Ready host whose negotiated protocol supports `{23}`. With an explicit `instance_id`, the handshake decides: a Ready v22 host returns `BRIDGE_PROTOCOL_INCOMPATIBLE`, and no fallback capability is called. Production `tools/list` is 20.

The typed Bridge-to-Addin-to-Revit live validation passed and is recorded in [CAP_0015_PARAMETER_BINDINGS_BRIDGE_VALIDATION.md](../CAP_0015_PARAMETER_BINDINGS_BRIDGE_VALIDATION.md). That record was made before the tool existed, when `tools/list` was 19. The CAP-0015 natural-agent gate, through an isolated official MCP client, has not been run.
