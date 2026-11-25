# BRIDGE-0021: Query-element predicates

- Status: Implemented. Live Revit validation is **PASS** on `e66bb76b702cf89aa963ffdbab7ec4d47d0597fc`. Record: [CAP_0018_QUERY_ELEMENT_PREDICATES_VALIDATION.md](../CAP_0018_QUERY_ELEMENT_PREDICATES_VALIDATION.md).
- Date: 2026-11-23
- Capability: [CAP-0018](../capabilities/CAP-0018-revit-query-element-predicates.md)
- Extends: [BRIDGE-0003](BRIDGE-0003-query-elements-capability-rpc.md) `revit.query_elements`

## RPC

No new JSON-RPC method. Protocol 25 adds the optional `predicates` array to the existing `revit.query_elements` request.

Predicate support is the explicit set `{25}`. `SupportsQueryElementPredicates` is true only for 25. Numeric `version >= N` checks are not used.

`revit.query_elements` itself stays on its existing explicit set, extended with 25. A predicate-free request is valid on every version that already supported CAP-0002, including 3 through 25. A request whose `predicates` field is present requires protocol 25. Omission is not the same as an empty array. An empty array is `INVALID_QUERY` and is not treated as a legacy query.

Protocol 25 preserves every protocol-24 capability by explicit membership, including `revit.lookup_read_operations`, `revit.read_operation`, `revit.query_parameter_bindings`, and `revit.query_family_types`. At the time of this specification, unknown protocol 26 supported nothing. [CAP-0020](../capabilities/CAP-0020-bounded-generated-csharp-read.md) later specifies protocol 26. Until that RPC is implemented, a host still does not advertise 26. This v25 record is otherwise unchanged.

## Advertisement

A full host advertises `[25,24,23,22,21,20,19,18,17,16,15,14,13,12,11,10,9,8,7,6,5,4,3,2,1]` only when the complete v24 prefix is present, including the read-operation service and the query service. There is no separate predicate service. A host that is missing read operations still advertises its earlier prefix, normally `[23..1]`, and does not advertise 24 or 25.

A client that negotiates a version below 25 can still call `revit.query_elements` without predicates. If `predicates` is present, the client refuses with `BRIDGE_PROTOCOL_INCOMPATIBLE` before the RPC is sent. The host adapter refuses the same request before the query service runs. The refusal is not converted into a zero-match result.

## Request and result

The request gains optional `predicates`. Each item is a closed object:

- `source`: `"parameter"`
- `parameter_ref`: opaque parameter identity
- `operator`: one of `is_missing`, `is_present`, `is_empty`, `is_not_empty`, `equals`, `not_equals`, `contains`, `greater_than`, `greater_or_equal`, `less_than`, `less_or_equal`
- `value`: omitted for existence and emptiness; otherwise `kind` is `string`, `integer`, or `quantity`

At most 8 predicates. The result is unchanged: `context`, `matched_count`, `truncated`, `element_refs`.

Candidate-budget exhaustion is `QUERY_CANDIDATE_LIMIT`. It is a capability error, distinct from `matched_count = 0`. Invalid predicate combinations, unknown parameter refs, and incompatible quantity units are `INVALID_QUERY`. No partial result is returned.

## What protocol 25 does not change

- No new MCP tool.
- No renamed tool or RPC.
- No model write and no transaction.
- Predicate-free document and active-view filter rules.
- Result ordering, the 1–100 limit, and opaque element refs.
