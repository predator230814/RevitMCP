# BRIDGE-0020: Read-operation registry

- Status: Proposed. Not implemented.
- Date: 2026-11-11
- Capability: [CAP-0016](../capabilities/CAP-0016-revit-read-operations.md)
- Architecture: Accepted [ADR-0024](../adr/ADR-0024-registry-driven-compiled-revit-read-operations.md)

## RPC

Two methods on bridge protocol 24:

- `revit.lookup_read_operations`
- `revit.read_operation`

Support for both is the explicit set `{24}`. A later protocol version supports them only when a later bridge specification adds that version to the predicate. Numeric `version >= N` checks are not used.

Protocol 24 preserves every capability from protocol 23 by explicit membership, including `revit.query_parameter_bindings`, `revit.query_family_types`, and `revit.preview_room_pattern_placements` with linked-room references. Individual registered operations do not consume a protocol version. Their presence is discovered through lookup.

`BridgeProtocol.CurrentVersion` becomes 24 only for a host that advertises 24. `ReadOperationsVersion` is 24. `SupportsReadOperations` is true only for 24. Every existing `Supports…` predicate that is true for 23 becomes true for 24 by explicit membership, in the same style as `SupportsQueryParameterBindings`.

A full production host advertises `[24,23,22,21,20,19,18,17,16,15,14,13,12,11,10,9,8,7,6,5,4,3,2,1]` only when the read-operation service is composed and its registry passed startup validation, together with the complete v23 prefix. A complete v23 host without that service still advertises `[23..1]`. A read-operation service without the v23 prefix does not advertise 24. Unknown protocol 25 supports nothing.

A connection below 24 is refused with `BRIDGE_PROTOCOL_INCOMPATIBLE` before either method runs. There is no fallback to `revit.query_family_types`, `revit.query_parameter_bindings`, or any other RPC.

## Startup

The composition root builds the registry from one explicit list. The production list is exactly `project.user_worksets` contract version 1. The registry is validated at construction, as CAP-0016 requires.

If validation throws, the host catches that failure, does not pass the read-operation service to `NamedPipeBridgeHost`, and does not advertise protocol 24. Addin startup continues. Protocol 23 and earlier capabilities stay composed. The client-visible failure for a later lookup or read is `BRIDGE_PROTOCOL_INCOMPATIBLE`, not a host crash and not a partial registry.

## Request and result

Contracts in `BimIt.Revit.Contracts`:

- `LookupReadOperationsRequest` and `LookupReadOperationsResult`
- `ReadOperationRequest` and `ReadOperationResult`

The Bridge methods do not accept `instance_id`. JSON property names are snake_case. `inputs` and `result` are JSON elements. The operation's typed request and result stay in the Addin. The Server does not receive a per-operation CLR type.

Lookup fields are `operation`, `contract_version`, `query`, `offset`, and `limit`, all optional. Read fields are `document_id`, `operation`, `contract_version`, `inputs`, `page_size`, and `cursor`, with the first four required.

The results are the CAP-0016 envelopes. `next_offset` and `next_cursor` are present and null when the CAP says they are null. Exact lookup includes `input_schema` and `output_schema`. Search lookup omits both.

`JsonSchemaSubset` lives in Contracts. The Addin uses it for definition validation at startup and for instance validation of `inputs` and of the serialized operation result.

## Execution

`revit.lookup_read_operations` reads the immutable registry on the calling thread. It does not call `EnqueueAsync` and does not touch the Revit API. It still applies `ReadResultBudget` to the serialized lookup result.

`revit.read_operation` performs the Revit-free CAP-0016 checks first. Only a request that passes them is handed to one `EnqueueAsync` work item. Document kind, the document guard, cursor revision, and the workset read happen at the start of that work item, in that order. The operation receives the active `Document` and the existing identity, revision, and cursor services. It does not receive `UIApplication`.

The cursor codec is the same per-process `DeclarativeQueryCursorCodec` that CAP-0014 and CAP-0015 use. The source string is the operation id. No new value is added to a central source list. Body version stays 2.

The workset read opens no transaction. When the active document is not workshared it returns the CAP-0016 empty success and does not construct `FilteredWorksetCollector`. A family document throws `UNSUPPORTED_DOCUMENT_KIND` before that read.

An unexpected failure is `REVIT_EXECUTION_FAILED` with a compact message. The adapter maps `BridgeException` codes unchanged, in the same way as `revit.query_parameter_bindings`.

## Errors

The RPC pair uses only the CAP-0016 codes. A null service or a negotiated protocol other than 24 is `BRIDGE_PROTOCOL_INCOMPATIBLE` with a compact message that names the method. It does not include a stack trace, a path, or a Revit exception.

## Acceptance

- A v24 host with a valid registry serves both methods and still serves `revit.query_parameter_bindings`.
- A v23 host advertises `[23..1]`, serves the protocol 23 methods, and both new methods return `BRIDGE_PROTOCOL_INCOMPATIBLE` without calling the registry.
- A registry validation failure produces the same advertisement and the same protocol error as a missing service, and the Addin remains started.
- Lookup with no active document succeeds.
- A read does not enqueue when operation resolution, paging compatibility, input schema, semantic input validation, or cursor integrity fails.
