# SERVER-0008: stdio `revit_request_parameter_update_review`

- Status: Repository truth for the implemented MCP tool
- Bridge protocol: `{9,10}`

## Tool

```text
revit_request_parameter_update_review
```

Title: Request Revit Parameter Update Review

The tool asks the exact target Revit process to present an existing immutable parameter-update intent. It returns after the presentation request is started or fails. It does not approve the intent, wait for a human decision, consume approval, or modify the model.

## Input

Closed object. Both properties are required.

```text
instance_id
intent_ref
```

There is no implicit instance and no fallback to another Revit process.

## Output

Closed object:

```text
status    started | already_active | busy | unavailable | terminal
```

Annotations: `readOnlyHint` false, `destructiveHint` false, `idempotentHint` false, `openWorldHint` false. These hints are not authorization.
