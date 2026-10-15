# CAP-0010 MCP validation

Status: live run recorded on 2026-10-15. This is not a merge verdict.

Code SHA: `07091fd3e5a59ff82c6cee4ecfbd9183f4fa8cdb`

Host: Revit 2026.5, build `26.5.0.55`. One process, instance `996b0448-319d-4cfa-8b03-1ba3a61e3c38`, registered `bridge_protocol_version = 13`.

Client: official `ModelContextProtocol` `McpClient` with `StdioClientTransport`, launching the Release `BimIt.Revit.Server.exe` built from that SHA. Each call started a fresh stdio server. Assistant Inspect Selection was not used.

Model: disposable copy of Autodesk Snowdon Towers Sample Plumbing at `%TEMP%\RevitMCP-SERVER0010-Snowdon-Plumbing.rvt`. Active view at open: sheet `P000 - COVER SHEET`. Active document id `592fc556-ad89-464e-84ba-cda30bb6d4fb`. The document was not modified and was not saved. Revit was closed after the run.

## Cases

1. Omitted `instance_id` with one eligible v13 host. **Pass.** Empty selection, the two-pipe selection, and the 26-pipe selection all auto-selected that instance.
2. Omitted `document_id` and omitted `limit`. **Pass.** Twenty-six selected pipes returned `selected_count` 26, `truncated` true, and 25 `element_refs`. Two selected pipes returned `selected_count` 2, `truncated` false, and both refs. CAP-0010 applied the default bound of 25.
3. Document guard. **Pass.** The matching id `592fc556-ad89-464e-84ba-cda30bb6d4fb` succeeded. `""`, `" "`, and `"not-the-active-document"` each returned `DOCUMENT_CONTEXT_CHANGED`.
4. Malformed MCP input. **Pass.** `{"limit":0}` and `{"unexpected":true}` each returned `INVALID_REQUEST` with the shared message `The tool request contains unexpected properties.` Neither returned `INVALID_SELECTION` or `NO_REVIT_INSTANCE`.
5. Two real selected pipes. **Pass.** `revit_get_selection` returned `6308c3f7-35fd-4393-a0d1-a0c86e94d27f-00150ee0` then `6308c3f7-35fd-4393-a0d1-a0c86e94d27f-00150f26`. `revit_get_elements` with the same `document_id` and both refs resolved each item as `status = ok`, category `Pipes`, name `PVC - DWV`, family `Pipe Types`, type `PVC - DWV`.
6. Empty selection. **Pass.** `selected_count` 0, `element_refs` empty, `truncated` false.
7. A v12-only host. **Not exercised.** A second live host without get-selection was not started. Server routing tests at this SHA already treat v12 as ineligible for this tool and eligible for the earlier tools.
8. No active UI document. **Pass.** After the plumbing document was closed and no document remained, `revit_get_selection` returned `NO_ACTIVE_DOCUMENT`.

## tools/list

**Pass.** Exactly 11 tools, ordinal order:

```text
revit_apply_parameter_updates
revit_describe_parameters
revit_get_context
revit_get_elements
revit_get_mep_topology
revit_get_parameter_values
revit_get_selection
revit_get_warnings
revit_preview_parameter_updates
revit_query_elements
revit_request_parameter_update_review
```

`revit_get_selection` title is `Get Revit Selection`. `readOnlyHint` is true. `openWorldHint` is false. No Bridge RPC name is exposed.
