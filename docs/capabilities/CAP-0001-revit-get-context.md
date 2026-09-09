# CAP-0001: `revit_get_context`

- Status: Accepted
- Operation class: Read
- Date: 2026-09-08

## Purpose

`revit_get_context` returns the current working context of a RevitMCP-enabled Revit instance so an agent can understand which Revit process, document, view, and selection state it is operating against before performing deeper model queries.

This capability is intentionally small. It is the first end-to-end Revit capability and should validate the architecture from MCP routing through the local bridge and Revit execution context without introducing model-query complexity.

Typical agent use cases:

- establish Revit context at the beginning of a workflow;
- re-check context when the user may have switched documents or views;
- determine whether a document is open before calling document-dependent capabilities;
- understand whether the active document is a project or family document;
- determine whether there is an active selection without returning the selected elements themselves.

## MCP tool

### Name

`revit_get_context`

### Title

`Get Revit Context`

### Description

Return the current application, active document, active view, and selection summary for a Revit instance. Use this when the target Revit context is unknown or may have changed.

### Annotations

- `readOnlyHint: true`
- `openWorldHint: false`

Annotations are descriptive hints only and are not security boundaries.

## MCP input

```json
{
  "instance_id": "optional opaque RevitMCP instance identifier"
}
```

`instance_id` is optional and must be treated as an opaque string. The MCP schema must not require UUID formatting even if the current implementation uses GUID values.

Instance targeting follows ADR-0003:

- zero ready instances and no `instance_id` -> `NO_REVIT_INSTANCE`;
- exactly one ready instance and no `instance_id` -> the server may select it automatically;
- multiple ready instances and no `instance_id` -> `INSTANCE_REQUIRED`;
- explicit unknown `instance_id` -> `INSTANCE_NOT_FOUND`;
- explicit known but unavailable `instance_id` -> `INSTANCE_UNAVAILABLE`.

The MCP-facing `instance_id` is a server routing concern. It is not passed through as a required field of the transport-neutral Revit capability request once the target bridge connection has been selected.

## Transport-neutral request

Conceptually:

```text
GetContextRequest
{
}
```

The request contains no MCP-, transport-, client-, or LLM-specific fields.

## Result contract

Conceptually:

```text
GetContextResult
{
    instance
    document?
    active_view?
    selection
}
```

### Instance

Required fields:

```text
instance_id: string
revit_version: string
revit_build: string
```

`instance_id` is the RevitMCP process-lifetime identity defined by ADR-0003.

### Document

When an active document exists:

```text
title: string
kind: "project" | "family"
is_workshared: boolean
is_model_in_cloud: boolean
is_read_only: boolean
is_modified: boolean
```

When no active document exists, `document` is `null`.

The base capability must not expose local file paths, central-model paths, usernames, cloud project IDs, cloud region, or similar environment metadata merely for context discovery.

### Active view

When an active document and active view exist:

```text
element_id: string
name: string
view_type: string
```

When no active view is available, `active_view` is `null`.

`element_id` is serialized as a string so element identifiers remain lossless and web/JSON friendly. Agent-facing code must treat it as an opaque identifier rather than a number suitable for arithmetic.

### Selection

Always present:

```text
count: integer >= 0
```

This capability returns only the selection count. It must not enumerate selected element IDs or element data.

A separate future capability may expose selection contents when required.

## Example successful result

```json
{
  "instance": {
    "instance_id": "a81e...",
    "revit_version": "2026",
    "revit_build": "26.x"
  },
  "document": {
    "title": "Hospital-MEP",
    "kind": "project",
    "is_workshared": true,
    "is_model_in_cloud": true,
    "is_read_only": false,
    "is_modified": true
  },
  "active_view": {
    "element_id": "184392",
    "name": "Level 02 - HVAC",
    "view_type": "FloorPlan"
  },
  "selection": {
    "count": 12
  }
}
```

## No-document state

An open Revit process with no active document is a valid state and must not be returned as an execution error.

Example:

```json
{
  "instance": {
    "instance_id": "a81e...",
    "revit_version": "2026",
    "revit_build": "26.x"
  },
  "document": null,
  "active_view": null,
  "selection": {
    "count": 0
  }
}
```

## Revit execution behavior

The capability is read-only and requires no Revit transaction.

Execution path:

```text
MCP tool
  -> instance resolution
  -> bridge connection
  -> JSON-RPC request
  -> Revit execution queue
  -> valid Revit API execution context
  -> collect context
  -> structured result
```

The implementation must not read Revit API state from an arbitrary background thread. Revit API access must occur through the project execution dispatcher/queue in a valid Revit API context.

The initial implementation may use the same execution path for context reads that future capabilities use, even if some individual properties might appear readable outside that path. Consistency and correctness take priority over premature optimization.

## Deterministic errors

Initial error codes:

```text
NO_REVIT_INSTANCE
INSTANCE_REQUIRED
INSTANCE_NOT_FOUND
INSTANCE_UNAVAILABLE
REVIT_EXECUTION_TIMEOUT
REVIT_EXECUTION_FAILED
```

No-active-document is not an error.

MCP tool execution failures should be returned as tool results suitable for agent recovery rather than being represented as protocol failures when the MCP protocol itself is functioning correctly.

Errors should contain at least:

```text
code: string
message: string
```

Additional structured details are allowed when they help deterministic recovery, for example candidate instances for `INSTANCE_REQUIRED`.

## Context-size and performance requirements

- Result size must remain bounded and independent of model element count.
- Selection enumeration is explicitly excluded.
- The capability must not enumerate model elements, parameters, linked models, warnings, views, or documents beyond what is required for the current context summary.
- The capability should be suitable for routine use by an agent without materially consuming model context.

No hard latency SLA is established yet because bridge and Revit integration have not been implemented. Performance expectations will be measured during the first real Revit validation.

## Security and data minimization

This capability is `Read` and must not modify the Revit model.

The returned context should contain only information needed for agent orientation. File-system paths and environment/user identifiers are excluded from the base contract.

Future additions that expose more document or cloud metadata require an explicit product need and review rather than silent expansion of this result.

## MCP output

The MCP adapter should provide a strict output schema and return the machine-readable result as structured content.

A concise human-readable text summary may accompany the structured result, but downstream clients must not be required to parse prose to obtain the contract fields.

## Acceptance criteria

CAP-0001 is considered correctly implemented when all of the following are true:

1. The MCP server exposes `revit_get_context` as a read-only tool with optional opaque `instance_id`.
2. With zero ready Revit instances, the call returns `NO_REVIT_INSTANCE`.
3. With one ready Revit instance and no `instance_id`, the call targets that instance deterministically.
4. With multiple ready instances and no `instance_id`, the call returns `INSTANCE_REQUIRED` and enough candidate metadata for the agent to retry explicitly.
5. An explicit unknown instance returns `INSTANCE_NOT_FOUND`.
6. An explicit registered but unreachable instance returns `INSTANCE_UNAVAILABLE` or the equivalent deterministic unavailable state established by the bridge implementation.
7. With an active project document, the result returns the defined instance, document, active-view, and selection-count fields.
8. With an active family document, `document.kind` is `family`.
9. With Revit open and no active document, the call succeeds with `document: null`, `active_view: null`, and selection count `0`.
10. Selected element identifiers are not returned; only the selection count is returned.
11. Element IDs are serialized as strings.
12. Local/central file paths, usernames, and cloud project identifiers are not returned by the base capability.
13. No Revit transaction is started.
14. Revit API access occurs through the accepted Revit execution-context mechanism rather than arbitrary background-thread access.
15. The transport-neutral capability contract contains no MCP-specific or LLM/client-specific types.
16. Automated tests cover instance-routing behavior and pure contract/serialization behavior outside Revit where possible.
17. The add-in variants for Revit 2025, 2026, and 2027 compile successfully.
18. The capability is validated manually in real Revit before being considered complete.

## Explicitly deferred

CAP-0001 does not define:

- element query/filter behavior;
- selected element details;
- explicit document identity/addressing;
- document path or cloud project metadata;
- linked-model context;
- worksharing ownership/user information;
- write behavior;
- UI behavior;
- remote/cloud routing behavior;
- detailed execution-queue scheduling or fairness;
- detailed bridge handshake/version negotiation.

Those concerns are specified separately when needed.

## Related decisions

- ADR-0001: Out-of-process MCP server and Revit capability host
- ADR-0002: Named Pipes + JSON-RPC local bridge
- ADR-0003: Revit instance registration, discovery, and addressing
- ADR-0004: Solution structure and multi-version build
