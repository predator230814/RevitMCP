# BRIDGE-0010: `revit.apply_parameter_updates` capability RPC

- Status: Implemented. Official MCP-client live functional validation on Revit 2026.5 build `26.5.0.55` at SHA `517bca8ddb8ecf3bab11cb14738ef8e0c9937353` is **PASS**. `tools/list` was exactly 9 and the selected protocol was 10. Induced Revit warning/error failure-dialog behavior was not executed and remains deferred validation debt.
- Introduced bridge protocol version: 10

## Purpose

Document the local JSON-RPC operation that implements Accepted CAP-0008.

```text
revit.apply_parameter_updates
```

Protocol v10 adds exactly this method and preserves the complete v9 prefix. It applies one previously previewed and locally approved immutable batch. It accepts no new values and no approval decision.

## Protocol

Current version is 10. Support is explicit, never numeric `>=`.

```text
get_context                      {2,3,4,5,6,7,8,9,10}
query_elements                   {3,4,5,6,7,8,9,10}
get_elements                     {4,5,6,7,8,9,10}
describe_parameters              {5,6,7,8,9,10}
get_parameter_values             {6,7,8,9,10}
get_mep_topology                 {7,8,9,10}
preview_parameter_updates        {8,9,10}
request_parameter_update_review  {9,10}
apply_parameter_updates          {10}
```

Full supported prefix:

```text
[10,9,8,7,6,5,4,3,2,1]
```

Unknown v11 supports nothing until a later specification says otherwise.

A host may advertise v10 only when it has the complete inherited v9 surface plus a functional apply service. If apply exists without request-review, the host does not advertise v10. Lower prefixes remain available.

## Contract

Request:

```text
intent_ref
```

No `instance_id` enters the Bridge request.

Result:

```text
status
```

Closed values:

```text
applied
approval_required
unavailable
in_progress
stale
transaction_failed
committed_unverified
indeterminate
audit_failed
```

The result has no fingerprint, session, value, document, approval, or audit field.

Caller timeout may skip a queued operation that has not started. After EXEC-0001 begins the apply, cancellation does not abort the Revit mutation. The Addin owns the single transaction named `RevitMCP Apply Parameter Updates`.
