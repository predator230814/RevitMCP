# SERVER-0009: stdio `revit_apply_parameter_updates`

- Status: Implemented. Official MCP-client live functional validation on Revit 2026.5 build `26.5.0.55` at SHA `517bca8ddb8ecf3bab11cb14738ef8e0c9937353` is **PASS**. `tools/list` was exactly 9 and the selected protocol was 10. Induced Revit warning/error failure-dialog behavior was not executed and remains deferred validation debt.
- Bridge protocol: `{10}` only

## Tool

```text
revit_apply_parameter_updates
```

Title: Apply Revit Parameter Updates

The tool applies one previously previewed and locally approved immutable parameter-update batch in the exact addressed Revit process. It accepts no new values and no approval decision.

## Input

Closed object. Both properties are required.

```text
instance_id
intent_ref
```

The input does not include `document_id`, fingerprint, `session_ref`, values, confirm, approval, or client, model, or provider identity.

Routing is exact. There is no implicit instance, no single-instance fallback, and no retry against another Revit process. A v9 host is not eligible.

## Output

Closed object:

```text
status
```

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

## Annotations

```text
readOnlyHint: false
destructiveHint: true
idempotentHint: false
openWorldHint: false
```

These are MCP hints only. They are not authorization.

After this implementation, `tools/list` is exactly 9 tools. That count was live-validated on Revit 2026.5 build `26.5.0.55` at SHA `517bca8ddb8ecf3bab11cb14738ef8e0c9937353`.
