# ADR-0009: Agent and human interaction and trusted approval boundary

- Status: Proposed
- Date: 2026-10-02

## Context

RevitMCP has one Revit-local capability and execution core. The current path is an ordinary stdio MCP client, `RevitMCP.Server`, the Named Pipe Bridge, the Addin, and EXEC-0001. `tools/list` exposes seven MCP tools. Six are reads. `revit_preview_parameter_updates` previews a bounded parameter batch and can create an ephemeral intent. It does not modify the Revit model. CAP-0008/apply does not exist. No trusted approval provider exists. No production Revit write exists.

MCP Apps are a possible future way for a person to interact with that same core without a new LLM reasoning turn. Examples discussed for that human surface include selection, focus, and zoom. The pressure is to treat those interactions as a reason to create another Revit MCP service, to expose every Revit UI action as a model tool, or to treat an app-only tool or an in-app approval screen as permission to write.

ADR-0005 already requires model-facing tools to be coherent capabilities rather than thin Revit API wrappers, and it keeps agent context bounded. ADR-0007 already keeps RevitMCP a specialized Revit capability service, keeps multi-service orchestration external and optional, and says a new MCP server is not justified merely because the tool count grew. ADR-0008 already requires every v1 write batch to follow preview, an immutable intent, trusted human approval, stale-state revalidation, one controlled Revit transaction, verification, and audit. ADR-0008 also says an MCP App may later present an approval UI, and that the app, the client, MRTR input, and tool annotations are not themselves approval.

This ADR separates those concerns. It does not implement them. External products, including Autodesk's official Revit MCP, Nonica, and other MCP implementations, remain reference patterns only. They are not architectural authorities for this decision.

## Decision drivers

- Keep one specialized Revit-local capability and execution core.
- Do not create a second Revit MCP service in order to host MCP Apps.
- Let audience and effect vary independently.
- Keep the model-facing tool surface coherent and reasoning-oriented.
- Allow a future MCP App to offer bounded direct human interactions that do not need another model turn.
- Classify by persistent Revit mutation, not by which surface invoked the action.
- Do not treat app visibility, an app-only tool, or an approval screen inside an app as trust, identity, or write permission.
- Keep persistent writes on the ADR-0008 path.
- Keep existing opaque RevitMCP identities authoritative.
- Keep current stdio valid for an MCP client/host that does not support MCP Apps.
- Leave orchestration external, optional, and framework-neutral.
- Do not authorize implementation from this ADR.

## Options considered

### Option A: A separate Revit MCP service for MCP Apps

Human interaction and model tools would be two Revit MCP servers.

Advantages:

- the model tool list stays small by putting human actions somewhere else;
- app hosting can evolve on its own process.

Disadvantages:

- splits one Revit execution context across two services;
- duplicates discovery, identity, and transaction safety;
- conflicts with ADR-0007, which rejects a new MCP server merely because the tool surface grew;
- makes ordinary stdio clients a second-class path if the app service becomes the real core.

Rejected.

### Option B: App visibility or an app-only tool is the approval boundary

If a person can see the MCP App, or if a tool is marked app-only, RevitMCP treats the call as human-approved and may write.

Advantages:

- one surface for both interaction and confirmation;
- no separate approval provider to design.

Disadvantages:

- seeing a UI is not authentication, authorization, or proof of who approved a batch;
- the app host can render, hide, or invoke tools without RevitMCP observing a person;
- an app-only write would bypass ADR-0008;
- ADR-0008 already rejects client-supplied acceptance, tool annotations, and `clientInfo` as approval.

Rejected.

### Option C: One core, two classification dimensions, and an unchanged write path

RevitMCP keeps one capability core. Each capability is classified by audience and, separately, by effect. Model tools stay reasoning-oriented. A future app may expose extra tools for bounded non-persistent human interaction. Persistent writes stay on the ADR-0008 path. An MCP App may display an approval UI but is not automatically the trusted approval provider. Orchestration stays outside the core. stdio remains valid.

Advantages:

- preserves ADR-0001, ADR-0005, ADR-0007, and ADR-0008;
- lets human interaction exist without a second Revit service and without a larger model tool list;
- keeps write safety independent of which client rendered the call;
- leaves MCP Apps optional.

Disadvantages:

- audience and effect must both be reviewed for every future capability;
- some Revit UI actions, including temporary hide/isolate, stay unclassified until their API behavior is validated;
- a trusted approval provider is still required before any apply can exist.

Proposed direction.

### Option D: Accept every non-parameter UI action as transient now

Selection, zoom, temporary hide/isolate, and similar view operations would all be classified as transient without checking whether Revit persists them.

Advantages:

- a larger human-interaction catalogue could be specified immediately.

Disadvantages:

- temporary hide/isolate may still change document, view, or model state in Revit;
- guessing that API behavior would make the mutation rule unenforceable.

Rejected. Temporary hide/isolate is not accepted as transient in this ADR.

## Decision

ADR-0009 does not supersede ADR-0001 through ADR-0008. Those decisions remain in force. This ADR is proposed only. It authorizes no production change.

### 1. One Revit-local capability and execution core

RevitMCP keeps one specialized Revit-local capability and execution core:

```text
MCP client
 -> RevitMCP.Server
 -> local Bridge
 -> RevitMCP.Addin
 -> Revit API / EXEC-0001
```

MCP Apps do not justify a separate Revit MCP service. A future MCP App is an optional human-interaction surface/adapter presented by a compatible MCP host. It is not a second place where Revit transactions, Revit-local identity, or capability semantics live.

Server-local request validation and instance routing stay the ADR-0001 / ADR-0007 server responsibility. That routing is not the external multi-service orchestrator.

### 2. Audience and effect are independent

Every capability has an audience classification and an effect classification. Audience does not imply effect. These are architectural descriptions, not an implementation enum or schema.

Audience / exposure:

- model — exposed for agent reasoning;
- app — exposed for a human-facing MCP App;
- model+app — exposed to both.

Effect:

- read;
- transient human interaction, the new category for interaction that does not persistently mutate the Revit document, view, or model;
- write. Persistent mutation remains Write.

ADR-0001 already requires Read, Write, and Destructive to remain distinguishable. Persistent write and destructive operation are not unrelated, mutually exclusive safety classes.

Destructive remains explicitly distinguishable, as ADR-0001 requires. For the ADR-0008 controlled-write path, a destructive operation is write-class behavior. A destructive classification never bypasses write approval or write safety.

model+app does not weaken the effect. A persistent write exposed in an app is still a write. A read exposed only to an app is still a read.

Existing accepted read tools and the accepted preview tool are model-facing. This ADR does not reclassify them and does not add an app audience to them.

### 3. Model-facing tools stay reasoning-oriented

Model-facing tools remain coherent capabilities an agent can reason over. They are not thin wrappers around every Revit UI or API action.

ADR-0005 still governs that model-facing surface: bounded results, deterministic contracts, concise schemas, and coherent batching. Placing an action on an app-only tool is not a reason to also place that action on the model tool list.

### 4. App-only tools are for bounded human interaction

A future MCP App may expose app-only tools for bounded direct human interactions that require no new LLM reasoning turn.

`app-only` describes audience. It does not mean safe, read-only, approved, authenticated, or authorized.

### 5. Persistent mutation decides write versus transient interaction

The architectural rule is:

```text
no persistent Revit document/view/model mutation
  -> may qualify as transient human interaction

persistent Revit document/view/model mutation
  -> write
```

Selection, focus, and zoom are examples that may qualify as transient human interactions when they do not persistently mutate the Revit document, view, or model.

Absence of persistent mutation is necessary before an action may be classified as transient. It does not by itself accept a tool, a schema, or an implementation.

Temporary hide/isolate is not accepted as transient. Its concrete Revit API behavior must be validated before any later specification classifies it.

Permanent hide/isolate, persistent graphic overrides, parameter changes, and other model changes are writes.

Transient human interaction is only the new non-persistent interaction category. Persistent mutation remains Write. Destructive remains explicitly distinguishable, as ADR-0001 requires, and is write-class behavior for the ADR-0008 controlled-write path. A destructive operation never bypasses write approval or write safety merely because it has its own classification. This ADR does not redefine which operations are destructive.

### 6. App visibility is not a trust boundary

MCP App visibility is not:

- authentication;
- authorization;
- human identity;
- approval;
- write permission.

An MCP App may present an approval UI. That presentation does not make the app a trusted approval provider. Trust in an approval provider must still be established independently, as ADR-0008 requires. If that trust is absent, apply fails closed.

### 7. Persistent writes cannot bypass ADR-0008

Persistent writes may never bypass ADR-0008 through an app-only tool, an MCP App, a model tool, or an external orchestrator.

The required write path remains:

```text
preview
-> immutable intent
-> trusted human approval
-> stale-state revalidation
-> controlled Revit transaction
-> verification
-> audit
```

Preview remains not approval and not a model write. Apply remains a separate future capability. This ADR does not define CAP-0008.

### 8. Existing opaque identities stay authoritative

These RevitMCP identities remain authoritative:

- `instance_id`;
- `document_id`;
- `element_ref`;
- `parameter_ref`;
- `intent_ref`.

An MCP client or host, including a host that presents a future MCP App, must not parse these identities or replace them with a parallel identity system. ADR-0003 and ADR-0006 remain the identity decisions. An app-only tool does not create a new client-facing Revit identity.

### 9. stdio and ordinary MCP clients remain supported

Current stdio operation remains valid. MCP Apps are optional. They must not replace ordinary MCP compatibility.

An MCP client/host that does not support MCP Apps must still be able to use the model-facing capabilities. RevitMCP must not require an MCP App, a browser host, or Streamable HTTP to perform its current work.

### 10. Orchestration stays external and optional

ADR-0007 remains in force. Multi-service orchestration stays outside the Revit capability core and is not required to use RevitMCP.

An orchestrator may coordinate workflow and may coordinate approval only as ADR-0007 and ADR-0008 already allow. It must not own Revit API execution, Revit transactions, Revit-local identity resolution, or Revit capability semantics.

This ADR does not choose LangGraph, Semantic Kernel, the OpenAI Agents SDK, or any other orchestration framework.

### 11. This ADR does not authorize implementation

ADR-0009 does not authorize:

- MCP Apps;
- app-only tools;
- CAP-0008 or any apply capability;
- a trusted approval-provider implementation;
- Streamable HTTP or any transport change;
- MRTR implementation or an MCP SDK upgrade;
- WebMCP;
- product UI;
- an orchestrator;
- temporary or permanent hide/isolate;
- any production write, transaction, save, or sync.

No capability is reclassified by this ADR. No tool is added or removed.

## Consequences

### Positive

- Human interaction can be discussed without splitting the Revit core or enlarging the model tool list.
- Write safety stays in ADR-0008 even if a later app renders the approval screen.
- Reviewers can reject an app-only write without a new safety model.
- Ordinary stdio clients remain first-class.
- Temporary hide/isolate cannot be treated as harmless before its Revit behavior is known.

### Costs / limitations

- Future capabilities need both an audience decision and an effect decision.
- Transient interaction is not available as a classification for temporary hide/isolate until a later, evidence-based decision.
- Writes still cannot ship until a trusted approval provider exists.
- This proposed ADR does not select the product UI, the first approval provider, or an orchestration technology.

## Explicitly not decided

- whether ADR-0009 is accepted;
- which future tools are `model`, `app`, or `model+app`;
- whether temporary hide/isolate is transient after Revit API validation;
- the MCP App host, manifest, or UI;
- which trusted approval provider ships first;
- CAP-0008 schema, errors, and Bridge methods;
- Streamable HTTP, remote MCP, and authentication;
- orchestration implementation technology;
- tool-surface scoping beyond the audience dimension above.

## References

- ADR-0001: out-of-process MCP server and Revit capability host
- ADR-0003: Revit instance registration, discovery, and addressing
- ADR-0005: agent context and token efficiency
- ADR-0006: document and element reference identity
- ADR-0007: federated MCP boundaries and optional orchestration
- ADR-0008: controlled write safety model
- Official MCP Apps specification, stable 2026-01-26. Supporting architecture reference only. This ADR does not implement MCP Apps: https://github.com/modelcontextprotocol/ext-apps/blob/main/specification/2026-01-26/apps.mdx
- MCP specification 2026-07-28, including the extensions framework. Supporting architecture reference only. This ADR does not change the protocol: https://modelcontextprotocol.io/specification/2026-07-28
- `docs/ARCHITECTURE.md`
- `docs/PRODUCT_UI_CONSIDERATIONS.md`
- `docs/VISION.md`
