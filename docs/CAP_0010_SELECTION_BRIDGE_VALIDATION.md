# CAP-0010 typed Bridge validation

Status: pending. This checklist is not a pass.

Scope: live `revit.get_selection` on Bridge protocol 13 inside Revit 2026.5. The MCP tool `revit_get_selection` is out of scope. Assistant Inspect Selection is out of scope.

## Before the run

- Use a full production Addin build that includes `IRevitGetSelectionService`.
- Confirm the registration advertises protocol 13 at the head of `[13,12,11,10,9,8,7,6,5,4,3,2,1]`.
- Confirm a host built without get-selection still advertises `[12,11,10,9,8,7,6,5,4,3,2,1]` and does not advertise 13.
- Call the typed Bridge client. Do not call an MCP tool.

## Cases

1. Handshake selects 13 on the full host.
2. Active project document, omitted `document_id`, omitted `limit`: success, `selected_count` is the unique resolved count, `element_refs` length is at most 25, `truncated` is true only when the count is greater than the returned length.
3. Present `document_id` equal to the active document's opaque id: success.
4. Present `document_id` that does not match, including a raw empty or whitespace string that is not the active id: `DOCUMENT_CONTEXT_CHANGED`.
5. `limit` 1 and `limit` 100: success. `limit` 0 and `limit` 101: `INVALID_SELECTION` before Revit execution.
6. Empty selection: `selected_count` 0, `element_refs` empty, `truncated` false.
7. More than the limit unique resolved refs: count is the full unique count, refs are the first ordinal slice, `truncated` is true. Order is ordinal and does not follow click order.
8. A selected element that does not resolve in the active document is omitted. It does not add a field and it does not fail the call.
9. A selected `ElementType`, when Revit puts one in `GetElementIds()`, is returned as that element's opaque ref. The call does not apply the CAP-0002 element-type exclusion.
10. A selected `RevitLinkInstance` host element, when present in `GetElementIds()`, is returned as that host element's opaque ref. Linked contents are not expanded.
11. No active UI document: `NO_ACTIVE_DOCUMENT`.
12. Protocols 1 through 12 and unknown 14 reject `revit.get_selection` before the Addin service runs.
13. A capability timeout returns `REVIT_EXECUTION_TIMEOUT`, does not retry, and leaves that connection unusable. Caller cancellation stays cancellation.
14. CAP-0001 `selection.count` is unchanged by this call. The two counts may differ.

Record the Revit build, the commit SHA, and each case result in this file only after the run. Do not mark the checklist passed from unit tests.
