# ADR-0013: BIM-IT Assistant local architecture and trusted-boundary separation

- Status: Accepted
- Date: 2026-10-13
- Product Owner: Dave

## Context

BIM-IT Assistant is the conversational product that can eventually use BIM-IT Revit MCP and, later, other specialized services. The first slice needs a Revit entry point and a local shell. It does not add an LLM provider, skills, tool-permission policy, or persistent chat history.

ADR-0007 keeps orchestration optional. ADR-0008 and ADR-0010 keep controlled writes on the trusted approval path. ADR-0011 keeps the audit barrier. This ADR places the Assistant outside those authorities.

## Decision drivers

- One Ribbon entry point inside Revit, not a ribbon of MCP operations.
- No Assistant process, WebView, MCP server, LLM, or network use during ordinary Revit startup.
- Dogfood the real MCP client, stdio server, Bridge, Addin, and EXEC-0001 path.
- Bind every Assistant workflow to the exact Revit `instance_id`.
- Keep external MCP clients able to use the server without the Assistant.
- Keep the Assistant and its WebView unable to satisfy CAP-0008.

## Options considered

### Option A: In-process Assistant inside the Addin, calling Bridge services directly

Advantages:

- fewer processes;
- direct access to Revit context.

Disadvantages:

- a second capability implementation beside the MCP server;
- an Assistant failure could be tangled with the capability host;
- external clients would not exercise the same path.

### Option B: Separate local Assistant process behind a dedicated IPC boundary

```text
Revit Ribbon
  -> BIM-IT Assistant DockablePane
    -> WPF host
      -> local WebView2
        -> bounded native messages
          -> Addin host
            -> dedicated local IPC
              -> BimIt.Revit.Assistant
                -> MCP client
                  -> BimIt.Revit.Server stdio
                    -> existing Bridge
                      -> Addin / EXEC-0001
```

Advantages:

- the capability host survives an Assistant failure;
- the Assistant uses the real MCP surface;
- the WebView never sees the Bridge pipe, API keys, or approval authority;
- external clients remain independent.

Disadvantages:

- another process and IPC contract to operate;
- startup of that process must stay lazy.

## Decision

Option B is accepted.

The Assistant is one local process per Revit process, started only when the user opens BIM-IT Assistant, and bound to that process's `instance_id`. There is no system-wide Assistant daemon in this decision.

IPC is a current-user Named Pipe with an explicit message contract. It is not reflection RPC and it is not TCP. The WebView does not connect to that pipe. The WebView origin is a dedicated local virtual host and is not the approval origin.

The trusted Approval DockablePane stays separate. No Assistant message creates an approval, consumes an approval, bypasses preview, bypasses stale checks, bypasses audit, or executes an arbitrary Revit mutation.

The first product workflows call existing read tools only: `revit_get_context`, `revit_get_warnings`, and `revit_query_elements`. This decision does not add Revit capabilities.

These are future extensions, not part of the accepted v1 implementation:

- custom or shipped skills
- per-Assistant tool permission policy
- ChatGPT, Copilot, Claude, or local LLM adapters
- persistent conversation history
- visual grounding
- show, select, zoom, or isolate
- Revit API lookup or arbitrary C# or Python execution
- MCP Apps and HTTP or remote MCP

Conceptual separation to preserve:

- Skill: guidance or knowledge
- Tool permission: what this Assistant is willing to request
- Capability: what BIM-IT Revit MCP exposes
- Approval: trusted human authorization for an exact write intent
- Host enforcement: the final security truth

## Consequences

- Revit startup registers the Ribbon button and the DockablePane. It does not create the Assistant WebView or start the Assistant or MCP server.
- Assistant process health is not a prerequisite for the Bridge, approval, or external MCP clients.
- v1 chat state is in memory for the current Revit session only.
- AI provider status is "Not configured" until a later adapter exists.
- CAP-0001 does not return `document_id`. CAP-0009 requires it, and CAP-0002 rejects an empty `filters` object. The v1 Assistant calls those tools as they exist and reports the contract result. Changing those contracts is follow-up work, not part of this ADR.
