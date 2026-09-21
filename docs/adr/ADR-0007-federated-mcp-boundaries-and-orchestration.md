# ADR-0007: Federated MCP boundaries and optional orchestration

- Status: Accepted
- Date: 2026-09-21

## Context

After SERVER-0004, RevitMCP exposes four read-only stdio MCP tools against a local Revit process through the existing Server, Named Pipe Bridge, Addin, and EXEC-0001 path. That Revit-local architecture is implemented and live-validated on Revit 2026.5.

A post-SERVER-0004 architecture review, informed by Autodesk University 2026 material, asked how RevitMCP should grow if later work includes Autodesk cloud services, multi-system workflows, or a larger capability catalogue. The review itself is not reproduced here. This ADR records the project decision using RevitMCP reasoning and public MCP references.

The risk is that later Autodesk/AEC work could turn `RevitMCP.Server` into a general mega-server: mixing local Revit execution with APS, ACC, Forma, workflow state, project memory, and an ever-growing tool list. Those Autodesk cloud concerns do not share the same process, security, identity, deployment, or failure model as local Revit.

This ADR does **not** implement a second MCP server, APS/Forma integration, an orchestrator, dynamic tool registration, write tools, authentication, or UI. It does **not** choose the next Revit capability.

## Decision drivers

- Preserve the existing Revit-local architecture and current four MCP tools.
- Keep Revit execution context, transactions, and local identities isolated from cloud Autodesk APIs.
- Allow ordinary MCP clients to use RevitMCP without an orchestrator.
- Keep core Revit capabilities vendor-neutral across MCP clients and LLMs.
- Limit pressure toward an unbounded agent-facing tool surface, consistent with ADR-0005.
- Leave repository topology, process count, packaging, deployment, orchestration technology, cloud identity, and auth implementation undecided.

## Options considered

### Option A: One universal Autodesk/AEC MCP server

One server would expose Revit, APS, ACC, Forma, workflow, memory, and related tools together.

Advantages:

- one connection for an agent that needs several Autodesk systems;
- fewer named services to discover at first.

Disadvantages:

- couples local Revit execution to cloud auth, deployment, and retry models;
- mixes Revit-local identities with cloud/project identities;
- encourages uncontrolled tool-surface growth;
- increases the blast radius of unrelated failures;
- makes it harder to keep RevitMCP directly usable as a specialized capability service.

Rejected as the default architecture.

### Option B: Specialized MCP services with optional external orchestration

RevitMCP remains a specialized Revit capability service. Autodesk cloud capability such as APS, ACC, and Forma is logically separated from the Revit-local MCP boundary when implemented. This option does not decide whether those cloud capabilities are exposed through one coherent Autodesk-cloud MCP service or several specialized services. A workflow orchestrator may compose RevitMCP with one or more cloud MCP services, but it is not part of the Revit core and is not required to use RevitMCP.

Advantages:

- preserves the current Revit-local path;
- isolates Revit safety and identity from cloud concerns;
- lets APS/Forma evolve independently;
- supports multiple MCP clients;
- allows future orchestration without requiring it;
- limits pressure toward a giant tool surface.

Disadvantages:

- multi-service workflows need explicit orchestration and state ownership later;
- auth and identity composition become future work;
- more deployment units may eventually exist;
- cross-service observability becomes important;
- capability discovery/scoping policy must eventually be implemented.

Accepted.

### Option C: One Revit MCP plus direct client-specific integrations for everything else

RevitMCP would stay specialized, but APS/Forma and other systems would be reached only through host- or vendor-specific integrations rather than reusable MCP service boundaries.

Advantages:

- no additional RevitMCP-owned service to design now;
- a single client can hard-wire its own Autodesk integrations.

Disadvantages:

- orchestration becomes client- and vendor-specific;
- cross-system workflows are harder to reuse;
- conflicts with vendor-neutral MCP composition.

Rejected as the architectural default.

## Decision

RevitMCP remains a **specialized Revit capability service**.

The existing Revit-local architecture remains:

```text
MCP client
 -> RevitMCP.Server
 -> local Bridge
 -> RevitMCP.Addin
 -> Revit API / EXEC-0001
```

Do not turn `RevitMCP.Server` into a general Autodesk/AEC mega-server.

Autodesk cloud capability such as APS, ACC, and Forma is logically separated from the Revit-local MCP boundary when implemented. ADR-0007 does **not** decide whether APS/ACC/Forma are exposed through one coherent Autodesk-cloud MCP service or several specialized services. That split remains deferred based on auth, lifecycle, capability cohesion, deployment, and failure semantics. Those cloud capabilities may eventually live in the same broader product or repository. This ADR does **not** decide repository topology, process count, packaging, or deployment topology. Multiple MCP processes are not required now.

This ADR does not supersede ADR-0001 through ADR-0006.

### 1. Local vs cloud boundary

Keep these responsibilities distinct.

Revit MCP owns:

```text
local Revit process
Revit execution context
active/open model state
Revit-specific identities
read/write transaction safety
local instance discovery
```

Autodesk cloud MCP (APS / ACC / Forma), when implemented, owns:

```text
Autodesk cloud APIs
cloud/project identities
OAuth / authorization
remote-capable deployment
long-running cloud operations
cloud-specific retry/idempotency concerns
```

Do not expose the local Revit Named Pipe directly to remote or cloud clients.

Do not reuse Revit-local `instance_id`, `document_id`, `element_ref`, or `parameter_ref` as generic APS/cloud identities. Cross-service workflows must use explicitly designed higher-level or project identities when needed. The exact cloud identity contract is deferred.

### 2. Orchestration

An orchestrator may compose multiple MCP services, for example:

```text
Agent / Host
   |
Workflow / Orchestration
   |
   +-- Revit MCP
   +-- Autodesk cloud MCP (APS / ACC / Forma)
   +-- future validation/rules MCP
   +-- future project-memory MCP
```

The orchestrator is **external** to the Revit capability core.

It may own workflow sequencing, multi-service routing, workflow state, cross-service retry/recovery policy, and approval/workflow coordination.

It must **not** own Revit API execution context, Revit transactions, Revit-local identity resolution, or Revit capability semantics.

RevitMCP must remain directly usable by ordinary MCP clients without this orchestrator. An orchestrator is not required now and is not implemented by this ADR.

This multi-service orchestrator is distinct from the Server-local request validation and routing already described in ADR-0001.

This ADR does not choose LangGraph, Semantic Kernel, OpenAI Agents SDK, Claude tooling, or any other orchestration framework. Orchestration implementation technology remains deferred and vendor-neutral.

### 3. MCP service specialization

Prefer coherent MCP service boundaries over one universal server when domains have materially different:

- execution environments;
- security/auth models;
- deployment models;
- lifecycle;
- ownership;
- failure/retry semantics.

Do **not** create a new MCP server merely because the number of tools increased. Server/service boundaries are semantic and operational boundaries, not arbitrary tool-count thresholds.

### 4. Tool-surface scoping

The total capability catalogue may grow, but the tool surface presented to an agent for a workflow should remain small and coherent.

Do not adopt a hard numeric limit such as six tools as a RevitMCP invariant.

The current four tools remain exposed exactly as today:

```text
revit_get_context
revit_query_elements
revit_get_elements
revit_describe_parameters
```

ADR-0007 introduces no tool split and no dynamic tool-list change. Dynamic tool exposure is not implemented.

For future growth, acceptable mechanisms to evaluate include:

- capability/workflow profiles;
- MCP dynamic tool-list exposure;
- separate specialized MCP services;
- host/orchestrator-side tool selection.

The mechanism is explicitly deferred.

Tool scoping must preserve:

- deterministic discoverability;
- vendor neutrality;
- clear capability contracts;
- ADR-0005 context/token-efficiency goals.

Illustrative future domains such as `core read`, `parameters`, `MEP`, `structural`, and `writes` may be discussed as examples. They are **not** accepted profile names or contracts.

The official MCP 2026-07-28 specification has a stateless protocol core over Streamable HTTP, says servers SHOULD return `tools/list` results in deterministic order, and supports cacheable list results plus opt-in list-change notifications delivered through client-opted `subscriptions/listen`. Those public protocol features are relevant when a future scoping mechanism is designed. They do not implement scoping in RevitMCP now.

### 5. Writes

ADR-0007 does **not** authorize write-capability implementation.

The future direction remains that writes will require explicit safety design, including concepts such as:

```text
preview
approval/confirmation
stale-state revalidation
controlled transaction
audit
```

The detailed write model requires its own future architecture or specification decision.

### 6. APS / remote MCP

A future APS/ACC/Forma service is expected to have a materially different transport and auth boundary from local Revit.

A likely architectural path is:

```text
remote-capable MCP
Streamable HTTP
MCP authorization
upstream APS OAuth
```

This is a direction, not a completed design. ADR-0007 does **not** select a specific identity provider, OAuth topology, token storage mechanism, APS auth flow, or hosting platform. Those require a dedicated future security/deployment ADR.

Passing tokens through model context is **not** an acceptable future design direction.

### 7. Project memory / validation services

Project-memory MCP and validation/rules MCP are possible future specialized services. They are **not** part of the current implementation roadmap. Do not add them to the solution or create specifications for them now.

The architectural point is only that such concerns should not be forced into `RevitMCP.Server` merely because they participate in the same workflow.

## Existing decisions preserved

ADR-0007 preserves:

- ADR-0001 Server/Addin separation;
- ADR-0002 Named Pipe local Bridge;
- ADR-0003 Revit instance discovery and addressing;
- ADR-0004 solution and multi-version structure;
- ADR-0005 bounded context/token efficiency;
- ADR-0006 document/element identity;
- current Bridge protocol v5;
- the current four MCP tools;
- the Revit 2025 / 2026 / 2027 target strategy.

No previous ADR is superseded.

## Consequences

### Positive

- Preserves the existing RevitMCP core and current four-tool stdio surface.
- Keeps local Revit safety, execution context, and identities isolated.
- Permits APS/Forma to evolve independently when that work is authorized.
- Supports multiple MCP clients without requiring an orchestrator.
- Allows future orchestration without making it mandatory.
- Limits pressure toward a giant tool surface.
- Aligns with ADR-0005 bounded, deterministic agent context.

### Negative / costs

- Multi-service workflows will need explicit orchestration and state ownership later.
- Auth and identity composition become explicit future work.
- More deployment units may eventually exist.
- Cross-service observability becomes important.
- Capability discovery and scoping policy must eventually be implemented.

## Deferred decisions

Separate ADRs or specifications are required for:

- the next Revit capability;
- write/destructive confirmation, transaction, and audit model;
- APS/ACC/Forma MCP contracts and cloud identity;
- remote MCP transport, MCP authorization, and APS OAuth topology;
- orchestration implementation technology;
- tool-scoping mechanism (profiles, dynamic lists, specialized services, or host selection);
- repository topology, process count, packaging, and deployment;
- project-memory or validation/rules services, if they are ever authorized.

## References

- ADR-0001: Out-of-process MCP server and Revit capability host
- ADR-0002: Named Pipes and JSON-RPC local bridge
- ADR-0003: Revit instance registration, discovery, and addressing
- ADR-0004: Solution structure and multi-version build strategy
- ADR-0005: Agent context and token efficiency
- ADR-0006: Document and element reference identity
- Model Context Protocol specification (2026-07-28): https://modelcontextprotocol.io/specification/2026-07-28
- MCP 2026-07-28 changelog (stateless remote MCP, list caching, list-change subscriptions): https://modelcontextprotocol.io/specification/2026-07-28/changelog
- MCP transports: https://modelcontextprotocol.io/specification/2026-07-28/basic/transports
- MCP Streamable HTTP: https://modelcontextprotocol.io/specification/2026-07-28/basic/transports/streamable-http
- MCP authorization: https://modelcontextprotocol.io/specification/2026-07-28/basic/authorization
- MCP tools: https://modelcontextprotocol.io/specification/2026-07-28/server/tools
