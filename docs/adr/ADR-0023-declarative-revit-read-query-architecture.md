# ADR-0023: Declarative Revit read-query architecture

- Status: Accepted
- Date: 2026-11-03
- Product Owner: Dave

## Context

ADR-0022 made Skills over generic capabilities the way to add workflows. A Skill can only be as good as the generic facts it can read. The first Standard Pack use case, a draft content inventory of a reference electrical project, showed where those facts run out.

Two live benchmarks ran on the same project on Revit 2026.5 (26.5.0.55), the active document `525413_E_EG_dave.otis`, with the same task, the same model, and an isolated Claude Code client that could reach only one Revit MCP each.

**BIM-IT Revit MCP** (typed read surface, 29 read calls):

- every call succeeded, and the results were structured and deterministic;
- it stayed read-only, because no write tool was reachable;
- it produced a useful 32-entry electrical sample with categories, families, types, placement classification, and instance counts per category;
- it could not prove a complete inventory. `revit_get_mep_device_types` covers a fixed MEP placement allowlist, stops at 80 items with `truncated: true`, has no cursor, and returns no per-type instance count and no family-level metadata. The client saw 89 distinct families. `level_name` was null on every device instance.

**pyRevit MCP** (generic `run_query`, 14 calls, 10 of them `run_query`):

- found 196 distinct families, 1,205 types, 254 placed types, and 8,278 placed instances in the nine target electrical categories, with 87 used families and 109 loaded but unused;
- joined types to instance counts and aggregated beside the Revit data, so only summaries crossed the wire;
- per-category instance counts matched the BIM-IT benchmark exactly, so both observed the same document.

The same run also showed costs:

- 5 of 10 `run_query` attempts failed. Causes: `ElementId.IntegerValue` removed in Revit 2026, `DB.Element.Name.GetValue` valid on IronPython but not on CPython, IronPython JSON serialization failing on accented French names (`É`, 0xC9) after the script had finished, and a list comprehension that could not see a loop variable in the script scope;
- each script was roughly 40 to 70 lines, and every run invented its own result keys, so a result can only be reproduced by keeping the script text;
- the script runs inside Revit with the Windows user's permissions. Model changes are rolled back, and the other machine-level capabilities are governed by the runtime's policy.

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

An internal kernel receives bounded structured requests and executes only compiled BIM-IT query operations. Each MCP tool is a coherent view of one source (for example family types) with a small filter vocabulary, a stable output schema, and pagination. Tools share the kernel's request validation, ordering, paging, bounds, identity, and serialization.

- Advantages: no generated code in Revit; the agent still decides what it wants; API drift stays inside BIM-IT; stable schemas; each tool is small enough to describe and test; new sources are additive.
- Disadvantages: questions outside the implemented sources still need a BIM-IT release; the vocabulary must be designed carefully so tools stay consistent.

### Option D: Keep extending specialised reads

Widen `revit_get_mep_device_types` and add similar per-question tools.

- Advantages: smallest step.
- Disadvantages: repeats the problem that the benchmark exposed. Each tool reinvents limits, ordering, and truncation, and none of them can prove completeness. It grows the surface per workflow, against ADR-0022.

## Decision

Option C is accepted. The Product Owner and Tech Lead approved this architecture direction after review. This ADR records architecture. The first slice, `revit_query_family_types`, was implemented from [CAP-0014](../capabilities/CAP-0014-revit-query-family-types.md) and passed its live gate (see the benchmark section and the implementation history below).

### 1. No arbitrary code execution in the production Revit capability layer

BIM-IT will not add a capability that executes model-supplied Python, C#, Dynamo, macro, expression, or Revit API source, in read or write mode, inside or outside a transaction. The production layer runs only code that BIM-IT compiled, reviewed, and tested.

This is a product trade-off, not a judgement of scripting runtimes. A scripting agent runtime is a valid tool for a trusted user who reads the script. BIM-IT's contract is that an agent, an external MCP client, or a firm's Skill can read the model without anyone having to trust generated code, and that the same request returns the same shape on every supported Revit version. Users who want scripting can run a scripting MCP beside BIM-IT under ADR-0007 federation. BIM-IT does not depend on it.

### 2. A trusted declarative read-query kernel

The Revit add-in hosts an internal, provider-neutral read-query kernel.

- **Input:** a bounded structured request: a source, filters from a closed vocabulary, a page size, and an optional cursor. Ordering is defined by the source, not chosen by the caller. A closed projection or alternate orderings may be added later only if real use shows a need.
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
- has a small input schema. In the first slice this is bounded filters, a page size, and an opaque cursor only. It exposes no caller-selectable projection and no caller-selectable ordering;
- has an explicit output schema;
- shares the kernel's validation errors, paging fields, and bounds, so that learning one tool teaches the pattern for the next.

A broad tool is not ruled out forever. If several sources share a clearly stable vocabulary, a later ADR may unify them. That would be an evolution of the same kernel, not a change of architecture. Starting narrow keeps the first slice reviewable and lets real use decide the vocabulary.

Existing tools stay. The first slice does **not** modify or migrate `revit_get_mep_device_types` (the MEP-placement catalogue for ADR-0018, with its `active` and placement semantics) or `revit_query_elements` (CAP-0002). Both remain unchanged. Any later delegation of either to the kernel is explicitly deferred and would need its own decision that preserves its contract.

### 5. First source and tool: `revit_query_family_types`

The first implementation slice is one tool, **`revit_query_family_types`**, over one source: **all `FamilySymbol` types in the active project document.** This includes loadable and in-place families when Revit exposes them as `FamilySymbol`s; `family_is_in_place` tells them apart. It is not limited to the MEP-placement allowlist and does not discard a type because BIM-IT cannot place it. Optional category filters may narrow the result. System `ElementType`s (conduit, cable tray, wire, and similar) remain future work.

The tool accepts only bounded filters, a page size, and an opaque cursor (section 4). It does not offer a separate family-level aggregation mode. Rows of types per family can be added later on demonstrated need.

**Type rows.** Each row carries Revit facts:

- category (a stable token and the display name);
- family name and type name;
- an opaque `type_ref` and an opaque `family_ref` under ADR-0006. The family name is human-readable and is not a cross-call identity;
- family placement metadata (`FamilyPlacementType`, mapped to a stable token);
- `family_is_in_place` (`Family.IsInPlace`), which distinguishes in-place families from loadable ones, where Revit exposes it;
- `placed_instance_count`;
- used versus unused, derived from that count.

**Query-level summary.** Every result also carries a summary of the whole matched set, not only the current page, enough to reproduce the electrical benchmark:

- matched type count;
- distinct family count;
- placed type count;
- placed instance count;
- used family count;
- unused family count (matched families whose matched types all have zero placed instances).

**Instance-count semantics (v1).** `placed_instance_count` is the number of matching instances in the active host project document, under the documented inventory semantics. It includes instances inside groups and design options when they exist as document elements. It includes nested shared family instances when Revit exposes them as matching project-document instances. Linked documents are excluded. These rules are written into the capability spec so that the benchmark is reproducible.

Not Revit facts, and therefore not returned: "Tt approved", "company standard", "preferred", library or `.rfa` provenance, and authorship. Those belong to future Standard Packs and human governance (ADR-0022). Name patterns, such as a `Tt_` prefix, are data for the agent to interpret, not classifications the kernel asserts.

Out of scope for the first slice: instances as rows, system families, linked documents, parameter values, caller-selectable projection or ordering, and any write.

### 6. Pagination and completeness

Any source that can exceed one response pages. Invariants:

- **Deterministic, server-defined ordering.** Every source defines a total order on a key that does not depend on collector iteration order. For family types: category token, then family name, then type name, then a stable identity tie-breaker. Callers cannot change it in v1. The same request against an unchanged document returns the same sequence.
- **Bounded page size.** Decided: default page size 100, maximum page size 200. The client may ask for less than the maximum, never more. The serialized result must also stay within the Assistant-compatible read-result budget (currently 120,000 characters). The scan and time execution budget is not decided here (see the open questions).
- **Opaque server-minted cursor.** A page that is not the last returns a cursor. Clients pass it back unchanged with an otherwise identical request. Clients must never construct, parse, or edit a cursor.
- **Counts.** The query-level summary (section 5) is exact. A result never reports an estimate as a count.
- **Explicit end.** The final page says so explicitly (for example `complete: true` with no cursor). Absence of a cursor alone is not the signal, and `truncated` alone is not enough.

**Consistency between pages.** Revit offers no read snapshot that lasts across calls, and the model may change between pages, through the user, a sync, or a controlled write. The invariant is: **a traversal is complete only if every page was read against the same unchanged document state.**

The first-slice design is a **document-scoped query epoch (revision) maintained by the add-in**. A continuation cursor is opaque and bound to:

- the Revit instance and document identity;
- a normalized fingerprint of the query;
- the document query revision at the time of the first page;
- the current paging position.

If the relevant document revision has changed when the next page is requested, the cursor is rejected as stale with a deterministic error, and the client restarts the traversal. A cursor that does not match the document, the query, or the source is rejected the same way. An add-in or session restart may also invalidate outstanding cursors. The design does **not** require a retained full inventory snapshot. How the cursor is encoded and where state is kept are implementation details. A traversal never silently mixes two model states into one inventory presented as complete.

### 7. Structured output

Every kernel-backed tool declares an MCP output schema and returns structured content as the authoritative result (ADR-0005). Any text content is a convenience mirror of the structured result and is never the only source of a fact. Field names, enums, and error codes are stable and versioned with the Bridge protocol.

### 8. Safety boundaries

BIM-IT does not claim a .NET sandbox. Compiled kernel code runs inside the Revit process with the permissions of the Revit user, like the rest of the add-in. Safety comes from a closed request vocabulary and a trusted implementation. It does not come from a process sandbox, from MCP annotations, or from static tests that could prove compiled C# incapable of opening a transaction or touching the machine. The security invariant is:

- **Requests cannot express operations.** A model or client request cannot express, inject, or select a filesystem, network, process, reflection, or arbitrary-code operation. It selects a source and supplies filter values, a page size, and a cursor. It never carries code, expressions, or .NET, runtime, or Revit API type or member names that would select executable API members, reflection targets, or runtime behavior. Ordinary BIM data values, such as family names, Revit `FamilySymbol` type names, and category tokens, remain valid closed filter values. Unknown sources, filter keys, or fields are rejected, never ignored.
- **No model-supplied code runs.** No Python, C#, Dynamo, or Revit API source from a model or client is ever executed (section 1).
- **Kernel implementations are trusted compiled BIM-IT code**, reviewed and tested, and written to contain no such behavior. They receive the document for reading, open no `Transaction`, and call no API that modifies the document. This is a rule for the reviewer and the code owner. Review and tests demonstrate that the shipped operations behave as specified. They do not make a hypothetical malicious operation impossible.
- **Read-only comes from the closed capability design** (a read source with no write path) **plus that trusted implementation.** `readOnlyHint` and the absence of `destructiveHint` describe the design for clients. They do not create it.
- **Bounded.** The request has a maximum size, filter count, and string lengths. The result has a maximum page size and character budget. Execution is bounded too, for example by a budget on elements scanned or on time, after which the kernel returns a deterministic error rather than a partial result presented as complete.
- **No provider dependency.** No LLM, Skill, or Assistant type is referenced. BIM-IT Assistant reaches kernel tools through the ADR-0022 read facade like any other trusted read, discovered from their annotations and failing closed.
- **Usable by external MCP clients and BIM-IT Assistant identically.**
- **Writes unchanged.** Nothing in this ADR adds, widens, or bypasses a write, a preview, an approval, or an audit.

### 9. Bridge capability gate

The first implementation has its own versioned Bridge capability gate, using the existing compatibility pattern (a host advertises the version only when it supports the capability). A host that does not advertise it is refused with the usual unsupported-version error. No fallback pretends an older host supports the capability.

### 10. Future extensibility (discussed, not decided)

Candidate sources and operations, each to be decided on demonstrated need:

- **Instance queries:** elements of a category or type with level, host, workset, and design-option facts. These overlap `revit_query_elements` (CAP-0002), which has no cursor today. Paging CAP-0002 or adding an instance source must not create two answers to the same question.
- **System ElementTypes:** conduit, cable tray, wire, pipe, and duct types, distribution systems. They are not `FamilySymbol`s and need their own source shape.
- **View templates** and **schedules:** schedule *definitions* are inventory. Schedule *contents* are computed data and may belong elsewhere.
- **Project parameter definitions and bindings (shared and non-shared):** identity, shared-parameter GUID when one exists, data type, instance or type binding, bound categories. Highly relevant to Standard Packs. Proposed as the second demonstrated source in [CAP-0015](../capabilities/CAP-0015-revit-query-parameter-bindings.md) (`revit_query_parameter_bindings`, Proposed, not accepted or implemented), over the entries of `Document.ParameterBindings`. Evidence: [ADR_0023_PARAMETER_BINDINGS_PYREVIT_BASELINE.md](../ADR_0023_PARAMETER_BINDINGS_PYREVIT_BASELINE.md).
- **Grouping, count, and distinct operations:** a small closed set over the fields of a source, not a general aggregation language.
- **Linked-document querying:** reading inventory from loaded links. It needs a link-addressing decision consistent with ADR-0019 and ADR-0021, and is not implied here.

None of these is committed to a single generic tool.

## Benchmark acceptance target for the first slice

The live gate on the same reference electrical project. For the nine benchmark categories (Electrical Fixtures, Electrical Equipment, Lighting Fixtures, Lighting Devices, Data Devices, Communication Devices, Fire Alarm Devices, Security Devices, Nurse Call Devices), the new BIM-IT read capability must reproduce the pyRevit baseline:

| Fact | Baseline |
|---|---|
| Distinct families | 196 |
| Types (`FamilySymbol`) | 1,205 |
| Placed types | 254 |
| Placed instances | 8,278 |
| Used families | 87 |
| Unused families | 109 |

These numbers are evidence from one reference project at one point in time, not product constants. The model may have changed since. The gate compares against a baseline re-measured on the same document state, and the counting semantics in section 5 must match.

The gate passes only if an isolated MCP client reaches the full inventory:

- without arbitrary scripts;
- without pyRevit, Nonica, or any other Revit MCP;
- without schema coaching in the prompt;
- without modifying the model;
- with no failed tool call attributable to Revit API, version, or runtime quirks;
- with every page traversed to an explicit final page.

### Gate result

The gate passed on 2026-11-04, in two natural-agent runs. In both, an isolated Claude Code client could reach only `BimIt.Revit.Server`, and its prompt was the BIM task alone. The agent chose `revit_get_context` and `revit_query_family_types` by itself, wrote no code, did not change the model, and had 0 BIM-IT or Revit tool failures. Both runs produced 196 families, 1,205 types, 254 placed types, 8,278 placed instances, 87 used families, and 109 unused families, equal to the baseline above, with French names intact.

The first run (26 calls) proved those totals but did not read every row of two categories, so it did not meet the "every page traversed to an explicit final page" criterion. A rerun with a stricter task (24 calls) traversed all 1,205 rows in 21 pages to `complete: true`, with 1,205 distinct row identities and no duplicate or gap. The criterion is unchanged.

In each run, one response of 79,310 characters, inside the 120,000-character budget, was not displayed by that client because of the client's own output limit. That was not a server error, and the agent recovered with smaller pages. The record is [ADR_0023_NATURAL_AGENT_BENCHMARK_VALIDATION.md](../ADR_0023_NATURAL_AGENT_BENCHMARK_VALIDATION.md). The tool-contract record, including the stale-cursor check, is [CAP_0014_FAMILY_TYPES_VALIDATION.md](../CAP_0014_FAMILY_TYPES_VALIDATION.md).

The gate covers the first slice only: the host document, one reference project, one Revit version, and one client. It does not validate any source named in section 10.

## Consequences

### Positive

- Inventory questions get complete, checkable, reproducible answers with stable schemas that Skills and Standard Packs can depend on.
- API drift is fixed once in BIM-IT, across Revit 2025, 2026, and 2027, instead of in every agent session.
- The safety story stays simple: no generated code in Revit, a closed request vocabulary, and trusted reviewed read operations.
- New sources are additive and share one paging and bounds model.

### Negative / costs

- A question outside the implemented sources still needs a BIM-IT release. The long tail that a scripting runtime answers at once is slower here.
- The kernel's vocabulary and paging model need careful design and tests up front.
- Page traversal of large inventories costs more round trips than one script.
- Two paths to some facts may exist for a while (for example `revit_get_mep_device_types` and the new source), and must be kept consistent.

## Implementation history and resolved questions

The five implementation questions that this ADR listed at acceptance were answered by CAP-0014 and its implementation. The answers are recorded in CAP-0014, which is the source of truth for them.

1. **Category filter shape.** Resolved: stable `BuiltInCategory` tokens only (`OST_...`). Localized display names are not accepted as input, and an unknown or malformed token is rejected as an invalid query. The result carries both the token and the localized display name.
2. **Execution budget.** Resolved for v1: no scan limit, no snapshot, and no cache. Each page recomputes the matched inventory and the exact summary under the existing 15-second capability timeout, and a timeout fails the call rather than returning a partial result. The live runs recorded page times well inside that timeout. Caching and snapshots remain deferred until live evidence justifies them.
3. **Revision source.** Resolved: the Addin advances the document query revision from Revit's `DocumentChanged`, `DocumentSynchronizedWithCentral`, and `DocumentReloadedLatest` events. The handlers must be registered while `OnStartup` runs. A first live run showed that a handler registered later, from the Idling bootstrap, received no events, and that was corrected and revalidated live. See [CAP_0014_FAMILY_TYPES_VALIDATION.md](../CAP_0014_FAMILY_TYPES_VALIDATION.md).
4. **Name filters.** Resolved: v1 offers case-insensitive substring filters on family name and type name, in addition to category tokens and a `usage` filter.
5. **Bridge version number.** Resolved: Bridge protocol 22.

Also decided at acceptance: change detection between pages, instance-count semantics, family identity, the scope of the source (all `FamilySymbol` types in the active project document), the first tool, the relationship to existing tools, the Bridge gate, the default page size (100), the maximum page size (200), and the requirement that a serialized result stay within the Assistant-compatible result budget. See sections 4 to 6, 8, and 9.

No question from the original list remains open. Questions that are not part of the first slice, such as the sources in section 10, stay undecided.

Implementation history of the first slice:

- CAP-0014 accepted, then implemented: Contracts, Addin read, Bridge protocol 22, and the MCP tool.
- Live validation of the tool contract found that a stale cursor was not detected, because of the late event registration in question 3. The fix and a passing live revalidation are recorded in [CAP_0014_FAMILY_TYPES_VALIDATION.md](../CAP_0014_FAMILY_TYPES_VALIDATION.md).
- The natural-agent gate passed on 2026-11-04 (see "Gate result").
- 2026-11-05: a natural-agent run found no BIM-IT tool for a project-wide parameter binding inventory. An independent pyRevit baseline was frozen, and CAP-0015 was proposed as the second source. It is not accepted, and no Bridge version is allocated.

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
- [CAP-0014](../capabilities/CAP-0014-revit-query-family-types.md): `revit_query_family_types`
- [CAP_0014_FAMILY_TYPES_VALIDATION.md](../CAP_0014_FAMILY_TYPES_VALIDATION.md) and [ADR_0023_NATURAL_AGENT_BENCHMARK_VALIDATION.md](../ADR_0023_NATURAL_AGENT_BENCHMARK_VALIDATION.md): live validation records
