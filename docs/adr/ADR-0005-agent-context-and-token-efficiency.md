# ADR-0005: Agent context and token efficiency

- Status: Accepted
- Date: 2026-09-09

## Context

Revit models can expose very large amounts of structured data, while AI agents have finite context windows and incur latency/cost when tool schemas and tool results are unnecessarily verbose.

A Revit capability layer that simply returns broad model dumps would be easy to implement but expensive for agents to consume, difficult to reason about, and likely to degrade reliability as model size grows.

RevitMCP therefore needs a vendor-neutral design rule for minimizing AI context consumption without making contracts ambiguous, lossy, or unsafe.

This is a cross-cutting concern. It affects capability design, MCP tool schemas, bridge payloads, result shaping, errors, pagination, batching, and future model-query operations.

## Decision drivers

- Keep routine agent interactions small enough to fit comfortably in model context.
- Avoid spending tokens on data the agent did not request or cannot act on.
- Keep capability behavior deterministic and machine-readable.
- Preserve vendor neutrality; do not optimize for one tokenizer or one LLM provider.
- Avoid moving filtering work to the LLM when RevitMCP can perform it deterministically before returning data.
- Keep result size predictable as BIM model size grows.
- Reduce unnecessary tool round-trips without creating large generic mega-tools.
- Preserve security, authorization, diagnostics, and recovery semantics even when optimizing payload size.

## Options considered

### Option A: Return broad raw data and let the AI filter it

Advantages:

- simplest Revit-side implementation;
- maximum raw information available to the client.

Disadvantages:

- unbounded context consumption;
- high latency and model cost;
- repeated transmission of irrelevant BIM data;
- poorer agent reliability on large responses;
- forces probabilistic client-side filtering where deterministic server-side filtering is possible.

### Option B: Rely on individual MCP clients to truncate results

Advantages:

- minimal RevitMCP-specific design work;
- clients can apply their own context policies.

Disadvantages:

- truncation may destroy semantic completeness;
- behavior varies by client/provider;
- the expensive payload has already been produced and transported;
- clients cannot reliably reconstruct server-side query intent after arbitrary truncation.

### Option C: Design RevitMCP capabilities for bounded, query-efficient agent context

Advantages:

- predictable context usage;
- better agent usability and latency;
- deterministic filtering and aggregation close to the data source;
- transport- and LLM-neutral;
- scales better to large BIM models.

Disadvantages:

- capability contracts require more deliberate design;
- large-result capabilities need explicit pagination/projection/filter semantics;
- overly aggressive minimization could omit useful information if contracts are poorly designed.

## Decision

RevitMCP will treat **AI context/token efficiency as a first-class capability-design requirement**.

The objective is not to minimize bytes at any cost. The objective is to return the **smallest deterministic payload that fully satisfies the capability contract and supports reliable agent recovery**.

### 1. Bounded-by-default results

Capabilities should be bounded independently of total Revit model size whenever their purpose allows it.

A capability whose natural result can grow with model size must define one or more of:

- filters;
- explicit limits;
- pagination/cursors;
- field projection;
- aggregation/summary modes;
- deterministic truncation metadata.

Unbounded whole-model enumeration must not be the default behavior.

### 2. Filter and aggregate before returning data

When RevitMCP can deterministically filter, aggregate, sort, count, or project data before returning it, it should generally do so instead of sending a broad dataset for the AI to filter afterward.

This applies especially to element queries, parameters, warnings, views, systems, rooms/spaces, MEP networks, and future analytical results.

### 3. Structured content is authoritative

Machine-readable structured results are the authoritative representation of capability output.

MCP-facing prose must not duplicate the full structured payload merely for readability.

When a human-readable text summary is useful, it should be concise and additive rather than a second serialization of the same result.

Clients must not be required to parse prose to recover structured values.

### 4. Avoid redundant fields and metadata

Capability results should not repeat data already established by routing/identity context unless that information is needed for correctness, recovery, or result interpretation.

Internal implementation details, local paths, environment metadata, verbose type descriptions, and diagnostic fields must not be added to normal results without a concrete need.

Opaque identifiers should remain opaque rather than being expanded into verbose metadata by default.

### 5. Tool schemas and descriptions should remain concise

MCP tool names, descriptions, input schemas, and output schemas consume agent context as well as tool results.

Tool contracts should therefore be:

- coherent rather than fragmented into many tiny near-duplicate tools;
- specific rather than generic mega-tools;
- concise in descriptions while preserving usage guidance;
- free of redundant schema fields;
- designed so common workflows require as few unnecessary round-trips as practical.

### 6. Prefer coherent batching when it reduces context/round-trip cost

Batch-oriented operations are preferred when multiple closely related items can be processed deterministically in one request and one bounded result.

Batching must not become an excuse for arbitrary command execution or unbounded payloads.

### 7. Errors remain compact and deterministic

Errors should expose stable codes and the minimum structured detail needed for an agent to recover.

Stack traces, repeated exception chains, local file paths, usernames, and unrelated diagnostics do not belong in normal agent-facing error payloads.

Detailed diagnostics may be recorded through local observability mechanisms separately.

### 8. Token efficiency must remain provider-neutral

RevitMCP will not define correctness in terms of a specific OpenAI, Anthropic, Google, or other tokenizer.

Where size needs to be tested or constrained, use provider-neutral measures such as:

- item counts;
- field counts;
- serialized payload size;
- explicit page/limit bounds.

Provider-specific token estimates may be used diagnostically but are not part of transport-neutral contracts.

### 9. Safety and correctness take precedence

Token/context efficiency must never be used to omit information required for:

- authorization;
- destructive-operation confirmation;
- deterministic error recovery;
- transaction safety;
- identity validation;
- data integrity.

If a larger payload is required for correctness, the contract should make that cost explicit rather than silently truncating critical information.

### 10. Capability reviews must consider context cost

New capability specifications and Tech Lead reviews should explicitly ask:

- Is the result bounded?
- Can filtering/aggregation happen before returning data?
- Are any fields redundant?
- Does prose duplicate structured content?
- Is pagination/projection required?
- Could a coherent batch reduce repeated calls?
- Does the tool/schema surface add more agent context than the workflow justifies?

## Consequences

### Positive

- RevitMCP remains practical on large BIM models.
- Agents consume less irrelevant context and can reason over more useful information.
- Lower transport/context cost is achieved without coupling to one AI provider.
- Capability design naturally favors explicit queries, projections, limits, and deterministic summaries.
- Tool surfaces remain smaller and more coherent.

### Negative / costs

- Large-data capabilities require explicit query/result-shaping design.
- Some future capabilities will need pagination or projection contracts before implementation.
- Context efficiency becomes an additional review criterion and may reject otherwise easy-to-implement broad data dumps.

## Non-goals

This ADR does not:

- define a project-wide numeric token budget;
- require exact tokenizer integration;
- mandate compression or binary encoding for local JSON-RPC;
- require shortened/cryptic property names that harm contract clarity;
- authorize lossy truncation of correctness-critical information;
- replace performance profiling or normal transport optimization.

## References

- ADR-0001: Out-of-process MCP server and Revit capability host
- ADR-0004: Solution structure and multi-version build strategy
- CAP-0001: `revit_get_context`
- BRIDGE-0001: Handshake and protocol-version negotiation
