# BRIDGE-0009: `revit.request_parameter_update_review` capability RPC

- Status: Repository truth for the implemented protocol-v9 path
- Introduced bridge protocol version: 9

## Purpose

Document the live local JSON-RPC operation that asks one exact Revit process to present an existing immutable CAP-0007 intent for local review.

```text
revit.request_parameter_update_review
```

The call starts or reports the presentation request. It does not approve the intent, wait for a human decision, consume approval, or modify the model.

## Protocol

Protocol v9 preserves the complete v8 prefix and adds this method only.

Support is an explicit set:

```text
revit.request_parameter_update_review    {9,10}
```

A host advertises `[9,8,7,6,5,4,3,2,1]` only when request-review is composed with the complete inherited prefix, including preview. Unknown versions are not selected by numeric comparison.

## Contract

Request:

```text
intent_ref
```

Result:

```text
status    started | already_active | busy | unavailable | terminal
```

`instance_id` is routing metadata outside this Bridge request. The request carries no document id, fingerprint, session reference, values, or approval decision.
