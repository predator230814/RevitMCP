# CAP-0018: Typed parameter predicates on `revit_query_elements`

- Status: Implemented. Live Revit validation is pending.
- Operation class: Read
- Date: 2026-11-23
- Extends: [CAP-0002](CAP-0002-revit-query-elements.md)
- Bridge: [BRIDGE-0021](../bridge/BRIDGE-0021-query-element-predicates.md), protocol 25
- Related: [CAP-0005](CAP-0005-revit-get-parameter-values.md), [ADR-0023](../adr/ADR-0023-declarative-revit-read-query-architecture.md)

## Purpose

This slice extends the existing `revit_query_elements` tool. It does not replace CAP-0002 and it does not add a tool or an RPC.

Coarse filters still narrow candidates by category, family, type, level, and name. Predicates then apply typed parameter conditions to the candidates that survive those filters. Every predicate must match.

A request that omits `predicates` follows the CAP-0002 path exactly. That path does not use the predicate candidate budget.

## Request

`predicates` is optional. Omission means there are no predicates. An explicit empty array is an invalid query.

A predicate has:

| Field | Rule |
| --- | --- |
| `source` | `"parameter"` only in this slice |
| `parameter_ref` | The existing opaque parameter identity from CAP-0004 / CAP-0005 |
| `operator` | One of the operators below |
| `value` | Present only when the operator requires a typed value |

At most 8 predicates. All predicates are ANDed.

Parameter refs for both instance and type sources use the same resolution rule as `revit_get_parameter_values`. The identity is resolved before any candidate is scanned. An unknown `parameter_ref` is an invalid query, not a per-element non-match.

### Operators

Existence and emptiness operators take no value:

- `is_missing`
- `is_present`
- `is_empty`
- `is_not_empty`

Comparison operators:

| Value kind | Operators |
| --- | --- |
| `string` | `equals`, `not_equals`, `contains` |
| `integer` | `equals`, `not_equals`, `greater_than`, `greater_or_equal`, `less_than`, `less_or_equal` |
| `quantity` (`value` + `unit_type_id`) | numeric ordering only |

String comparison is ordinal and case-sensitive. It does not trim. That matches the stored parameter string. It is distinct from CAP-0002 name matching, which stays ordinal and ignore-case.

### Missing and empty

Missing is not empty.

- `is_missing`: the resolved instance or type parameter is not present on the candidate.
- `is_present`: it is present, whether or not it has a value.
- `is_empty`: it is present and either has no value, or its string value is empty.
- `is_not_empty`: it is present and is not empty.

A missing parameter does not match `equals`, `not_equals`, `contains`, or a numeric comparison. `0` and `false` are not empty. Strings are not trimmed to decide emptiness.

### Scope

Document scope is valid when there is at least one coarse filter or at least one predicate. Active-view behavior is unchanged: an empty coarse-filter object remains valid, and predicates are optional.

## Execution

For a request that contains predicates:

1. Establish scope.
2. Enumerate non-`ElementType` elements.
3. Apply the existing CAP-0002 coarse filters exactly as today.
4. Count and store the elements that pass those filters.
5. Enforce `MaxPredicateCandidates` after coarse filtering. The v1 constant is 10,000. It is isolated so live validation can tune it.
6. If the candidate set exceeds that bound, fail with `QUERY_CANDIDATE_LIMIT`. Return no partial result and do not evaluate predicates.
7. Only after the candidate set is within the budget, evaluate predicates.
8. Sort matching element refs ordinally and apply the existing result limit.

A request with no predicates uses the existing CAP-0002 matching path and does not consult `MaxPredicateCandidates`.

`QueryElementCandidate` does not gain parameter fields. Predicate preparation, the resolved parameter predicate, the instance/type surface resolver, and the typed evaluator stay inside the Addin query pipeline.

## Quantity comparisons

This slice does not infer a unit from a double and does not implement quantity equality or tolerance.

Before conversion, all of the following must hold:

- the parameter storage is a double;
- `parameter.Definition.GetDataType()` yields a non-empty measurable spec;
- the requested unit token is a unit;
- that unit is valid for the parameter spec.

Only then is the requested value converted to internal units and compared with the parameter's double value. An incompatible spec/unit pair, a non-finite requested value, or a non-finite converted value is an invalid query. A double that is not a measurable spec is an invalid query when a quantity comparison reaches it. A parameter that is missing, has no value, or is not stored as a double does not match the comparison and does not attempt conversion.

## Result and errors

The result shape is unchanged: `context`, `matched_count`, `truncated`, `element_refs`.

There is no partial success. Malformed predicates, invalid operator/value combinations, unknown parameter refs, incompatible quantity units or specs, non-finite quantities, and invalid bounds are `INVALID_QUERY`. Candidate-budget exhaustion is `QUERY_CANDIDATE_LIMIT`, which is distinct from a zero-match result. Unexpected Revit failures remain execution failures.

The schemas stay closed. They do not expose Revit API types, numeric element ids, paths, or implementation details.

## Bridge

Predicate-aware `revit.query_elements` requires bridge protocol 25. Predicate-free queries remain valid on the existing CAP-0002 protocol set, including protocol 25. Predicates are not sent to a host that negotiated an older protocol. See BRIDGE-0021.

## Explicitly deferred

- API-property predicates
- quantity equality and tolerance
- OR trees, joins, regex, and expression languages
- geometry and topology predicates
- generated code, writes, and transactions
- Assistant proposal logic and the Proposal Registry
- live Revit validation

## Acceptance

- Omitted predicates preserve CAP-0002 matching, ordering, limits, and the absence of the candidate budget.
- Missing, present, and empty are distinct, and a missing parameter does not satisfy `not_equals`.
- Instance and type parameter refs use the CAP-0005 source rule.
- String, integer, and quantity comparisons follow the applicability table.
- Incompatible and non-finite quantities fail before a partial result is returned.
- Multiple predicates are ANDed, with a maximum of 8.
- Document scope may use predicates without a coarse filter.
- The candidate budget is counted after coarse filtering and overflow returns no result.
- Protocol 25 is required for predicates, and older negotiated protocols still execute predicate-free queries.
- The result still contains only opaque element refs and the existing summary fields.
