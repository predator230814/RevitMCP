# ADR-0023: Declarative Revit read-query architecture

- Status: Proposed
- Date: 2026-11-03
- Product Owner: Dave

## Context

ADR-0022 made Skills over generic capabilities the way to add workflows. A Skill can only be as good as the generic facts it can read. The first Standard Pack use case, a draft content inventory of a reference electrical project, showed where those facts run out.

Two live benchmarks ran on the same project on Revit 2026.5 (26.5.0.55), the active document `525413_E_EG_dave.otis`, with the same task, the same model, and an isolated Claude Code client that could reach only one Revit MCP each.

**BIM-IT Revit MCP** (typed read surface, 29 read calls):

- every call succeeded, and the results were structured and deterministic;
- it stayed read-only by construction, because write tools were not reachable;
- it produced a useful 32-entry electrical sample with categories, families, types, placement classification, and instance counts per category;
- it could not prove a complete inventory. `revit_get_mep_device_types` covers a fixed MEP placement allowlist, stops at 80 items with `truncated: true`, has no cursor, and returns no per-type instance count and no family-level metadata. The client saw 89 distinct families. `level_name` was null on every device instance.

**pyRevit MCP** (generic `run_query`, 14 calls, 10 of them `run_query`):

- found 196 distinct families, 1,205 types, 254 placed types, and 8,278 placed instances in the nine target electrical categories, with 87 used families and 109 loaded but unused;
- joined types to instance counts and aggregated beside the Revit data, so only summaries crossed the wire;
- per-category instance counts matched the BIM-IT benchmark exactly, so both observed the same document.

The same run also showed costs:

- 5 of 10 `run_query` attempts failed. Causes: `ElementId.IntegerValue` removed in Revit 2026, `DB.Element.Name.GetValue` valid on IronPython but not on CPython, IronPython JSON serialization failing on accented French names (`É`, 0xC9) after the script had finished, and a list comprehension that could not see a loop variable in the script scope;
- each script was roughly 40 to 70 lines, and every run invented its own result keys, so a result can only be reproduced by keeping the script text;
- the script runs inside Revit with the Windows user's permissions. Model changes are rolled back; filesystem, network, and process access are governed by policy, not by construction.

That is a coherent product choice for a scripting runtime aimed at a trusted power user. BIM-IT has a different product goal: deterministic, reviewable capabilities that any MCP client and BIM-IT Assistant can use without trusting generated code. The question this ADR answers is how BIM-IT gets the useful part of generic querying, the agent choosing *what* to read and the joins and counts running beside the data, without making model-written code part of the production capability layer.

## Decision drivers

- The Revit add-in remains the only authority for Revit truth (ADR-0001, ADR-0022).
- Read, write, and destructive operations stay clearly separated (AGENTS.md, ADR-0008).
- Results are bounded, filtered, and aggregated before they leave Revit, and structured content is authoritative (ADR-0005).
- Cross-call identity uses opaque references (ADR-0006).
- Revit-version API differences are absorbed once, inside BIM-IT, across the supported Revit versions (ADR-0004).
- The same capability works for external MCP clients and BIM-IT Assistant with no provider dependency (ADR-0014, ADR-0022).
- An inventory must be provably complete, or say explicitly that it is not.
- Tool surfaces stay small and coherent for agent usability (ADR-0007).

## Options considered

### Option A: Generic code execution (a BIM-IT `runPython` / `runCSharp`)

The model writes Python, C#, or raw Revit API source and BIM-IT runs it inside Revit, perhaps inside a rolled-back transaction.

- Advantages: maximum flexibility; any fact Revit exposes is reachable at once; no BIM-IT release per question.
- Disadvantages:
  - **Security.** Generated code runs with the user's machine permissions. A rolled-back transaction protects the model, not the filesystem, network, processes, or event registrations. Sandboxing a .NET or Python runtime inside the Revit process cannot be proven complete, and the approval model of ADR-0008 to ADR-0011 does not cover arbitrary code.
  - **Reproducibility.** The result shape is whatever the script returned. A Skill or Standard Pack pipeline cannot depend on it, and re-running a quarterly inventory means re-generating code.
  - **Compatibility.** Every session meets Revit API drift and engine differences again, as half of the benchmark's attempts did. A trusted implementation would absorb each difference once.
  - **Governance.** "Read-only" becomes a property of the generated text rather than of the product.

### Option B: One broad generic query tool ("SQL for Revit")

A single MCP tool accepts a declarative query language: any source, filter, projection, join, group, and aggregate.

- Advantages: no code execution; one schema to learn; very flexible.
- Disadvantages: the input schema becomes a language whose semantics must be specified, versioned, and bounded before any use case is validated. Agents choose badly among very general parameters. Every new source widens one tool's contract and its tests. Hard to evolve without breaking clients.

### Option C: A trusted declarative read-query kernel exposed through small, source-specific tools

An internal kernel receives bounded structured requests and executes only compiled BIM-IT query operations. Each MCP tool is a coherent view of one source (for example family types) with a small filter and projection vocabulary, a stable output schema, and pagination. Tools share the kernel's request validation, ordering, paging, bounds, identity, and serialization.

- Advantages: no generated code in Revit; the agent still decides what it wants; API drift stays inside BIM-IT; stable schemas; each tool is small enough to describe and test; new sources are additive.
- Disadvantages: questions outside the implemented sources still need a BIM-IT release; the vocabulary must be designed carefully so tools stay consistent.

### Option D: Keep extending specialised reads

Widen `revit_get_mep_device_types` and add similar per-question tools.

- Advantages: smallest step.
- Disadvantages: repeats the problem that the benchmark exposed. Each tool reinvents limits, ordering, and truncation, and none of them can prove completeness. It grows the surface per workflow, against ADR-0022.

## Decision

Option C is proposed.

### 1. No arbitrary code execution in the production Revit capability layer

BIM-IT will not add a capability that executes model-supplied Python, C#, Dynamo, macro, expression, or Revit API source, in read or write mode, inside or outside a transaction. The production layer runs only code that BIM-IT compiled, reviewed, and tested.

This is a product trade-off, not a judgement of scripting runtimes. A scripting agent runtime is a valid tool for a trusted user who reads the script. BIM-IT's contract is that an agent, an external MCP client, or a firm's Skill can read the model without anyone having to trust generated code, and that the same request returns the same shape on every supported Revit version. Users who want scripting can run a scripting MCP beside BIM-IT under ADR-0007 federation. BIM-IT does not depend on it.

### 2. A trusted declarative read-query kernel

The Revit add-in hosts an internal, provider-neutral read-query kernel.

- **Input:** a bounded structured request: a source, filters from a closed vocabulary, a projection from a closed field list, an ordering, a page size, and an optional cursor.
- **Execution:** each source maps to a compiled query operation that uses `FilteredElementCollector` and related read APIs. The operation decides how to read: which collectors, which class and category filters, how to join types to instances, how to count.
- **Output:** a typed result in `BimIt.Revit.Contracts`, with no Revit API object.

**The LLM decides what data it wants. BIM-IT code decides how the Revit API obtains it.** Revit-version differences, for example `ElementId.Value` versus `IntegerValue`, live in the add-in's multi-version compatibility layer (ADR-0004), never in model-generated text. Text is serialized through the same UTF-8 contract path as every other capability, so accented names need no special handling by the caller.

The kernel is internal. It is not itself an MCP tool and has no notion of providers, prompts, or Skills.

### 3. Semantic capabilities are preserved

The kernel does not replace BIM-semantic reads. The boundary:

| Kind of fact | Path |
|---|---|
| Generic model inventory, filter, and count facts: what families, types, and elements exist, with which names, categories, and counts | Declarative query layer (this ADR) |
| Spatial, topological, and geometric reasoning primitives: room boundaries and holes, linked spatial context, MEP connectivity, room layouts | Semantic read capabilities, for example `revit_read_spatial_rooms`, `revit_get_mep_topology`, `revit_get_room_layouts` |
| Persistent model mutation | ADR-0008 controlled-write path: preview, trusted local approval, apply, audit. Unchanged |

A semantic read may use kernel building blocks internally. The kernel never gains geometry reasoning, and it never writes.

### 4. Agent-facing contract: small source-specific tools over one kernel

The kernel is exposed first through **one source-specific MCP tool per coherent source**, not through one broad query tool. Each tool:

- names its source in its name and description;
- has a small input schema: filters, projection, ordering, page size, cursor;
- has an explicit output schema;
- shares the kernel's validation errors, paging fields, and bounds, so that learning one tool teaches the pattern for the next.

A broad tool is not ruled out forever. If several sources share a clearly stable vocabulary, a later ADR may unify them. That would be an evolution of the same kernel, not a change of architecture. Starting narrow keeps the first slice reviewable and lets real use decide the vocabulary.

Existing tools stay. `revit_get_mep_device_types` remains the MEP-placement catalogue for ADR-0018 and its `active` and placement semantics. Whether it is later rebuilt on the kernel is an implementation choice that must not change its contract.

### 5. First source: family/type inventory

The first implementation slice after this ADR targets the standards use case: **loadable families and their `FamilySymbol` types, in any supported category.** It is not limited to the MEP-placement allowlist.

For each type it returns Revit facts:

- category (a stable token and the display name);
- family name and type name;
- family placement metadata (`FamilyPlacementType`, mapped to a stable token);
- in-place versus loadable, where Revit exposes it (`Family.IsInPlace`);
- placed instance count in the queried document;
- used versus loaded but unused, derived from that count;
- opaque references for the type and family under ADR-0006, where a later call needs them.

It also supports family-level aggregation: types per family, placed types per family, and instances per family. That covers the used and unused family split without the client joining pages.

Not Revit facts, and therefore not returned: "Tt approved", "company standard", "preferred", library or `.rfa` provenance, and authorship. Those belong to future Standard Packs and human governance (ADR-0022). Name patterns, such as a `Tt_` prefix, are data for the agent to interpret, not classifications the kernel asserts.

Out of scope for the first slice: instances as rows, system families, linked documents, parameter values beyond what the slice names, and write anything.

### 6. Pagination and completeness

Any source that can exceed one response pages. Invariants:

- **Deterministic ordering.** Every source defines a total order on a key that does not depend on collector iteration order, for example category token, then family name, then type name, then a stable identity tie-breaker. The same request against an unchanged document returns the same sequence.
- **Bounded page size.** The client may ask for less than the maximum, never more. The maximum is fixed by the implementation and documented in the capability.
- **Opaque server-minted cursor.** A page that is not the last returns a cursor. Clients pass it back unchanged with an otherwise identical request. Clients must never construct, parse, or edit a cursor. The kernel rejects a cursor that does not match the document, the request, or the source with a deterministic error.
- **Counts.** A page reports the total matched count when it is cheap to compute exactly. Otherwise it says the count was not computed. It never reports an estimate as a count.
- **Explicit end.** The final page says so explicitly (for example `complete: true` with no cursor). Absence of a cursor alone is not the signal, and `truncated` alone is not enough.

**Consistency between pages.** Revit offers no read snapshot that lasts across calls, and the model may change between pages, through the user, a sync, or a controlled write. The invariant is: **a traversal is complete only if every page was read against the same unchanged document state.** The cursor binds the document identity, a fingerprint of the normalized request, and the position in the order. If the kernel can tell that the document changed since the first page, it refuses the cursor with a deterministic stale error and the client restarts. It never silently mixes two model states into one completed inventory. How the change is detected is left to the implementation slice and must be documented there (see the open questions). If a source cannot detect a change, its documentation must say that its completeness claim is a best effort for an unchanged model.

### 7. Structured output

Every kernel-backed tool declares an MCP output schema and returns structured content as the authoritative result (ADR-0005). Any text content is a convenience mirror of the structured result and is never the only source of a fact. Field names, enums, and error codes are stable and versioned with the Bridge protocol.

### 8. Safety boundaries

The query path:

- **Read-only by implementation, not only by annotation.** Kernel operations receive read access to the document. They never open a `Transaction`, `SubTransaction`, or `TransactionGroup`, and never call an API that modifies the document. Architecture tests in the slice should enforce this, for example by rejecting transaction types in kernel code. `readOnlyHint` and the absence of `destructiveHint` describe this; they do not create it.
- **Executes only known compiled operations.** A request selects among sources and fields. It never carries code, expressions, member names, or reflection targets. Unknown sources, fields, or filter keys are rejected, never ignored.
- **Bounded.** The request has a maximum size, filter count, and string lengths. The result has a maximum page size and character budget. Execution is bounded too, for example by a budget on elements scanned or on time, after which the kernel returns a deterministic error rather than a partial result presented as complete.
- **No filesystem, network, or process capability.** The kernel has no dependency that provides them.
- **No provider dependency.** No LLM, Skill, or Assistant type is referenced. BIM-IT Assistant reaches kernel tools through the ADR-0022 read facade like any other trusted read, discovered from their annotations and failing closed.
- **Usable by external MCP clients and BIM-IT Assistant identically.**
- **Writes unchanged.** Nothing in this ADR adds, widens, or bypasses a write, a preview, an approval, or an audit.

### 9. Future extensibility (discussed, not decided)

Candidate sources and operations, each to be decided on demonstrated need:

- **Instance queries:** elements of a category or type with level, host, workset, and design-option facts. These overlap `revit_query_elements` (CAP-0002), which has no cursor today. Paging CAP-0002 or adding an instance source must not create two answers to the same question.
- **System ElementTypes:** conduit, cable tray, wire, pipe, and duct types, distribution systems. They are not `FamilySymbol`s and need their own source shape.
- **View templates** and **schedules:** schedule *definitions* are inventory. Schedule *contents* are computed data and may belong elsewhere.
- **Shared parameter definitions and bindings:** GUID, data type, instance or type binding, bound categories. Highly relevant to Standard Packs.
- **Grouping, count, and distinct operations:** a small closed set over the fields of a source, not a general aggregation language.
- **Linked-document querying:** reading inventory from loaded links. It needs a link-addressing decision consistent with ADR-0019 and ADR-0021, and is not implied here.

None of these is committed to a single generic tool.

## Benchmark acceptance target for the first slice

A future live gate on the same reference electrical project. For the nine benchmark categories (Electrical Fixtures, Electrical Equipment, Lighting Fixtures, Lighting Devices, Data Devices, Communication Devices, Fire Alarm Devices, Security Devices, Nurse Call Devices), the new BIM-IT read capability must reproduce the pyRevit baseline:

| Fact | Baseline |
|---|---|
| Distinct families | 196 |
| Types (`FamilySymbol`) | 1,205 |
| Placed types | 254 |
| Placed instances | 8,278 |
| Used families | 87 |
| Loaded but unused families | 109 |

These numbers are evidence from one reference project at one point in time, not product constants. The model may have changed since. The gate compares against a baseline re-measured on the same document state, and the counting semantics must match (see the open questions).

The gate passes only if an isolated MCP client reaches the full inventory:

- without arbitrary scripts;
- without pyRevit, Nonica, or any other Revit MCP;
- without schema coaching in the prompt;
- without modifying the model;
- with no failed tool call attributable to Revit API, version, or runtime quirks;
- with every page traversed to an explicit final page.

## Consequences

### Positive

- Inventory questions get complete, checkable, reproducible answers with stable schemas that Skills and Standard Packs can depend on.
- API drift is fixed once in BIM-IT, across Revit 2025, 2026, and 2027, instead of in every agent session.
- The safety story stays simple: no generated code in Revit, and reads are read-only by construction.
- New sources are additive and share one paging and bounds model.

### Negative / costs

- A question outside the implemented sources still needs a BIM-IT release. The long tail that a scripting runtime answers at once is slower here.
- The kernel's vocabulary and paging model need careful design and tests up front.
- Page traversal of large inventories costs more round trips than one script.
- Two paths to some facts may exist for a while (for example `revit_get_mep_device_types` and the new source), and must be kept consistent.

## Open questions for the Product Owner and Tech Lead

1. **Change detection between pages.** Options: a document-scoped modification counter that the add-in keeps, a short-lived server-side snapshot of the ordered keys, or a documented best effort. Which is acceptable for the first slice?
2. **Instance counting semantics.** Do placed-instance counts include elements in non-primary design options, group members, and nested shared family instances? The pyRevit baseline counted every instance the unfiltered collector returned. The gate needs one documented rule, and probably a field that says which rule applies.
3. **Family identity in the result.** Is the family name, which is unique per document, enough? Or does the first slice also return an opaque family reference?
4. **Category scope.** Any category with loadable families, or a documented category set for the first slice?
5. **Page size and budgets.** The maximum page size, the result character budget (BIM-IT Assistant caps a read result at 120,000 characters), and the execution budget.
6. **First tool name and shape.** One family-type tool with a family-level aggregation mode, or two tools (types, families) over the same source?
7. **Relationship to existing tools.** Should `revit_get_mep_device_types` eventually delegate to the kernel, and should CAP-0002 gain the same cursor model?
8. **Bridge protocol.** The slice needs a new protocol version gate. Is a host that lacks it refused like other versioned capabilities?

## References

- ADR-0001: out-of-process MCP and Revit capability host
- ADR-0004: solution structure and multi-version build
- ADR-0005: agent context and token efficiency
- ADR-0006: document and element reference identity
- ADR-0007: federated MCP boundaries and orchestration
- ADR-0008 to ADR-0011: controlled writes, approval, audit
- ADR-0014: provider-neutral Assistant agent runtime
- ADR-0018: pattern family resolution (`revit_get_mep_device_types`)
- ADR-0022: Agent Skills and knowledge architecture
- CAP-0002: `revit_query_elements` (no cursor in v1)
- Live benchmarks on project `525413_E_EG`, Revit 2026.5, November 2026: BIM-IT Revit MCP validation and pyRevit MCP comparison, both run through an isolated Claude Code client (transcripts kept outside the repository)
