# ADR-0024: Registry-driven compiled Revit read operations

- Status: Proposed
- Date: 2026-11-10
- Product Owner: Dave

## Context

[ADR-0023](ADR-0023-declarative-revit-read-query-architecture.md) rejected arbitrary model-supplied Python, C#, or Revit API execution in the production capability layer. It introduced a trusted, compiled, declarative read kernel exposed through small source-specific MCP tools. Two sources proved it live:

- [CAP-0014](../capabilities/CAP-0014-revit-query-family-types.md) `revit_query_family_types` (Bridge protocol 22), natural-agent gate passed 2026-11-04;
- [CAP-0015](../capabilities/CAP-0015-revit-query-parameter-bindings.md) `revit_query_parameter_bindings` (Bridge protocol 23), natural-agent gate passed 2026-11-06.

A third real need then appeared. The firm-owned Electrical Standards Skill v0.1 needs the user worksets of a project. No generic read exposes worksets, so the Skill records `capture_status: missing_fact` ([ELECTRICAL_STANDARDS_SKILL_V0_1.md](../ELECTRICAL_STANDARDS_SKILL_V0_1.md), [staging validation](../ELECTRICAL_STANDARDS_SKILL_V0_1_STAGING_VALIDATION.md)). [CURRENT_STATE.md](../CURRENT_STATE.md) already names worksets as "a fact that is easy in the Revit API but that should not automatically become another hardcoded MCP field or tool".

The ADR-0023 pattern answers that need correctly, but its cost per fact is high and grows linearly. Each source today adds, end to end:

- request, result, and error DTOs in `BimIt.Revit.Contracts`;
- one Bridge service interface (`IRevitQuery…Service`), one constructor parameter and one `[JsonRpcMethod]` in `StreamJsonRpcBridgeAdapter`, and one new linear Bridge protocol version with its `Supports…` predicate;
- one Addin capability service over EXEC-0001;
- six Server files (`…ApplicationService`, `…JsonSchemas`, `…McpTools`, `…Outcome`, `…ToolMetadata`, `…ToolRegistration`) and one more entry in `tools/list`;
- CAP, BRIDGE, and SERVER specifications.

Continuing would give `revit_query_worksets`, `revit_query_phases`, `revit_query_design_options`, `revit_query_levels`, `revit_query_view_templates`, and so on: the Revit API surface rebuilt one MCP tool at a time. Every client pays for every tool in `tools/list` context (ADR-0005 §5), and the Bridge protocol number advances once per fact. The Product Owner explicitly wants to avoid that.

### External reference: pyRevit MCP (`develop`)

The pyRevit MCP server was inspected on 2026-11-10 at `pyrevitlabs/pyRevit` `develop` commit `615530d24632033875e6fedeffa0e57adfeec60f`, mainly `dev/pyRevitLabs/pyRevitCLI/PyRevitMcpServer.cs`, `PyRevitAutomationOperations.cs`, and `pyrevitlib/pyrevit_automation.py`. Facts relevant to this decision:

- **Small tool surface.** 15 MCP tools: `list_skills`, `get_skill`, `list_revit_instances`, `get_context`, `inspect_elements`, `lookup_pyrevit_api`, `lookup_revit_api`, `list_automation`, `run_automation`, `show_elements`, `navigate_revit_link`, `capture_view`, `run_query`, `run_modify`, `get_run`. There is no tool per Revit data source.
- **Discovery instead of tools.** `lookup_revit_api` reflects over the running Revit API to describe types and members (signatures, enum values, obsolete markers). `lookup_pyrevit_api` searches the pyrevitlib and rpw libraries. Both are informational.
- **Skills carry the know-how.** `revit-scripting`, `pyrevit-library`, and task skills (`views`, `scheduling`, `modeling`, …) tell the agent how to write scripts and which library functions to use.
- **A reviewed automation registry.** A Python decorator, `pyrevit_automation.operation(automation_id, PlainEnglish, mode, effects, context, transaction, inputs)`, attaches metadata to a library function. `list_automation` lists marked operations by stable identifier, paginated, without Revit. `run_automation` runs one by `id` with an `inputs` object. An operation is invocable only when it is hand-wired in `PyRevitAutomationOperations`, or when its marker declares `inputs` on a module-level function of pyrevitlib or rpw whose mode is `query` or `pure` and whose transaction is `none`. Inputs are a closed typed list (`string`, `number`, `integer`, `boolean`, `element`, `scalar`; text at most 256 characters). Unknown inputs are rejected. The run is a query, so model changes are rolled back, and the tool states that it accepts no Python source, module names, or callable names. Markers are found by scanning library source text. A marker declares inputs but no output schema.
- **Generic code execution remains the main path.** `run_query` runs model-written read-only Python (changes rolled back), and `run_modify` runs model-written Python that changes the model, with a dry run and, under policy `ask`, a user approval prompt.

The architectural lesson: **pyRevit avoids hundreds of per-Revit-API MCP tools by combining Skills, discovery, and a few generic execution surfaces.** Its `list_automation` / `run_automation` pair shows that a stable-identifier registry behind one generic tool is usable by agents.

BIM-IT adopts the useful part: a registry, discovery by stable identifier, one generic execution surface, and Skills that point at operations. BIM-IT does **not** adopt generic code execution (`run_query`, `run_modify`), and its registry differs from pyRevit's in three ways that matter for BIM-IT's product goal:

| | pyRevit automation registry | BIM-IT read-operation registry (this ADR) |
|---|---|---|
| Membership | Any marked, eligible function in pyrevitlib or rpw, found by scanning source text, plus hand-wired entries | Only operations compiled into BIM-IT and listed explicitly in one registration list. No scanning, no reflection discovery |
| Output | Whatever the function returns. No declared output schema | A declared, versioned output schema per operation, validated before the result leaves Revit |
| Identity | `element` inputs are raw element id integers | No raw Revit numeric ids. Opaque references under ADR-0006 where identity is needed |
| Model-written code | Also offered (`run_query`, `run_modify`) | Never offered (ADR-0023 §1) |

pyRevit is a valid scripting runtime for a trusted power user. This comparison records a design difference, not a judgement.

## Decision drivers

- Avoid one MCP tool per Revit data source. Keep the visible MCP surface small (ADR-0005, ADR-0007 §4).
- Keep every ADR-0023 invariant: no model-supplied code, closed request vocabulary, trusted compiled read operations, bounded results, deterministic errors, structured content authoritative.
- The Addin, running inside a specific Revit version, is the only authority on which operations exist (ADR-0001, ADR-0004). Descriptors are not duplicated in the Server.
- Adding a new factual read should cost one compiled operation, its tests, and a short contract, not a new tool, a new Bridge method, and a new protocol version.
- Same behaviour for external MCP clients and BIM-IT Assistant, provider-neutral (ADR-0014, ADR-0022).
- Skills orchestrate. They never grant access (ADR-0022).
- Read and write stay clearly separated (AGENTS.md, ADR-0008).
- Opaque identity, never internal Revit numeric ids (ADR-0006).

## Options considered

All three options keep ADR-0023 §1: no model-written code runs. A generic code-execution surface (ADR-0023 Option A, pyRevit's `run_query`) was rejected there and is not reconsidered.

### Option A: Generic `revit_lookup` + `revit_read` over a closed registry, with per-operation schemas and runtime validation

Two MCP tools. `revit_lookup` returns registered operation metadata, including each operation's input and output JSON Schemas. `revit_read` executes one registered operation by stable id with an `inputs` object. The Addin validates inputs and outputs per operation at runtime.

- Advantages:
  - The MCP surface grows by two tools once, then stays fixed however many operations are added.
  - One Bridge method pair and one protocol version gate the mechanism. Later operations need no protocol bump.
  - The Server stays a thin router. The running Addin is the single source of operation metadata.
  - `revit_read` has one honest set of annotations (`readOnlyHint: true`, `openWorldHint: false`), so ADR-0022's Assistant read facade discovers it with no Assistant change.
  - Operation metadata is loaded only when needed (progressive disclosure), like Skills.
- Disadvantages, stated plainly:
  - **Loss of MCP-level static typing per operation.** MCP advertises one static `inputSchema` and `outputSchema` per tool. `revit_read`'s schemas can only describe the envelope: `operation` is a string with no enum (the set depends on the connected host), `inputs` is an open `object`, and the result payload is an `object`. A client that validates arguments or structured output against `tools/list` schemas validates only the envelope. Clients that generate typed bindings from `tools/list` get no per-operation types.
  - **The agent must look before it reads.** The precise contract is one `revit_lookup` call away, an extra round trip compared with a dedicated tool whose schema is already in context. An agent that guesses inputs gets a runtime validation error instead of being steered by the schema before the call.
  - **Less first-class visibility.** An operation is not visible in `tools/list`. Hosts that show, allow-list, or approve tools by name see `revit_read`, not `project.user_worksets`. Per-operation permissions in such clients are not possible.
  - **Errors carry more of the contract.** Validation errors must be precise enough to steer the agent (accepted input names, the failing constraint), as pyRevit's `run_automation` already does.
  - Runtime validation is BIM-IT's responsibility on both sides: inputs before execution, outputs before serialization.
- Mitigations, which reduce but do not remove the loss: exact lookup returns full JSON Schemas; Skills name the operation ids they rely on; the `revit_read` description tells the agent to call `revit_lookup` first; errors name the accepted inputs; contract tests prove each operation's declared schemas match its typed C# contract.

### Option B: Expose each registered operation dynamically as its own MCP tool

The Server builds one MCP tool per registered operation, with that operation's precise input and output schemas, from the Addin registry.

- Advantages: full MCP-level static typing per operation; every operation is first-class in `tools/list`; per-tool client permissions work; no extra lookup round trip.
- Disadvantages:
  - It rebuilds the per-source tool surface the Product Owner wants to avoid. Fifty operations become fifty tools in every client's context, which is the ADR-0005 cost this ADR exists to stop. Only the authoring cost is removed.
  - `tools/list` would depend on which Revit instance is connected. The Server routes by `instance_id` across several Revit processes and versions, so the list would be a union with per-instance holes, or would change at runtime. That needs `notifications/tools/list_changed`, whose client support is uneven, and conflicts with ADR-0007 §4's requirement of deterministic discoverability. The Server could not answer `tools/list` before any Revit instance is reachable.
  - Tool annotations, names, and descriptions become generated text. Review of the agent-facing contract moves from source to runtime output.
  - The ADR-0022 Assistant facade would see a changing tool set on each turn.

ADR-0007 §4 lists dynamic tool-list exposure as a mechanism to evaluate later. This ADR does not adopt it. It remains possible to *promote* an individual operation to a dedicated tool by an explicit decision (see the decision rule).

### Option C: Continue source-specific MCP tools (ADR-0023 §4 as written)

- Advantages: proven twice; full static typing; first-class visibility; no new mechanism.
- Disadvantages: linear growth of tools, Bridge methods, protocol versions, Server files, and specifications per fact. It is the path to rebuilding the Revit API as MCP tools. It also makes simple facts expensive enough that they get deferred, which is how the workset `missing_fact` arose.

### Preferred direction

**Option A.** It keeps the visible MCP surface small and fixed and the Addin authoritative, at the cost of per-operation static typing in MCP. That cost is accepted deliberately, is mitigated by lookup, Skills, contract tests, and precise errors, and is bounded by the decision rule below: a contract that needs first-class visibility keeps a dedicated tool.

## Decision (proposed)

### 1. Two MCP tools over a closed registry of compiled read operations

BIM-IT introduces a registry of trusted compiled C# read operations hosted by the Addin, and two MCP tools in `BimIt.Revit.Server`.

**`revit_lookup`** discovers or describes registered BIM-IT read operations. It searches BIM-IT operation metadata only, never Revit API types or members.

- Search mode: optional text query and paging; returns summaries in a deterministic order (operation id, ordinal).
- Exact mode: one operation id; returns its full contract metadata:
  - stable operation id (for example `project.user_worksets`);
  - title and description;
  - contract version, and the other versions this host supports, if any;
  - context requirement (v1: `document`, the active project document);
  - effects (v1: always `read`; see §5);
  - bounded input schema (JSON Schema, closed: `additionalProperties: false`);
  - output schema (JSON Schema of the result payload);
  - pagination capability (`none` or `cursor`);
  - availability on this host (see §7).
- Unknown id: deterministic `UNKNOWN_READ_OPERATION` error.

**`revit_read`** executes one registered operation. Conceptual request:

```json
{
  "instance_id": "optional Server routing identity",
  "document_id": "active-document guard",
  "operation": "project.user_worksets",
  "contract_version": 1,
  "inputs": {},
  "page_size": 100,
  "cursor": "opaque, only for operations with cursor pagination"
}
```

Conceptual result (structured content, authoritative):

```json
{
  "operation": "project.user_worksets",
  "contract_version": 1,
  "document_id": "…",
  "result": { "…": "operation-specific, matches the declared output schema" },
  "complete": true,
  "next_cursor": null
}
```

`contract_version`, `page_size`, `cursor`, `complete`, and `next_cursor` are illustrative. The exact envelope is fixed by the CAP / BRIDGE / SERVER specifications that implement this ADR.

The model or client **cannot** provide C#, Python, Dynamo, expressions, .NET type names, Revit API type or member names for execution, reflection targets, or arbitrary methods or callables. `operation` is matched by ordinal equality against the registry's ids and selects nothing else. **Only an operation id registered by BIM-IT can execute.** Unknown envelope fields and unknown `inputs` properties are rejected, never ignored (the existing `ClosedSchemaArgumentValidator` / `StrictInputMcpServerTool` behaviour at the envelope; the operation's closed binder for `inputs`).

Both tools are `readOnlyHint: true`, `destructiveHint` absent, `openWorldHint: false`, accept `instance_id`, and declare an output schema for their envelope (ADR-0005 §3). `revit_read` takes `document_id`, so the ADR-0022 Assistant facade injects the bound instance and the active document exactly as it does for today's reads.

### 2. Internal C# architecture

No new project or assembly. Repository inspection shows the existing split already fits: wire DTOs in `BimIt.Revit.Contracts`, one Bridge service interface and RPC pair in `BimIt.Revit.Bridge`, operations and the registry in `BimIt.Revit.Addin` beside `Query/` and `Capabilities/`, and two tool registrations in `BimIt.Revit.Server`. A separate operations assembly can be reconsidered only if operation count or multi-version build pressure later justifies it.

Conceptual boundaries (names are illustrative):

```text
Contracts   ReadOperationDescriptor            wire metadata: id, title, description, contract_version,
                                                context, effects, input_schema, output_schema, pagination,
                                                availability
            LookupReadOperationsRequest/Result
            ReadOperationRequest/Result         envelope; inputs and result as JSON elements
            per-operation typed DTOs            e.g. UserWorksetsInput / UserWorksetsResult

Bridge      IRevitReadOperationsService         LookupAsync, ReadAsync
            revit.lookup_read_operations         one RPC pair, gated by one protocol version
            revit.read_operation

Addin       IRevitReadOperation                 non-generic boundary used by the registry and dispatcher:
                                                  Descriptor
                                                  ExecuteAsync(envelope, dispatcher, context services)
            RevitReadOperation<TRequest,TResult> abstract typed base implementing IRevitReadOperation:
                                                  Bind(JsonElement) -> TRequest          closed, rejects unknown
                                                  Validate(TRequest)                     operation-specific bounds
                                                  Execute(ReadOperationContext, TRequest) -> TResult
                                                                                         runs on the Revit thread
                                                  ValidateResult(TResult)                operation-specific checks
            ReadOperationRegistry               immutable, built once from an explicit list
            ReadOperationDispatcher             id + version lookup, document guard, cursor/revision checks,
                                                EXEC-0001 enqueue, size budget, error mapping

Server      revit_lookup, revit_read            envelope schemas, instance routing, timeout, error mapping.
                                                No operation descriptors, no per-operation code.
```

Rules:

- **Explicit registration.** The Addin composition root (where today's capability services are composed) passes one explicit, reviewed list of operation instances to `ReadOperationRegistry`. Adding an operation is a code change to that list, visible in review.
- **Startup validation.** The registry rejects, at construction: malformed ids (proposed grammar: lowercase dot-separated segments of `[a-z][a-z0-9_]*`, at least two segments), duplicate `(id, contract_version)` pairs, contract version below 1, effects other than `read`, missing or unparsable schemas, an input schema that is not a closed object, and inconsistent pagination metadata. A registry that fails validation is not advertised: the Bridge capability for read operations stays off and both tools return the standard unsupported-version error. Unit tests run the same validation in CI, so this should never reach a user.
- **Typed inside, non-generic at the boundary.** `RevitReadOperation<TRequest,TResult>` keeps each operation strongly typed. The registry and dispatcher see only `IRevitReadOperation`, so dispatch needs no reflection and no generic type resolution at runtime.
- **Validation order.** Envelope (Server and Bridge) → operation lookup → version → document guard → `Bind` and `Validate` (before any Revit access) → cursor and revision checks → `Execute` inside one EXEC-0001 work item → `ValidateResult` → serialization through the shared UTF-8 contract path → result size budget (currently 120,000 characters, `RESULT_TOO_LARGE`). A result that fails `ValidateResult` is never returned in part.
- **Schemas match types.** Each operation's declared JSON Schemas are hand-authored like today's `…JsonSchemas` classes, and a contract test proves that the typed binder accepts and rejects exactly what the schema declares and that sample results validate against the output schema. Whether to generate schemas from the DTOs instead is an implementation choice, not an execution path.
- **All Revit access through EXEC-0001.** `Execute` runs only inside the dispatcher's work item. `ReadOperationContext` gives the operation the active `Document` for reading and the existing identity, reference, and revision services. It does not give `UIApplication`.
- **Shared kernel building blocks.** Cursor-paginated operations reuse ADR-0023's `DeclarativeQueryCursorCodec`, `DocumentQueryRevisionService`, ordering, and completeness rules. The cursor fingerprint covers the operation id, contract version, `document_id`, and normalized inputs.

### 3. The Addin is authoritative; nothing is duplicated in the Server

The Server holds no operation list and no operation schemas. `revit_lookup` forwards to the selected instance, and that instance answers from its own registry for its own Revit version. Consequence: `revit_lookup` needs a reachable Revit instance. pyRevit's `list_automation` works without Revit. BIM-IT gives that up deliberately so that a lookup never describes an operation the connected host does not have.

### 4. Security invariant

The registry is closed and trusted. Every operation is:

- compiled with BIM-IT;
- explicitly registered in the one registration list;
- reviewed and tested like any other capability;
- versioned (stable id plus contract version);
- bounded in input (sizes, counts, string lengths), output (page size, character budget), and execution (EXEC-0001 timeout);
- read-only: it opens no `Transaction`, `SubTransaction`, or `TransactionGroup` and calls no API that modifies the document;
- transaction-free, unless an exceptional read-only Revit presentation mechanism is explicitly decided by a separate ADR.

Executable operations are **not** discovered by reflection over assemblies, by attribute scanning, by naming convention, or by loading external code. Reflection may later serve informational API lookup (in the sense of pyRevit's `lookup_revit_api`), but never as the execution selector under this ADR.

As in ADR-0023 §8, BIM-IT claims no .NET sandbox. Compiled operations run in the Revit process with the user's permissions. Safety comes from the closed registry, the closed request vocabulary, and review of trusted code. `readOnlyHint` describes the design; it does not create it.

**Skills cannot register an operation, grant access, or widen an operation's effects.** Skill content is never executed (ADR-0022). A Skill names operation ids; the registry decides whether they exist.

### 5. No generic write

Controlled writes remain outside this registry. **This ADR authorizes no generic `revit_write`** and no write, preview, or approval operation inside the registry. The `effects` field exists so that metadata is explicit, but in v1 its only legal value is `read`, and startup validation enforces that. One MCP tool carries one set of annotations, so a registry that mixed reads and writes behind `revit_read` would make its `readOnlyHint` false or dishonest. Writes stay on the ADR-0008 to ADR-0011 path: preview, trusted local approval, apply, audit.

### 6. Decision rule: registered operation or dedicated MCP tool

**Default to a registered compiled read operation** for a bounded, deterministic, factual read where the main variation from existing reads is the data source. Examples: project metadata and inventories such as worksets, phases, design options, levels, view templates, schedule definitions.

**Keep or create a dedicated semantic MCP tool** when the capability has at least one of:

- substantial domain semantics that the tool's own description must teach (pattern resolution, placement);
- a unique interaction pattern (selection, review, approval, multi-step chaining);
- geometry, topology, or spatial reasoning (`revit_read_spatial_rooms`, `revit_get_mep_topology`, `revit_get_room_layouts`);
- special identity or chaining requirements that other tools consume directly;
- a contract important enough to deserve first-class agent visibility and per-tool client permissions.

Any write, preview, or approval capability is a dedicated tool under ADR-0008. Choosing a dedicated tool for a pure inventory read needs a short written reason in its CAP.

### 7. Compatibility and versioning

- **Stable operation ids.** An id is never renamed or reused for a different meaning. The namespace prefix (`project.`, …) is part of the id, not a routing hint.
- **Contract versions.** Each operation carries an integer contract version, starting at 1. A breaking change publishes a new version under the same id. Breaking means: removing or renaming an output field, changing a field's type, meaning, or units, changing ordering or counting semantics, making an optional input required, or narrowing accepted inputs. Adding an optional input whose absence keeps the previous behaviour, or adding an optional output field that changes no existing field, is non-breaking.
- **Several versions at once.** A host may register more than one contract version of an operation during a deprecation window. A request may pin `contract_version`. The response always states the version that ran.
- **Revit-version availability.** The multi-version build (ADR-0004) decides which operations compile and register for each Revit version. An operation the BIM-IT build knows but that cannot run on this host is listed with `available: false` and a stable reason in exact lookup and returns `READ_OPERATION_UNAVAILABLE` from `revit_read`. An id the host has never heard of returns `UNKNOWN_READ_OPERATION`. A retired operation returns `READ_OPERATION_UNAVAILABLE` with reason `retired`.
- **Bridge negotiation.** One new Bridge protocol version (expected to be 24, following `QueryParameterBindingsVersion = 23`) gates the lookup and read RPC pair, following the existing pattern: a host advertises the version only when it composes the service. Individual operations do **not** consume protocol versions. Their availability is negotiated through lookup. A host without the version is refused with the existing unsupported-version error. No fallback pretends an older host has the registry.
- **Document context guard.** In v1 every operation has context `document`. `revit_read` requires `document_id` from `revit_get_context` and compares it with ordinal equality to the active document before any cursor or Revit access (`DOCUMENT_CONTEXT_CHANGED`). `NO_ACTIVE_DOCUMENT` and `UNSUPPORTED_DOCUMENT_KIND` keep their current meaning. Other contexts (application-level facts, linked documents) need a later decision.
- **Deterministic errors.** Existing codes are reused where the meaning is the same: `NO_ACTIVE_DOCUMENT`, `DOCUMENT_CONTEXT_CHANGED`, `UNSUPPORTED_DOCUMENT_KIND`, `INVALID_CURSOR`, `STALE_CURSOR`, `RESULT_TOO_LARGE`, `REVIT_EXECUTION_TIMEOUT`, `REVIT_EXECUTION_FAILED`. Proposed new codes: `UNKNOWN_READ_OPERATION`, `READ_OPERATION_UNAVAILABLE`, `READ_OPERATION_VERSION_UNSUPPORTED`, `INVALID_READ_OPERATION_INPUT` (naming the accepted inputs and the failing constraint), and `READ_OPERATION_RESULT_INVALID` (output validation failed; no data returned). Final names are fixed in the specifications. Errors stay compact and contain no stack traces, paths, or Revit exception text (ADR-0005 §7).
- **No internal numeric ids.** A generic registry is not a reason to expose `ElementId`, `WorksetId`, or other internal Revit integers. Where an operation needs cross-call identity it returns ADR-0006 opaque references, meaningful only with the matching `document_id`.

### 8. Relationship to ADR-0022 (Skills)

ADR-0022 remains valid and unchanged. Skills provide orchestration and business guidance. A Skill may tell an agent to:

1. call `revit_lookup` to find the appropriate operation;
2. call `revit_read`;
3. interpret the structured result using firm or project knowledge.

A Skill gains no Revit API access of its own, cannot add or alter operations, and cannot widen effects. Skills should name the operation ids and, where it matters, the contract versions they rely on, so a Skill states its dependency precisely.

The ADR-0022 Assistant read facade needs no change: `revit_lookup` and `revit_read` meet its existing exposure criteria (`revit_` prefix, `readOnlyHint` true, `destructiveHint` not true, `openWorldHint` false, object input schema, accepts `instance_id`), and `revit_read`'s `document_id` is injected like any other read. The model-facing read surface grows by two tools, once.

### 9. Relationship to ADR-0023 (declarative read-query architecture)

This ADR **extends** ADR-0023 and does not supersede it.

- Kept unchanged: §1 no arbitrary code execution; §2 "the LLM decides what data it wants, BIM-IT code decides how the Revit API obtains it"; §3 semantic capabilities preserved; §6 pagination and completeness invariants; §7 structured output; §8 safety boundaries. Registered operations inherit all of them.
- Refined: §4 chose "one source-specific MCP tool per coherent source" and said a later ADR may unify sources. This ADR is that later decision in a specific form. It does **not** adopt ADR-0023's rejected Option B (a broad query language): a registered operation is still a narrow, source-specific contract with its own closed inputs and output schema. What changes is the default *exposure* for new bounded factual reads: a registered operation behind `revit_lookup` / `revit_read`, instead of a new MCP tool.
- §9's per-capability Bridge gate is kept for dedicated tools. Registered operations share one gate (§7 above).

### 10. Existing source-specific tools

`revit_query_family_types` (CAP-0014) and `revit_query_parameter_bindings` (CAP-0015) are not removed or changed. They remain accepted public contracts. They are **not** duplicated as registered operations during the first implementation merely to prove the registry. A future migration or adaptation into the registry may be considered separately, and must preserve backward compatibility of the existing tools. The same applies to `revit_query_elements` and `revit_get_mep_device_types`.

### 11. First demonstrated operation: `project.user_worksets`

The first implementation candidate is the operation `project.user_worksets`: an inventory of the user-created worksets in the active host project.

Its architectural role:

- it is the first and only operation in the first slice, and it proves the full path: registration, startup validation, `revit_lookup` discovery, `revit_read` execution, the Bridge gate, the document guard, the Assistant facade, and the error model;
- it is a clear "default to registered operation" case under §6: bounded, factual, read-only, transaction-free, no geometry;
- once implemented and live-validated, it should be enough to replace the external Electrical Standards Skill's `missing_fact` for worksets. That Skill change is a separate step for the Skill's owner. This ADR does not modify the Skill;
- its gate should follow ADR-0023's model: an isolated MCP client that can reach only `BimIt.Revit.Server`, with a BIM task prompt and no schema coaching, finds the operation through `revit_lookup` and reads the inventory through `revit_read` without code and without modifying the model, matched against an independent baseline on the same document state.

This ADR does not decide the operation's fields, the behaviour on a non-workshared project (an empty deterministic result or a deterministic error), whether element counts per workset belong in it, or whether it paginates. Those belong to its CAP. It does not implement the operation.

## Out of scope

- arbitrary C# execution;
- arbitrary Python execution;
- raw Revit API invocation;
- generic reflection execution;
- generic writes;
- migration of existing source-specific tools;
- external plugin code loading;
- runtime installation of executable operations by Skills;
- a universal Revit query language;
- implementing the registry or `project.user_worksets` in this task.

## Consequences

### Positive

- New factual reads stop growing `tools/list`, the Bridge protocol number, and the Server file set. Each costs one compiled operation, its tests, and a short contract.
- BIM-IT gains pyRevit-like discoverability and extensibility without model-written code in Revit.
- The Addin remains the single authority, per Revit version, on what operations exist.
- Skills can depend on stable operation ids and versioned output schemas, which pyRevit's markers do not provide.
- The Assistant gets every new operation automatically through two tools it already discovers.

### Negative / costs

- Per-operation static typing is lost at the MCP level. MCP clients validate only the envelope. Correctness of inputs and outputs relies on BIM-IT's runtime validation and contract tests.
- Agents need an extra `revit_lookup` round trip before the first use of an operation, unless a Skill names the inputs.
- Operations are not individually visible or permissionable in MCP hosts.
- `revit_lookup` requires a running Revit instance.
- Two exposure styles coexist: dedicated tools and registered operations. The decision rule must be applied consistently, and some facts may be reachable both ways if existing tools are later adapted.
- The registry, validation, and versioning machinery need careful first implementation and tests before they pay off.

## Unresolved questions for the Product Owner and Tech Lead

1. **Tool names.** `revit_lookup` / `revit_read` (short, as proposed) or more explicit names such as `revit_lookup_read_operations` / `revit_run_read_operation`. The `revit_` prefix is required by the Assistant facade.
2. **Version pinning.** Is `contract_version` optional (latest when omitted) or required? Should Skills be required to pin?
3. **Unavailable operations in search.** Should search results include operations unavailable on this host, or only exact lookup?
4. **Registry validation failure.** Disable the whole registry (proposed, fail closed) or drop only the invalid operation?
5. **Output validation depth.** Typed `ValidateResult` plus contract tests (proposed), or a JSON Schema validator at runtime in the Addin, which adds a dependency to the Revit process?
6. **Paging placement.** Paging fields in the envelope (proposed, uniform cursor model) or inside each operation's inputs?
7. **Lookup search semantics and bounds.** Matching rules (ids, titles, descriptions, tags), page size, and the maximum descriptor size returned in one call.
8. **Promotion path.** If an operation later needs first-class visibility, is it promoted to a hand-written dedicated tool, or may the Server generate one from the descriptor (a narrow form of Option B)?
9. **Migration of CAP-0014 and CAP-0015.** Whether, when, and how they become registered operations with their tools kept as compatible facades.
10. **Specification set.** Whether the mechanism gets one CAP plus BRIDGE and SERVER specs, and whether each later operation needs a full CAP or a lighter operation contract document.
11. **Workset operation scope.** Fields, non-workshared behaviour, counts, and pagination for `project.user_worksets`, to be decided in its CAP.

## References

- [AGENTS.md](../../AGENTS.md)
- [ADR-0001](ADR-0001-out-of-process-mcp-and-revit-capability-host.md), [ADR-0004](ADR-0004-solution-structure-and-multi-version-build.md)
- [ADR-0005](ADR-0005-agent-context-and-token-efficiency.md): bounded results, concise tool surfaces, deterministic errors
- [ADR-0006](ADR-0006-document-and-element-reference-identity.md): opaque identity
- [ADR-0007](ADR-0007-federated-mcp-boundaries-and-orchestration.md): tool-surface scoping, dynamic tool lists deferred
- [ADR-0008](ADR-0008-controlled-write-safety-model.md) to [ADR-0011](ADR-0011-v1-controlled-write-audit.md): controlled writes
- [ADR-0014](ADR-0014-provider-neutral-assistant-agent-runtime.md): provider-neutral Assistant
- [ADR-0022](ADR-0022-agent-skills-and-knowledge-architecture.md): Agent Skills, Assistant read facade
- [ADR-0023](ADR-0023-declarative-revit-read-query-architecture.md): declarative read-query architecture
- [CAP-0014](../capabilities/CAP-0014-revit-query-family-types.md), [CAP-0015](../capabilities/CAP-0015-revit-query-parameter-bindings.md)
- [EXEC-0001](../execution/EXEC-0001-revit-execution-queue.md): Revit execution queue
- [ELECTRICAL_STANDARDS_SKILL_V0_1.md](../ELECTRICAL_STANDARDS_SKILL_V0_1.md): worksets `missing_fact`
- [TECH_WATCH.md](../TECH_WATCH.md)
- pyRevit `develop` at `615530d24632033875e6fedeffa0e57adfeec60f` (inspected 2026-11-10): `dev/pyRevitLabs/pyRevitCLI/PyRevitMcpServer.cs`, `dev/pyRevitLabs/pyRevitCLI/PyRevitAutomationOperations.cs`, `pyrevitlib/pyrevit_automation.py`, `pyrevitlib/pyrevit/agent/skills/`
