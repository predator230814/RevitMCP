# ADR-0014: Provider-neutral Assistant agent runtime and initial GitHub Copilot adapter

- Status: Accepted
- Date: 2026-10-13
- Product Owner: Dave

## Context

[ADR-0013](ADR-0013-bim-it-assistant-local-architecture.md) placed BIM-IT Assistant outside the Revit capability host and outside the trusted approval authority. The first shell calls three existing read workflows through the official MCP client. It does not answer natural language.

ADR-0007 keeps orchestration optional and the Revit capability service specialized. ADR-0008, ADR-0009, and ADR-0010 keep controlled writes on the trusted approval path. ADR-0012 keeps BIM-IT Assistant as the conversational product, separate from BIM-IT Revit MCP.

This decision adds the first conversational model inside the Assistant process. It does not add a second Revit capability path, a write-enabled agent, skills, persistent history, or a product OAuth UI.

## Decision drivers

- BIM-IT owns the conversation, the exact Revit instance binding, the MCP client, tool exposure, capability policy, and the approval boundary.
- A model provider must be replaceable without changing Revit capability contracts.
- The first provider is the official GitHub Copilot SDK, using the logged-in user already present on the machine.
- The provider sees only three BIM-IT read workflows. It does not see write tools, a shell, or the Bridge.
- A provider failure must not take down Revit, the Bridge, the MCP server, approval, or external MCP clients.

## Options considered

### Option A: Let GitHub Copilot connect directly to BimIt.Revit.Server

The Copilot session would be an MCP client of the existing stdio server.

Advantages:

- fewer adapter lines;
- the model would see the server's own tool schemas.

Disadvantages:

- the provider would own tool exposure and could see write tools;
- instance binding and approval policy would sit in provider configuration;
- a second MCP client would duplicate the validated Assistant path.

### Option B: Provider-neutral runtime inside BimIt.Revit.Assistant, with one Copilot adapter

BIM-IT keeps the MCP client. The Copilot session receives three custom read tools that call that client. Another provider can implement the same runtime contract later.

Advantages:

- Revit contracts stay provider-neutral;
- the validated path stays Revit, Assistant pane, Addin, Assistant IPC, Assistant, MCP client, Server, Bridge, EXEC-0001;
- write tools and approval stay unreachable from the model;
- Copilot authentication and model selection stay inside the adapter.

Disadvantages:

- the Assistant process gains a provider runtime and its child process;
- natural-language answers depend on that provider being signed in.

## Decision

Option B is accepted.

BIM-IT owns:

- conversation and product UX;
- exact Revit instance binding;
- the MCP client;
- BIM-IT tool exposure;
- capability policy;
- the approval boundary;
- future skills;
- the provider-independent agent contract.

Provider adapters own:

- connection to a model or agent runtime;
- provider authentication integration;
- model discovery;
- conversation turn execution.

The initial adapter is the official `GitHub.Copilot.SDK` stable package. At implementation, that version is 1.0.21. Only `BimIt.Revit.Assistant` references it.

The Copilot client runs in `CopilotClientMode.Empty` with an isolated per-process base directory, session store disabled, memory disabled, skills disabled, and no Copilot-hosted MCP servers. Its allow-list is exactly three custom tools: `bimit_get_model_context`, `bimit_get_warnings`, and `bimit_get_active_view_elements`. Those tools call the existing Assistant MCP session. They inject the bound `instance_id`. The model cannot supply `instance_id` or `document_id`.

GitHub Copilot is not the Revit capability layer, the MCP server, the approval authority, the Bridge, or the owner of Revit instance selection.

The runtime contract is provider-neutral: initialize, send one turn, reset the conversation, and dispose. Provider status is `not_configured`, `starting`, `ready`, or `unavailable`. SDK types do not cross Assistant IPC or enter the Revit add-in.

v1 authentication uses the SDK's logged-in-user path. There is no GitHub OAuth product UI, no stored BIM-IT token, and no token in IPC, the WebView, MCP arguments, or logs. v1 model selection is `auto`. There is no model picker and no token streaming. New Chat resets the provider session as well as the in-memory timeline. Chat history is not persisted.

Later adapters such as OpenAI, Anthropic, or a local model can implement the same runtime contract without changing Revit capability contracts.

## Consequences

- Natural language is answered from the Assistant process. Slash commands and the three quick actions stay on the deterministic workflow path.
- MCP readiness and provider readiness are independent. Quick actions still run when the provider is unavailable.
- The Assistant still cannot approve a write. CAP-0008 and the Approval pane GUID are unchanged.
- Empty-mode Copilot state lives under `%LOCALAPPDATA%\BIM-IT\Assistant\CopilotRuntime\` for one Assistant process and is deleted on the way out. It is not BIM-IT chat history.
- Skills, persistent history, a general tool-permission UI, write tools from chat, streaming, and MCP Apps remain unimplemented.

## References

- [ADR-0007](ADR-0007-federated-mcp-boundaries-and-orchestration.md)
- [ADR-0008](ADR-0008-controlled-write-safety-model.md)
- [ADR-0009](ADR-0009-agent-human-interaction-and-trusted-approval-boundary.md)
- [ADR-0010](ADR-0010-revit-local-trusted-approval-authority.md)
- [ADR-0012](ADR-0012-bim-it-product-family-and-specialized-mcp-services.md)
- [ADR-0013](ADR-0013-bim-it-assistant-local-architecture.md)
- GitHub Copilot SDK for .NET, `GitHub.Copilot.SDK` 1.0.21, including `CopilotClientMode.Empty` and custom tools
