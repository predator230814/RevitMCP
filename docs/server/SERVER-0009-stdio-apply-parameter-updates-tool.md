# SERVER-0009: stdio `revit_apply_parameter_updates`

- Status: Implemented in the Draft PR. Live Revit write validation is pending. Do not merge until that gate passes.
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

After this implementation, `tools/list` is exactly 9 tools. Live validation of that count is still pending.
