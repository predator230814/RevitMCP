# ADR-0024: Registry-driven compiled Revit read operations

- Status: Accepted
- Date: 2026-11-10
- Revised: 2026-11-10, after Tech Lead review (overall architecture approved; decisions on schema validation, lookup, versioning, registry failure, paging, and promotion recorded below)
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

BIM-IT adopts the useful part: a registry, discovery by stable identifier, one generic execution surface, and Skills that point at operations. BIM-IT does **not** adopt generic code execution (`run_query`, `run_modify`), and its registry differs from pyRevit's in ways that matter for BIM-IT's product goal:

| | pyRevit automation registry | BIM-IT read-operation registry (this ADR) |
|---|---|---|
| Membership | Any marked, eligible function in pyrevitlib or rpw, found by scanning source text, plus hand-wired entries | Only operations compiled into BIM-IT and listed explicitly in one registration list. No scanning, no reflection discovery |
| Output | Whatever the function returns. No declared output schema | A declared, versioned output schema per operation, enforced at runtime before the result leaves Revit |
| Identity | `element` inputs are raw element id integers | No raw Revit numeric ids. Opaque references under ADR-0006 where identity is needed |
| Model-written code | Also offered (`run_query`, `run_modify`) | Never offered (ADR-0023 §1) |

pyRevit is a valid scripting runtime for a trusted power user. This comparison records a design difference, not a judgement.

## Decision drivers

- Avoid one MCP tool per Revit data source. Keep the visible MCP surface small (ADR-0005, ADR-0007 §4).
- Keep every ADR-0023 invariant: no model-supplied code, closed request vocabulary, trusted compiled read operations, bounded results, deterministic errors, structured content authoritative.
- The Addin, running inside a specific Revit version, is the only authority on which operations exist (ADR-0001, ADR-0004). Descriptors are not duplicated in the Server.
- Declared operation schemas and the compiled operations must not be able to drift silently.
- Adding a new factual read should cost one compiled operation, its tests, and a short contract, not a new tool, a new Bridge method, and a new protocol version.
- Same behaviour for external MCP clients and BIM-IT Assistant, provider-neutral (ADR-0014, ADR-0022).
- Skills orchestrate. They never grant access (ADR-0022).
- Read and write stay clearly separated (AGENTS.md, ADR-0008).
- Opaque identity, never internal Revit numeric ids (ADR-0006).

## Options considered

All three options keep ADR-0023 §1: no model-written code runs. A generic code-execution surface (ADR-0023 Option A, pyRevit's `run_query`) was rejected there and is not reconsidered.

### Option A: Generic `revit_lookup` + `revit_read` over a closed registry, with per-operation schemas enforced at runtime

Two MCP tools. `revit_lookup` returns registered operation metadata, including each operation's input and output JSON Schemas. `revit_read` executes one registered operation by stable id and contract version with an `inputs` object. The Addin validates inputs and outputs against the declared schemas at runtime, in addition to typed semantic validation.

- Advantages:
  - The MCP surface grows by two tools once, then stays fixed however many operations are added.
  - One Bridge method pair and one protocol version gate the mechanism. Later operations need no protocol bump.
  - The Server stays a thin router. The running Addin is the single source of operation metadata.
  - `revit_read` has one honest set of annotations (`readOnlyHint: true`, `openWorldHint: false`), so ADR-0022's Assistant read facade discovers it with no Assistant change.
  - Operation metadata is loaded only when needed (progressive disclosure), like Skills.
- Disadvantages, stated plainly:
  - **Loss of MCP-level static typing per operation.** MCP advertises one static `inputSchema` and `outputSchema` per tool. `revit_read`'s schemas can only describe the envelope: `operation` is a string with no enum (the set depends on the connected host), `inputs` is an open `object`, and the result payload is an `object`. A client that validates arguments or structured output against `tools/list` schemas validates only the envelope. Clients that generate typed bindings from `tools/list` get no per-operation types.
  - **The agent must look before it reads.** The precise contract and the required `contract_version` are one `revit_lookup` call away, an extra round trip compared with a dedicated tool whose schema is already in context. An agent that guesses inputs gets a runtime validation error instead of being steered by the schema before the call.
  - **Less first-class visibility.** An operation is not visible in `tools/list`. Hosts that show, allow-list, or approve tools by name see `revit_read`, not `project.user_worksets`. Per-operation permissions in such clients are not possible.
  - **Errors carry more of the contract.** Validation errors must be precise enough to steer the agent (accepted input names, the failing constraint), as pyRevit's `run_automation` already does.
  - Runtime validation is BIM-IT's responsibility on both sides: inputs before execution, outputs before return.
- Mitigations, which reduce but do not remove the loss: exact lookup returns full JSON Schemas; Skills name the operation ids and contract versions they rely on; the `revit_read` description tells the agent to call `revit_lookup` first; errors name the accepted inputs; the declared schemas are enforced at runtime on every call (§2), so MCP clients lose static typing but BIM-IT does not lose enforcement.

### Option B: Expose each registered operation dynamically as its own MCP tool

The Server builds one MCP tool per registered operation, with that operation's precise input and output schemas, from the Addin registry.

- Advantages: full MCP-level static typing per operation; every operation is first-class in `tools/list`; per-tool client permissions work; no extra lookup round trip.
- Disadvantages:
  - It rebuilds the per-source tool surface the Product Owner wants to avoid. Fifty operations become fifty tools in every client's context, which is the ADR-0005 cost this ADR exists to stop. Only the authoring cost is removed.
  - `tools/list` would depend on which Revit instance is connected. The Server routes by `instance_id` across several Revit processes and versions, so the list would be a union with per-instance holes, or would change at runtime. That needs `notifications/tools/list_changed`, whose client support is uneven, and conflicts with ADR-0007 §4's requirement of deterministic discoverability. The Server could not answer `tools/list` before any Revit instance is reachable.
  - Tool annotations, names, and descriptions become generated text. Review of the agent-facing contract moves from source to runtime output.
  - The ADR-0022 Assistant facade would see a changing tool set on each turn.

ADR-0007 §4 lists dynamic tool-list exposure as a mechanism to evaluate later. This ADR does not adopt it, and v1 generates no MCP tools from descriptors (§6).

### Option C: Continue source-specific MCP tools (ADR-0023 §4 as written)

- Advantages: proven twice; full static typing; first-class visibility; no new mechanism.
- Disadvantages: linear growth of tools, Bridge methods, protocol versions, Server files, and specifications per fact. It is the path to rebuilding the Revit API as MCP tools. It also makes simple facts expensive enough that they get deferred, which is how the workset `missing_fact` arose.

### Chosen direction

**Option A.** It keeps the visible MCP surface small and fixed and the Addin authoritative, at the cost of per-operation static typing in MCP. That cost is accepted deliberately, is offset by runtime schema enforcement, lookup, Skills, and precise errors, and is bounded by the decision rule below: a contract that needs first-class visibility keeps a dedicated tool.

## Decision (proposed)

### 1. Two MCP tools over a closed registry of compiled read operations

BIM-IT introduces a registry of trusted compiled C# read operations hosted by the Addin, and two MCP tools in `BimIt.Revit.Server`, named **`revit_lookup`** and **`revit_read`**.

**`revit_lookup`** discovers or describes BIM-IT registered read operations. It is **not** a Revit API lookup: it never searches or describes Revit API types, members, or assemblies. It has two modes, decided in v1:

- **Search mode** (simple, deterministic discovery; no fuzzy search, tags, or ranking):
  - optional `query`, at most 256 characters;
  - matching is case-insensitive ordinal substring over the operation id, title, and description; an absent query matches every registered operation;
  - results are ordered by operation id, ordinal, then contract version;
  - default page size 25, maximum 50, with deterministic paging over that order;
  - results return **summaries only**: id, contract version, title, description, context, effects, pagination capability. No schemas.
- **Exact mode**: one operation id, optionally with a contract version. Returns the **full descriptor** of each matching registered version:
  - stable operation id (for example `project.user_worksets`);
  - title and description;
  - contract version;
  - context requirement (v1: `document`, the active project document);
  - effects (v1: always `read`; see §5);
  - input schema (JSON Schema, closed recursively: `additionalProperties: false` on every object node, §2);
  - output schema (JSON Schema of the result payload, closed recursively the same way);
  - pagination capability (`none` or `cursor`).
- An id, or an id and version, that is not registered on the connected host returns a deterministic error (§7). Lookup never describes an operation that is not registered on the connected host.

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

`document_id`, `operation`, `contract_version`, and `inputs` are required. **There is no implicit "latest" execution.** An agent that does not know the contract version calls `revit_lookup` first.

Conceptual result (structured content, authoritative):

```json
{
  "operation": "project.user_worksets",
  "contract_version": 1,
  "document_id": "…",
  "result": { "…": "operation-specific, valid against the declared output schema" },
  "complete": true,
  "next_cursor": null
}
```

Field names in the envelope are illustrative. The exact envelope is fixed by the specifications that implement this ADR.

**Paging is part of the generic envelope, not of operation inputs.** The common paging fields (`page_size`, `cursor` in the request; `complete`, `next_cursor` in the result) live in the `revit_read` envelope with one meaning for every operation. Only an operation that declares `cursor` pagination accepts them. A paging argument sent to an operation that declares `none` is rejected deterministically (`INVALID_READ_OPERATION_INPUT`, naming the paging field). Operations do not define their own paging fields in `inputs`.

The model or client **cannot** provide C#, Python, Dynamo, expressions, .NET type names, Revit API type or member names for execution, reflection targets, or arbitrary methods or callables. `operation` and `contract_version` are matched against the registry by ordinal and numeric equality and select nothing else. **Only an operation id and version registered by BIM-IT on that host can execute.** Unknown envelope fields and unknown `inputs` properties are rejected, never ignored.

Both tools are `readOnlyHint: true`, `destructiveHint` absent, `openWorldHint: false`, accept `instance_id`, and declare an output schema for their envelope (ADR-0005 §3). `revit_read` takes `document_id`, so the ADR-0022 Assistant facade injects the bound instance and the active document exactly as it does for today's reads.

### 2. Internal C# architecture

No new project or assembly is prescribed. Repository inspection shows the existing split already fits: wire DTOs in `BimIt.Revit.Contracts`, one Bridge service interface and RPC pair in `BimIt.Revit.Bridge`, operations and the registry in `BimIt.Revit.Addin` beside `Query/` and `Capabilities/`, and two tool registrations in `BimIt.Revit.Server`.

Conceptual boundaries (names are illustrative):

```text
Contracts   ReadOperationDescriptor            wire metadata: id, title, description, contract_version,
                                                context, effects, input_schema, output_schema, pagination
            LookupReadOperationsRequest/Result
            ReadOperationRequest/Result         envelope; inputs and result as JSON elements
            per-operation typed DTOs            e.g. the request/result types of project.user_worksets v1

Bridge      IRevitReadOperationsService         LookupAsync, ReadAsync
            revit.lookup_read_operations         one RPC pair, gated by one protocol version
            revit.read_operation

Addin       IRevitReadOperation                 non-generic boundary used by the registry and dispatcher
            RevitReadOperation<TRequest,TResult> abstract typed base implementing IRevitReadOperation
            ReadOperationRegistry               immutable, built once from an explicit list
            ReadOperationDispatcher             runs the pipeline below

Server      revit_lookup, revit_read            envelope schemas, instance routing, timeout, error mapping.
                                                No operation descriptors, no per-operation code.
```

**Declared schemas are executable contracts.** An operation's `input_schema` and `output_schema` are not documentation. They are enforced on every call. The required `revit_read` pipeline in the Addin is:

1. resolve the registered `(operation, contract_version)` on this host, check paging arguments against the declared pagination capability, and apply the document guard (§7);
2. **validate the JSON `inputs` against `input_schema`**;
3. bind (deserialize) `inputs` to `TRequest`;
4. run operation-specific semantic validation on `TRequest` (before any Revit access);
5. check the cursor and document revision, for cursor-paginated operations;
6. **execute** the operation inside one EXEC-0001 work item;
7. run typed semantic validation on `TResult`;
8. serialize `TResult` to the generic JSON payload through the shared UTF-8 contract path;
9. **validate that JSON against `output_schema`**;
10. apply the result size budget (currently 120,000 characters, `RESULT_TOO_LARGE`);
11. only then return the result.

A failure at steps 2 to 4 returns `INVALID_READ_OPERATION_INPUT` and never reaches Revit. A failure at step 7 or 9 returns `READ_OPERATION_RESULT_INVALID` and no data. No partial result is ever returned.

**Schema validator.** Runtime validation uses the deterministic BIM-IT JSON Schema subset that the Server already enforces for tool arguments in `ClosedSchemaArgumentValidator` (today: `type`, including type arrays; `enum`; `const`; `properties`; `required`; `additionalProperties`; `items`; `minLength` / `maxLength`; `minimum` / `maximum`; `minItems` / `maxItems`; `uniqueItems`). The implementation should reuse or extract that subset into a location both the Addin and the Server can reference, rather than adding a third-party or full JSON Schema library, which would be a new runtime dependency inside the Revit process for this feature alone. The exact shared class and project location are implementation details.

**Schema keyword handling.** The shared validator supports a deliberate subset, and today it silently ignores keywords outside it. For operation contracts that is not acceptable, so **registry startup validation rejects any operation schema that uses a validation or structural keyword outside the supported subset**, instead of ignoring it. A declared constraint can therefore never be silently unenforced. Annotation-only keywords that do not affect validation, such as `description`, may be explicitly allow-listed by the shared subset. `oneOf`, `anyOf`, `allOf`, `$ref`, `pattern`, `format`, and other JSON Schema features are not introduced by this ADR. They may be added later only as reviewed extensions to the shared validator, with tests. The first registry operation uses only the supported deterministic subset.

**Operation schemas are closed recursively.** Every object-shaped operation contract is closed. Registry startup validation requires `additionalProperties: false` on **every object schema node that declares `properties`**, recursively across **both** `input_schema` and `output_schema`, including:

- the root object;
- nested objects at any depth;
- object schemas used as `items` of arrays;
- and, if composition keywords are added to the subset in the future, object schemas inside their branches.

A node typed `object` that declares no `properties` is also an object-shaped contract. It must carry `additionalProperties: false` too (for example an operation whose `inputs` is the empty object `{}`), so the closure invariant cannot be bypassed by omitting `properties`. The rule applies only to object schema nodes. Schema nodes that are not objects (strings, numbers, integers, booleans, arrays themselves) do not carry `additionalProperties`. An operation schema that contains an open object contract fails registry startup validation, and therefore disables the whole registry on that host under the fail-closed rule below.

The reasons:

- **Undeclared input properties must never be accepted.** A closed input contract is what makes the request vocabulary closed (ADR-0023 §8) at every depth, not only at the root.
- **Undeclared output properties must never leave Revit.** Output-schema validation at step 9 only guarantees the declared shape if no object in that shape is open. An open nested object would let any field a compiled result happens to carry reach the client unchecked.
- **A compiled DTO and its declared schema must not drift silently.** With every object closed, a result field that the schema does not declare fails `READ_OPERATION_RESULT_INVALID` at runtime and fails the contract tests in CI.
- **Adding an output field is a deliberate schema change.** Even when the addition is contract-version-compatible (§7), it requires updating the declared `output_schema` in the same reviewed change. A DTO change alone never widens what leaves Revit.

**Contract tests remain required** for every operation: the typed binder and the input schema agree on accepted and rejected inputs, and representative results validate against the output schema. They catch drift in CI. They are not the only guarantee: runtime validation at steps 2 and 9 means a schema and an operation that have drifted fail closed at runtime instead of returning unchecked data.

Other rules:

- **Explicit registration.** The Addin composition root (where today's capability services are composed) passes one explicit, reviewed list of operation instances to `ReadOperationRegistry`. Adding an operation is a code change to that list, visible in review.
- **Startup validation, fail closed.** The registry validates every registration at construction: id grammar (proposed: lowercase dot-separated segments of `[a-z][a-z0-9_]*`, at least two segments), unique `(id, contract_version)` pairs, contract version of at least 1, effects equal to `read`, present and parsable schemas, an object root for `input_schema`, `additionalProperties: false` on every object schema node that declares `properties` in both schemas (recursive closure, above), schemas limited to the supported subset plus allow-listed annotations, and consistent pagination metadata. **Any startup validation failure disables the whole read-operation registry and capability for that host.** The Bridge capability is not advertised, and `revit_lookup` and `revit_read` return the standard unsupported-version error for that host. Invalid operations are never dropped one by one while the rest keep running. CI and unit tests run the same validation over the real registration list, so this is expected never to reach production.
- **Typed inside, non-generic at the boundary.** `RevitReadOperation<TRequest,TResult>` keeps each operation strongly typed. The registry and dispatcher see only `IRevitReadOperation`, so dispatch needs no reflection and no generic type resolution at runtime.
- **All Revit access through EXEC-0001.** Operation code runs only inside the dispatcher's work item. The operation receives the active `Document` for reading and the existing identity, reference, and revision services. It does not receive `UIApplication`.
- **Shared kernel building blocks.** Cursor-paginated operations reuse ADR-0023's `DeclarativeQueryCursorCodec`, `DocumentQueryRevisionService`, ordering, and completeness rules. The cursor fingerprint covers the operation id, contract version, `document_id`, and normalized inputs.

### 3. The Addin registry is authoritative; nothing is duplicated in the Server

The Server holds no operation list, no operation schemas, and no catalogue of operations known to other builds or Revit versions. It validates only the generic envelopes, routes to the selected instance, and maps errors.

**`revit_lookup` execution context.** `revit_lookup` reads only the immutable in-memory registry of the selected host. It requires a reachable, selected Revit instance, because that host owns the registry. It does **not** require an active document, does **not** go through EXEC-0001, and does **not** call the Revit API. `revit_read` is the only path that applies document guards and executes an operation through EXEC-0001.

The consequence is accepted deliberately: pyRevit's `list_automation` works without Revit, but BIM-IT lookup never describes an operation the connected host does not actually have.

### 4. Security invariant

The registry is closed and trusted. Every operation is:

- compiled with BIM-IT;
- explicitly registered in the one registration list;
- reviewed and tested like any other capability;
- versioned (stable id plus contract version);
- bounded in input (schema-enforced sizes, counts, string lengths), output (page size, character budget), and execution (EXEC-0001 timeout);
- read-only: it opens no `Transaction`, `SubTransaction`, or `TransactionGroup` and calls no API that modifies the document;
- transaction-free, unless an exceptional read-only Revit presentation mechanism is explicitly decided by a separate ADR.

Executable operations are **not** discovered by reflection over assemblies, by attribute scanning, by naming convention, or by loading external code. Reflection may later serve informational API lookup (in the sense of pyRevit's `lookup_revit_api`), but never as the execution selector under this ADR, and never through `revit_lookup`.

As in ADR-0023 §8, BIM-IT claims no .NET sandbox. Compiled operations run in the Revit process with the user's permissions. Safety comes from the closed registry, the closed request vocabulary, runtime schema enforcement, and review of trusted code. `readOnlyHint` describes the design; it does not create it.

**Skills cannot register an operation, grant access, or widen an operation's effects.** Skill content is never executed (ADR-0022). A Skill names operation ids and versions; the host's registry decides whether they exist.

### 5. No generic write

Controlled writes remain outside this registry. **This ADR authorizes no generic `revit_write`** and no write, preview, or approval operation inside the registry. The `effects` field exists so that metadata is explicit, but in v1 its only legal value is `read`, and startup validation enforces it. One MCP tool carries one set of annotations, so a registry that mixed reads and writes behind `revit_read` would make its `readOnlyHint` false or dishonest. Writes stay on the ADR-0008 to ADR-0011 path: preview, trusted local approval, apply, audit.

### 6. Decision rule: registered operation or dedicated MCP tool

**Default to a registered compiled read operation** for a bounded, deterministic, factual read where the main variation from existing reads is the data source. Examples: project metadata and inventories such as worksets, phases, design options, levels, view templates, schedule definitions.

**Keep or create a dedicated semantic MCP tool** when the capability has at least one of:

- substantial domain semantics that the tool's own description must teach (pattern resolution, placement);
- a unique interaction pattern (selection, review, approval, multi-step chaining);
- geometry, topology, or spatial reasoning (`revit_read_spatial_rooms`, `revit_get_mep_topology`, `revit_get_room_layouts`);
- special identity or chaining requirements that other tools consume directly;
- a contract important enough to deserve first-class agent visibility and per-tool client permissions.

Any write, preview, or approval capability is a dedicated tool under ADR-0008. Choosing a dedicated tool for a pure inventory read needs a short written reason in its CAP.

**Promotion.** If a registered operation later deserves first-class MCP visibility, promoting it is an explicit architecture decision and CAP for a hand-written dedicated tool. v1 does not generate MCP tools from operation descriptors.

### 7. Compatibility and versioning

- **Stable operation ids.** An id is never renamed or reused for a different meaning. The namespace prefix (`project.`, …) is part of the id, not a routing hint.
- **Contract versions.** Each operation carries an integer contract version, starting at 1. A breaking change publishes a new version under the same id. Breaking means: removing or renaming an output field, changing a field's type, meaning, or units, changing ordering or counting semantics, making an optional input required, or narrowing accepted inputs. Adding an optional input whose absence keeps the previous behaviour, or adding an optional output field that changes no existing field, is non-breaking and keeps the version. Because operation schemas are closed recursively (§2), a non-breaking addition still requires updating the declared input or output schema in the same change.
- **Explicit version on every read.** `revit_read` requires `contract_version`. A host may register more than one version of an operation during a deprecation window. The response states the version that ran, which always equals the version requested.
- **Revit-version availability.** The multi-version build (ADR-0004) decides which operations compile and register for each Revit version. The connected host's registry is the whole truth for that host. There is no global catalogue of operations that are known to BIM-IT but unavailable on this host, and lookup lists no operation as unavailable merely because another build or Revit version supports it.
- **Bridge negotiation.** One new Bridge protocol version (expected to be 24, following `QueryParameterBindingsVersion = 23`) gates the lookup and read RPC pair, following the existing pattern: a host advertises the version only when it composes the service and its registry passed startup validation. Individual operations do **not** consume protocol versions. Their presence is discovered through lookup. A host without the version is refused with the existing unsupported-version error. No fallback pretends an older host has the registry.
- **Document context guard.** In v1 every operation has context `document`. `revit_read` requires `document_id` from `revit_get_context` and compares it with ordinal equality to the active document before any cursor or Revit access (`DOCUMENT_CONTEXT_CHANGED`). `NO_ACTIVE_DOCUMENT` and `UNSUPPORTED_DOCUMENT_KIND` keep their current meaning. Other contexts (application-level facts, linked documents) need a later decision.
- **Deterministic errors.** Existing codes are reused where the meaning is the same: `NO_ACTIVE_DOCUMENT`, `DOCUMENT_CONTEXT_CHANGED`, `UNSUPPORTED_DOCUMENT_KIND`, `INVALID_CURSOR`, `STALE_CURSOR`, `RESULT_TOO_LARGE`, `REVIT_EXECUTION_TIMEOUT`, `REVIT_EXECUTION_FAILED`. New v1 codes, with final spelling fixed in the specifications:
  - `UNKNOWN_READ_OPERATION`: no operation with this id is registered on the connected host;
  - `READ_OPERATION_VERSION_UNSUPPORTED`: the id is registered on this host, but not with the requested contract version; the error names the registered versions;
  - `INVALID_READ_OPERATION_INPUT`: `inputs` or paging arguments failed schema or semantic validation; the error names the accepted inputs and the failing constraint;
  - `READ_OPERATION_RESULT_INVALID`: the result failed typed or output-schema validation; no data is returned.

  `READ_OPERATION_UNAVAILABLE` is **not** part of v1. It may be introduced later only if a concrete case of a registered but conditionally unavailable operation is justified.

  Errors stay compact and contain no stack traces, paths, or Revit exception text (ADR-0005 §7).
- **No internal numeric ids.** A generic registry is not a reason to expose `ElementId`, `WorksetId`, or other internal Revit integers. Where an operation needs cross-call identity it returns ADR-0006 opaque references, meaningful only with the matching `document_id`.

### 8. Relationship to ADR-0022 (Skills)

ADR-0022 remains valid and unchanged. Skills provide orchestration and business guidance. A Skill may tell an agent to:

1. call `revit_lookup` to find the appropriate operation and its contract version;
2. call `revit_read` with that id and version;
3. interpret the structured result using firm or project knowledge.

A Skill gains no Revit API access of its own, cannot add or alter operations, and cannot widen effects. **Skills should name the operation id and contract version they depend on**, so a Skill states its dependency precisely and keeps working, or fails deterministically with `READ_OPERATION_VERSION_UNSUPPORTED`, when a host changes.

The ADR-0022 Assistant read facade needs no change: `revit_lookup` and `revit_read` meet its existing exposure criteria (`revit_` prefix, `readOnlyHint` true, `destructiveHint` not true, `openWorldHint` false, object input schema, accepts `instance_id`), and `revit_read`'s `document_id` is injected like any other read. The model-facing read surface grows by two tools, once.

### 9. Relationship to ADR-0023 (declarative read-query architecture)

This ADR **extends** ADR-0023 and does not supersede it.

- Kept unchanged: §1 no arbitrary code execution; §2 "the LLM decides what data it wants, BIM-IT code decides how the Revit API obtains it"; §3 semantic capabilities preserved; §6 pagination and completeness invariants; §7 structured output; §8 safety boundaries. Registered operations inherit all of them.
- Refined: §4 chose "one source-specific MCP tool per coherent source" and said a later ADR may unify sources. This ADR is that later decision in a specific form. It does **not** adopt ADR-0023's rejected Option B (a broad query language): a registered operation is still a narrow, source-specific contract with its own closed inputs and output schema. What changes is the default *exposure* for new bounded factual reads: a registered operation behind `revit_lookup` / `revit_read`, instead of a new MCP tool.
- §9's per-capability Bridge gate is kept for dedicated tools. Registered operations share one gate (§7 above).

### 10. Existing source-specific tools

`revit_query_family_types` (CAP-0014) and `revit_query_parameter_bindings` (CAP-0015) are not removed or changed. They remain accepted public contracts. They are **not** duplicated as registered operations during the first implementation. Migration into the registry is deferred until real evidence justifies it, and any migration must preserve the existing tools' backward compatibility. The same applies to `revit_query_elements` and `revit_get_mep_device_types`.

### 11. First operation: `project.user_worksets`, contract v1

The first operation is **`project.user_worksets`, contract version 1**: an inventory of the user-created worksets in the active host project. It is the first and only operation in the first slice and is used to prove the registry end to end: registration, startup validation, `revit_lookup` discovery, required contract version, runtime schema validation, `revit_read` execution through EXEC-0001, the Bridge gate, the document guard, the Assistant facade, and the error model.

Its exact contract (fields, non-workshared behaviour, counts, pagination) belongs to its implementation specification / CAP, not to this ADR. Once implemented and live-validated through an isolated natural-agent gate in the ADR-0023 style, it should be enough to replace the external Electrical Standards Skill's `missing_fact` for worksets. That Skill change is a separate step for the Skill's owner. This ADR does not modify the Skill and does not implement the operation.

## Out of scope

- arbitrary C# execution;
- arbitrary Python execution;
- raw Revit API invocation or lookup through `revit_lookup`;
- generic reflection execution;
- generic writes;
- dynamically generated MCP tools;
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
- Declared schemas are enforced at runtime, so schemas and compiled operations cannot drift silently.
- Skills can depend on stable operation ids and explicit contract versions with enforced output schemas, which pyRevit's markers do not provide.
- The Assistant gets every new operation automatically through two tools it already discovers.

### Negative / costs

- Per-operation static typing is lost at the MCP level. MCP clients validate only the envelope; BIM-IT enforces the operation schemas at runtime.
- Agents need a `revit_lookup` round trip before the first use of an operation, unless a Skill names the id, version, and inputs.
- Operations are not individually visible or permissionable in MCP hosts.
- `revit_lookup` requires a reachable Revit instance (but no active document).
- One invalid registration disables every registered operation on that host until fixed. CI is the defence.
- The shared schema subset must be extracted or shared between Server and Addin, and extended deliberately when an operation needs a keyword it lacks.
- Two exposure styles coexist: dedicated tools and registered operations. The decision rule must be applied consistently.

## Implementation-level questions (do not affect acceptance)

These are left to the implementing specifications and code review. None changes the architecture above.

1. **Shared validator location.** Which project hosts the extracted JSON Schema subset so that both the Addin and the Server reference it (for example `BimIt.Revit.Contracts` or a small shared internal source), and how the Server's existing `ClosedSchemaArgumentValidator` callers move to it.
2. **Specification set.** One CAP plus BRIDGE and SERVER specifications for the mechanism and `project.user_worksets` v1, and the template used for later operation contracts.
3. **Exact envelope spelling.** Final field names of the lookup and read envelopes and of the new error codes.
4. **Lookup paging token.** Whether search paging uses an offset or an opaque token, within the decided page sizes and ordering.

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
- `src/BimIt.Revit.Server/ClosedSchemaArgumentValidator.cs`: existing deterministic JSON Schema subset
- [ELECTRICAL_STANDARDS_SKILL_V0_1.md](../ELECTRICAL_STANDARDS_SKILL_V0_1.md): worksets `missing_fact`
- [TECH_WATCH.md](../TECH_WATCH.md)
- pyRevit `develop` at `615530d24632033875e6fedeffa0e57adfeec60f` (inspected 2026-11-10): `dev/pyRevitLabs/pyRevitCLI/PyRevitMcpServer.cs`, `dev/pyRevitLabs/pyRevitCLI/PyRevitAutomationOperations.cs`, `pyrevitlib/pyrevit_automation.py`, `pyrevitlib/pyrevit/agent/skills/`
