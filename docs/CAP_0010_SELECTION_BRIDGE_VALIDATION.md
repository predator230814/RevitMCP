# CAP-0010 typed Bridge validation

Status: live run recorded on 2026-10-15. This is not a merge verdict.

Code SHA: `3f6f57024be0600486f717b71162e08da285d45d`

Host: Revit 2026.5, build `26.5.0.55`. The typed client was `NamedPipeBridgeClient` against the Release Addin built from that SHA. No MCP tool was called. Assistant Inspect Selection was not used.

Model: disposable copy of Autodesk Snowdon Towers Sample Plumbing at `%TEMP%\RevitMCP-CAP0010-Snowdon-Plumbing.rvt`. Active view at open: sheet `P000 - COVER SHEET`. Document id `c17852c3-98dc-4bd1-8f85-31f9f395beec`. No save back to the sample. No model transaction.

## Advertisement

The full production host registered `bridge_protocol_version = 13`. Handshake on that host selected 13 and returned `[13,12,11,10,9,8,7,6,5,4,3,2,1]`.

A second live host without `IRevitGetSelectionService` was not started. The complete-v12-without-selection advertisement `[12,11,10,9,8,7,6,5,4,3,2,1]` remains the automated Bridge result at this SHA.

## Cases

1. Handshake selects 13 on the full host. **Pass.** Observed on the Home process and on the plumbing process.
2. Omitted `document_id` and omitted `limit`. **Pass.** Empty selection returned count 0, no refs, `truncated` false. Two selected pipes returned count 2, two refs, `truncated` false.
3. Present matching `document_id`. **Pass.** `c17852c3-98dc-4bd1-8f85-31f9f395beec`.
4. Present non-matching `document_id`. **Pass.** `""`, `" "`, and `"not-the-active-document"` each returned `DOCUMENT_CONTEXT_CHANGED`.
5. Limits. **Pass.** `limit` 1 and `limit` 100 succeeded. `limit` 0 and `limit` 101 returned `INVALID_SELECTION`.
6. Empty selection. **Pass.** `selected_count` 0, `element_refs` empty, `truncated` false. CAP-0001 `selection.count` was 0.
7. Count before limit, ordinal slice, truncation. **Pass.** Two pipe refs, `limit` 1: `selected_count` 2, one returned ref `6308c3f7-35fd-4393-a0d1-a0c86e94d27f-00150ee0`, `truncated` true. The full pair was ordered `…00150ee0` then `…00150f26`.
8. Unresolved selected id. **Not exercised.** No safe deterministic live selection contained an id the active document could not resolve. Automated tests cover omission.
9. Selected `ElementType`. **Not exercised.** No safe deterministic live selection put an element type in `GetElementIds()`. The service does not apply the CAP-0002 element-type exclusion; that remains a contract and source assertion.
10. Selected `RevitLinkInstance` host. **Pass.** An existing host link was already in the model (`b1b19cba-8df3-40e6-9dbf-7caa44ec46cd-0014c9fd`, class `RevitLinkInstance`). `revit.get_selection` returned that one ref. Linked contents were not expanded. `revit.get_elements` resolved it as category `RVT Links`, name `Snowdon Towers Sample Architectural.rvt : Architecture : location <Not Shared>`.
11. No active UI document. **Pass.** Home process, no open document: `NO_ACTIVE_DOCUMENT`.
12. Protocols 1 through 12 and unknown 14. **Pass.** Negotiated versions 1 through 12 each rejected `revit.get_selection` with `BRIDGE_PROTOCOL_INCOMPATIBLE` before the Addin service ran. A handshake that offered only 14 was rejected with `BRIDGE_PROTOCOL_INCOMPATIBLE`.
13. Capability timeout, no retry, unusable connection, and caller cancellation. **Not exercised.** Inducing a timeout would stall the live Revit API thread. Automated Bridge tests at this SHA cover timeout, the unusable connection, and distinct caller cancellation.
14. CAP-0001 `selection.count` unchanged. **Pass.** Empty selection stayed 0. The two-pipe selection stayed 2. In both exercised cases `selected_count` equalled that count. The contract still allows the two counts to differ when unresolved ids are present; that difference was not exercised live.

## Composability gate

**Pass.** With the two host pipes selected:

`revit.get_selection` returned

- `6308c3f7-35fd-4393-a0d1-a0c86e94d27f-00150ee0`
- `6308c3f7-35fd-4393-a0d1-a0c86e94d27f-00150f26`

Typed `GetElementsAsync` with the same `document_id` resolved both refs. Each item was `status = ok`, category `Pipes`, name `PVC - DWV`, family `Pipe Types`, type `PVC - DWV`.

That is the path: Revit selection, opaque `element_ref`, existing get-elements capability, real selected element.
