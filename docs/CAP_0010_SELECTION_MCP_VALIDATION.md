# CAP-0010 MCP validation

Status: pending. This checklist is not a pass.

Scope: official MCP client to `BimIt.Revit.Server` stdio to Bridge protocol 13 to Revit 2026.5. Assistant Inspect Selection is out of scope.

## Before the run

- Use the Server and Addin built from the SERVER-0010 head.
- Confirm `tools/list` returns exactly 11 tools, including `revit_get_selection`.
- Confirm the tool title is `Get Revit Selection`, `readOnlyHint` is true, and `openWorldHint` is false.
- Confirm no Bridge RPC name is exposed.
- The Revit host advertises protocol 13.

## Cases

1. Omitted `instance_id` with one eligible v13 host succeeds.
2. Omitted `document_id` and omitted `limit` return the CAP-0010 default bound of 25.
3. A matching `document_id` succeeds. An empty or whitespace `document_id` that is not the active id returns `DOCUMENT_CONTEXT_CHANGED`.
4. `limit` 0 and an unexpected property return `INVALID_REQUEST` before discovery. They do not return `INVALID_SELECTION` or `NO_REVIT_INSTANCE`.
5. Two real selected elements, ideally pipes, return opaque `element_refs`. `revit_get_elements` with the same `document_id` and one returned ref resolves `status = ok`.
6. Empty selection is success: `selected_count` 0, `element_refs` empty, `truncated` false.
7. A v12-only host is not eligible for this tool. Existing tools on that host stay eligible.
8. No active UI document returns `NO_ACTIVE_DOCUMENT`.

Record the Revit build, the commit SHA, and each case result in this file only after the run.
