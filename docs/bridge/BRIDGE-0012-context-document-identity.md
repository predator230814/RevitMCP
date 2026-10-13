# BRIDGE-0012: Context document identity and active-view query

- Status: Implemented in the BIM-IT Assistant shell slice. Live Revit validation is not yet recorded.
- Date: 2026-10-13
- Related: [BRIDGE-0001](BRIDGE-0001-handshake-and-version-negotiation.md), [BRIDGE-0002](BRIDGE-0002-get-context-capability-rpc.md), [BRIDGE-0003](BRIDGE-0003-query-elements-capability-rpc.md), [BRIDGE-0011](BRIDGE-0011-get-warnings-capability-rpc.md), [CAP-0001](../capabilities/CAP-0001-revit-get-context.md), [CAP-0002](../capabilities/CAP-0002-revit-query-elements.md), [ADR-0006](../adr/ADR-0006-document-and-element-reference-identity.md)

## Purpose

Introduce bridge protocol version 12. v12 preserves every v11 capability guarantee and adds two accepted contract amendments:

- CAP-0001: when an active document exists, `document` includes the opaque `document_id` owned by ADR-0006.
- CAP-0002: `scope=active_view` accepts `filters: {}`. `scope=document` still requires at least one filter.

v11 is not redefined. A connection that negotiates protocol 11 does not receive `document_id` and still rejects an empty filter object for every scope, including `active_view`.

## Compatibility rule

v12 preserves the full v11 capability prefix by explicit membership, not by `version >= N`.

| Capability | Supported versions |
| --- | --- |
| Handshake | 1 through 12 |
| `revit.get_context` | 2 through 12 |
| `revit.query_elements` | 3 through 12 |
| `revit.get_elements` | 4 through 12 |
| `revit.describe_parameters` | 5 through 12 |
| `revit.get_parameter_values` | 6 through 12 |
| `revit.get_mep_topology` | 7 through 12 |
| `revit.preview_parameter_updates` | 8 through 12 |
| `revit.request_parameter_update_review` | 9 through 12 |
| `revit.apply_parameter_updates` | 10, 11, 12 |
| `revit.get_warnings` | 11, 12 |
| CAP-0001 `document.document_id` | 12 |
| CAP-0002 empty filters for `active_view` only | 12 |

A full host advertises:

```text
SupportedVersions = [12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1]
CurrentVersion = 12
```

That advertisement exists only when get-warnings is composed with the complete v10 prefix. Partial prefixes stay on their historical lists and do not advertise 12. Examples:

- apply without get-warnings: `[10, 9, 8, 7, 6, 5, 4, 3, 2, 1]`
- request-review without apply: `[9, 8, 7, 6, 5, 4, 3, 2, 1]`
- preview without request-review: `[8, 7, 6, 5, 4, 3, 2, 1]`

Unknown protocol version 13 supports nothing until a later specification lists it.

## What v12 does not change

- No new JSON-RPC method.
- No renamed tool or RPC.
- No model write.
- No new identity system. `document_id` comes from the existing open-document identity service. It is not derived from path, title, username, or cloud metadata.
- A missing active document still returns `document: null`.
- An unfiltered whole-document query remains invalid.
- Active-view queries stay bounded by `limit` and stay read-only.

## Wire behavior

When the selected protocol supports the v12 document-identity guarantee, an active document object includes `document_id`. When the selected protocol does not, the Bridge omits `document_id` before the result leaves the connection. The field is not set to an empty string and is not invented by the client.

When the selected protocol does not support an unfiltered active-view query, an empty `filters` object is rejected with `INVALID_QUERY` before the query service runs. A v12 connection accepts that object only for `active_view`.
