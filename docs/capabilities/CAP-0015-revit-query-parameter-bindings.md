# CAP-0015: `revit_query_parameter_bindings`

- Status: Accepted
- Operation class: Read
- Date: 2026-11-05
- Architecture: Accepted [ADR-0023](../adr/ADR-0023-declarative-revit-read-query-architecture.md), second source (accepted, implemented; natural-agent gate pending)
- Introduced bridge protocol version: 23 ([BRIDGE-0019](../bridge/BRIDGE-0019-query-parameter-bindings-capability-rpc.md))

## Authorization

This specification is **Accepted** (Product Owner and Tech Lead, 2026-11-05, after the independent pyRevit baseline and contract review). It is **implemented**: the Contracts, the Addin query core and `Document.ParameterBindings` reader, the Bridge RPC `revit.query_parameter_bindings` on protocol 23, the Server application service, and the MCP tool `revit_query_parameter_bindings` exist, so production `tools/list` is 20. The typed Bridge live validation (Bridge → Addin → Revit 2026.5) passed on 2026-11-06 at `393b12f`, including the stale-cursor contract check below; see [CAP_0015_PARAMETER_BINDINGS_BRIDGE_VALIDATION.md](../CAP_0015_PARAMETER_BINDINGS_BRIDGE_VALIDATION.md). That record was made before the tool existed, when `tools/list` was 19. The natural-agent gate, through an isolated official MCP client against the 20-tool Server, has not been run, so the acceptance gate below is not met.

It defines the second source of the ADR-0023 read-query kernel. The independent evidence it relies on is [ADR_0023_PARAMETER_BINDINGS_PYREVIT_BASELINE.md](../ADR_0023_PARAMETER_BINDINGS_PYREVIT_BASELINE.md).

Where this specification and ADR-0023 or CAP-0004 differ, the accepted document wins and this one is corrected.

## Purpose

`revit_query_parameter_bindings` returns a complete, deterministic, paged inventory of the project parameter bindings of the active host project document. One row is one entry of `Document.ParameterBindings`: a parameter definition, how it is bound (instance or type), and the complete set of categories it is bound to. A query-level summary describes the whole matched set.

The use case that demonstrated the need is a parameter-standards audit of a reference electrical project. On 2026-11-05, an isolated natural-agent run against the 19-tool BIM-IT surface could not produce this inventory. The only parameter-discovery tool, `revit_describe_parameters`, is element-scoped (at most 10 elements) and reports neither binding kind nor bound categories. The same facts were then read independently through pyRevit, which required generated code and two failed attempts (see the baseline record).

Typical agent use:

```text
revit_get_context
-> document_id
-> revit_query_parameter_bindings (filters ...)
-> next_cursor
-> revit_query_parameter_bindings (same query + cursor) ... until complete: true
-> optional: revit_get_parameter_values (element_ref + parameter_ref from a row, when the element exposes that parameter)
```

## Source boundary

The source is exactly: **all entries of `Document.ParameterBindings` of the active host project document.**

The baseline document shows why the boundary matters. It held 1,713 `ParameterElement`s (1,150 of them `SharedParameterElement`s), but only 144 entries in `Document.ParameterBindings`. The other 1,569 parameter elements (1,077 shared, 492 non-shared) are definitions that the document stores for other reasons, for example parameters that loaded families or schedules carry. They are not project parameter bindings.

The source is **not**:

| Not in the source | Why |
|---|---|
| every `ParameterElement` or `SharedParameterElement` of the document | most of them are not bound to categories (1,569 of 1,713 in the baseline) |
| every parameter visible on elements | that is CAP-0004 discovery from known elements; it includes built-in and family parameters |
| family-document parameters (`FamilyManager`) | a family document has no project binding map; family documents are unsupported |
| schedule-specific parameter metadata | schedule fields are not bindings |
| linked-document bindings | linked documents are excluded, as in CAP-0014 |
| Autodesk Parameters Service or shared-parameter files | not document state |
| parameter values | values come from `revit_get_parameter_values` (CAP-0005) |

The source is the binding map as the Revit API exposes it. It includes every entry, including definitions whose `Definition.Visible` is false (two in the baseline). See "Hidden definitions".

## Operation class and safety

Read-only. The rules of CAP-0014 apply unchanged:

- no `Transaction`, `SubTransaction`, or `TransactionGroup`, and no Revit API call that modifies the document;
- no model-supplied code, expression, or Revit API member name in the request. Parameter names and category tokens are ordinary closed filter values;
- no file writes, no network access, no reflection or API selection driven by the request;
- all Revit API access through EXEC-0001;
- read-only safety comes from the closed vocabulary and the trusted implementation (ADR-0023 section 8), not from annotations or a sandbox.

## MCP tool

- Name: `revit_query_parameter_bindings`
- Title: `Query Revit Parameter Bindings`
- Annotations: `readOnlyHint: true`, `openWorldHint: false`. The tool must pass the BIM-IT Assistant read-facade policy (ADR-0022).

The description states, in this order, concisely:

1. what it returns: the project parameter bindings of the active host project, one row per bound definition, with identity, data type, instance or type binding, and all bound categories, plus a summary of the whole matched set;
2. that it is a paged inventory: repeat the same query with `next_cursor` until `complete` is `true`;
3. that `document_id` comes from `revit_get_context`;
4. that `parameter_ref` is the same reference that `revit_describe_parameters` and `revit_get_parameter_values` use;
5. that category filters use stable `OST_` tokens and the name filter is a case-insensitive substring.

## MCP input

Closed object. `additionalProperties` is false. A JSON `null` for an optional field means the field is omitted.

```json
{
  "instance_id": "optional Server routing identity",
  "document_id": "required active-document guard",
  "name_contains": "Tt_",
  "identity_kind": "all",
  "binding_kind": "all",
  "category_tokens": ["OST_ElectricalFixtures"],
  "page_size": 50,
  "cursor": null
}
```

`instance_id`, `document_id`, and `cursor` have exactly the CAP-0014 semantics, including `DOCUMENT_CONTEXT_CHANGED` for a mismatched `document_id` and the 1 to 2048 character cursor bound.

### Filter vocabulary and justification

Each filter is in v1 only because the audit use case needs it. All filters AND with each other.

| Filter | Shape | Demonstrated use | Decision |
|---|---|---|---|
| `category_tokens` | 1 to 20 distinct `BuiltInCategory` member names | "which parameters are bound to Electrical Fixtures": the core question of an electrical audit. The baseline binds 28 definitions to Electrical Fixtures out of 144 | Include |
| `identity_kind` | enum `all` (default), `shared`, `local`, `built_in` | a shared-parameter audit (GUID compliance) needs the 73 shared definitions without the 71 local ones | Include |
| `binding_kind` | enum `all` (default), `instance`, `type` | type-bound parameters are the exception (6 of 144) and are what an audit checks separately | Include |
| `name_contains` | 1 to 256 characters | find a naming family such as a firm prefix. The prefix is data the agent interprets, not a classification | Include |
| `page_size`, `cursor` | see "Pagination" | paging | Include |

Not in v1: exact-name arrays, GUID lookup, data-type filters, group filters, regex, wildcards, projection, ordering. A GUID lookup is answered by `identity_kind: "shared"` and a traversal. A data-type filter has no demonstrated need: in the baseline, 132 of 144 definitions are text.

### `category_tokens`

Same token rules as CAP-0014: the exact, case-sensitive name of a defined `BuiltInCategory` member of the running Revit version, not `INVALID`, not numeric, not a localized display name. Shape is checked before Revit execution and defined-ness in the Addin. A defined token that no binding uses is valid and contributes zero matches.

**Match semantics.** A definition matches when **any** of its bound categories has one of the requested tokens. The returned row still carries the definition's **complete** bound-category set, not only the matching categories. A filter narrows which rows return. It never truncates a row.

### `identity_kind` and `binding_kind`

Closed enums. Any other value is `INVALID_REQUEST` at the MCP layer and `INVALID_PARAMETER_BINDING_QUERY` at the transport-neutral layer.

### `name_contains`

The CAP-0014 name-filter semantics: case-insensitive substring with `StringComparison.OrdinalIgnoreCase` (folds case, not accents), no trimming, no regex or wildcards, and whitespace-only is `INVALID_PARAMETER_BINDING_QUERY`. It matches the definition name.

### `page_size`

```text
default: 50
minimum: 1
maximum: 100
```

`page_size` is a maximum, not part of the query fingerprint, and may change between pages. The reasoning is in "Page size".

## Row

Closed object, all fields required.

```json
{
  "parameter_ref": "opaque",
  "name": "Example parameter",
  "identity": { "kind": "shared", "guid": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx" },
  "data_type": { "kind": "spec", "forge_type_id": "autodesk.spec:spec.string-2.0.0" },
  "binding_kind": "instance",
  "categories": [
    { "category_token": "OST_ElectricalFixtures", "category_name": "Electrical Fixtures" }
  ]
}
```

- **`parameter_ref`:** the existing CAP-0004 opaque reference. See "parameter_ref interoperability". No new reference namespace.
- **`name`:** `Definition.Name`, Unicode, unmodified. Not identity.
- **`identity`:** exactly the CAP-0004 `identity` object: `built_in` with `parameter_type_id`, `shared` with a lowercase hyphenated `guid`, or `local` with no further field.
- **`data_type`:** exactly the CAP-0004 `data_type` object (`measurable_spec`, `spec`, `category`, `unknown`, and `forge_type_id` when Revit supplies a non-empty identifier), classified from `Definition.GetDataType()` with the same helpers.
- **`binding_kind`:** `instance` for an `InstanceBinding`, `type` for a `TypeBinding`.
- **`categories`:** the complete bound-category set. See "Categories".

Not in the row in v1: parameter group, `Visible`, `VariesAcrossGroups`, any value, formulas, numeric `ElementId`s, `UniqueId`s, category type. See "Explicitly deferred".

### Categories

Each entry has the CAP-0014 category shape: `category_token` (the `BuiltInCategory` member name) and `category_name` (the localized Revit display name).

- If a bound category is null, cannot be read, or its value is not a defined `BuiltInCategory` member, the entry is still returned with `category_token: "unknown"` and the display name when readable, otherwise an empty string. It is never dropped. A `category_tokens` filter cannot match `unknown`, as in CAP-0014.
- Entries are ordered by `category_token`, then `category_name`, both ordinal.
- Each Revit category appears once. Two different Revit categories that both map to `unknown` with the same name remain two entries.
- An empty binding category set is returned as `categories: []`. The baseline had none, but the contract does not assume it is impossible.
- Category type (Model, Annotation, Internal, AnalyticalModel) is not exposed. The baseline observed Internal and AnalyticalModel categories in binding sets, but no use case needs the distinction. The token already identifies the category.

The baseline had 354 associations with 109 distinct categories. Every association mapped to a defined `OST_` member, and every bound category was a built-in category.

### Classification failures

The binding map key is a `Definition`, and the value a `Binding`. Rows are never silently dropped:

- a key whose identity cannot be classified still has a `local` identity through the existing classifier's fallback key (see "parameter_ref interoperability");
- a binding that is neither `InstanceBinding` nor `TypeBinding`, or a key that is not an `InternalDefinition`, fails the call with `REVIT_EXECUTION_FAILED`. It is never omitted and never reported as complete. The baseline had no such entry.

## `parameter_ref` interoperability

This is a core requirement. The capability reuses the CAP-0004 identity system. It does not create a second one.

### Existing mechanism

- `OpenDocumentParameterIdentityService` keeps one `ParameterIdentityMap` per open document.
- `ParameterIdentityMap.GetRef(source, identity)` returns the existing reference for the key `(GetElementParameterSource, identity kind, stable key)`, or mints a random opaque reference the first time. `TryResolve` returns the `(source, identity)` binding.
- `ParameterIdentityClassifier` produces the identity: built-in type id, else shared GUID, else a local stable key. For a local parameter the stable key is the `UniqueId` of the `ParameterElement` of the `InternalDefinition`, with a fallback key derived from the definition id when that element is not available.
- `RevitParameterIdentity.Classify(Parameter, Document)` and `ClassifyDataType(Parameter)` feed it from a `Parameter`.
- CAP-0005 and the CAP-0007/CAP-0008 write path resolve a reference with `TryResolve` and then scan an element's `GetOrderedParameters()` on the bound source for a parameter of the same identity.

### Rule

For each binding-map entry, the capability computes:

```text
source   = Instance for InstanceBinding, Type for TypeBinding
identity = the classifier's identity of the definition (see "Definition classification")
parameter_ref = OpenDocumentParameterIdentityService.GetRef(document, source, identity)
```

The identity and reference namespace is the CAP-0004 one. For the same open document, the same definition, and the same source, the identity is the same whichever path classified it, so `GetRef` returns the same `parameter_ref`, whichever tool runs first. Every reference from this capability resolves through `OpenDocumentParameterIdentityService.TryResolve`.

**Parity with `revit_describe_parameters`** is required for every definition that CAP-0004 can actually observe, that is, a parameter that `GetOrderedParameters()` returns on the instance or type surface of an element passed to `revit_describe_parameters`. For such a definition, both tools return the same `parameter_ref` and the same `identity` and `data_type`.

**Chaining to `revit_get_parameter_values` (CAP-0005)** is conditional:

- when a target element exposes the parameter on its CAP-0005 parameter surface for the bound source, the reference chains normally and CAP-0005 reads its value;
- a binding-map definition that the element does not expose on that surface is still a valid inventory row, but the reference may not resolve to a value on that element. CAP-0005 reports that with its existing item-level semantics. This capability makes no promise that every row yields a value on every element of a bound category.

### Definition classification

The existing classifier entry points take a `Parameter`. A binding entry has a `Definition` and no element. The implementation adds definition-based entry points that feed the same pure `ParameterIdentityClassifier`. The classification must be null-safe, because Autodesk documents that `InternalDefinition.GetParameterTypeId()` returns `null` for custom-defined parameters:

```text
typeId = definition.GetParameterTypeId()
if typeId is not null and not empty
   and ParameterUtils.IsBuiltInParameter(typeId)
    -> built_in, parameter_type_id = typeId.TypeId
else if doc.GetElement(definition.Id) is SharedParameterElement spe
    -> shared, guid = spe.GuidValue (existing canonical lowercase format)
else
    -> local, stable key from the existing ResolveLocalKey logic for an InternalDefinition
```

`ParameterUtils.IsBuiltInParameter` is called only when `typeId` is non-null and non-empty. A null or empty id is never classified as built-in and never raises.

Required internal entry points:

- `RevitParameterIdentity.Classify(InternalDefinition definition, Document document)` implementing the rule above;
- `RevitParameterIdentity.ClassifyDataType(Definition definition)`, with the existing `Parameter` overload delegating to it;
- `ParameterIdentityClassifier` stays the single place that orders built-in, then shared, then local.

A parity test is required. For a parameter observed on an element and the same definition read from the binding map, the `Definition` path and the existing `Parameter` path must produce an equal `ClassifiedParameterIdentity` and data type for the same definition and source, and therefore the same `parameter_ref`. It must cover shared, local, and accented names, and a definition whose `GetParameterTypeId()` is null.

### Hidden definitions

Decision: the capability inventories **all** `Document.ParameterBindings` entries, including definitions whose `Definition.Visible` is false (two in the baseline). v1 adds no `visible` field and excludes no hidden definition, because either would break completeness against the binding map or widen the row without a demonstrated need.

Consequence: CAP-0004 discovers only parameters that `GetOrderedParameters()` returns, and CAP-0005 resolves through the same surface. A hidden definition has a valid row and `parameter_ref`, but `revit_describe_parameters` may never show it and `revit_get_parameter_values` may not resolve it to a value on an element. This is the conditional chaining described in "Rule".

## Query-level summary

Every page returns the same exact summary over the **entire matched set**:

```json
{
  "matched_definition_count": 144,
  "shared_count": 73,
  "local_count": 71,
  "built_in_count": 0,
  "instance_bound_count": 138,
  "type_bound_count": 6,
  "distinct_category_count": 109
}
```

| Field | Definition |
|---|---|
| `matched_definition_count` | rows in the matched set; one per binding-map entry |
| `shared_count`, `local_count`, `built_in_count` | matched rows per identity kind |
| `instance_bound_count`, `type_bound_count` | matched rows per binding kind |
| `distinct_category_count` | distinct Revit categories across the **complete** bound-category sets of the matched rows, counted by Revit category, so two `unknown` categories count twice |

Invariants: the three identity counts sum to `matched_definition_count`; the two binding counts sum to it; the sum of `returned_count` over a completed traversal equals it. An empty match returns all zeros.

`built_in_count` is expected to be 0 in normal projects. Decided at review: it stays, because the three identity counts partition the set, and a non-zero value is a fact an auditor needs.

**Not in the summary:** `definition_category_pair_count`. It equals the sum of `categories.length` over a completed traversal and is fully derivable. The acceptance gate checks it that way. Data-type counts and per-category counts are also not in the summary: they are derivable, and no use case has needed them as a server aggregate.

## Result

```json
{
  "context": { "instance_id": "opaque", "document_id": "opaque" },
  "summary": { "...": "as above" },
  "returned_count": 0,
  "complete": true,
  "next_cursor": null,
  "bindings": []
}
```

Closed objects at every level. Structured content is authoritative, with the CAP-0014 success and error policy (no duplicated JSON text on success; compact text error with `isError`).

## Ordering

Server-defined total order, not configurable, independent of binding-map iteration order:

1. `name`, `OrdinalIgnoreCase`;
2. `name`, `Ordinal`;
3. identity kind, in the order `built_in`, `shared`, `local`;
4. the identity's stable key, `Ordinal`: the built-in parameter type id, the lowercase shared GUID, or the local stable key.

Decided at review: the first two keys follow CAP-0004's descriptor ordering, so the two parameter tools list names the same way. Keys 3 and 4 make the order total. Each binding-map entry is keyed by one definition, so the stable identity key is unique per row. The local stable key is an internal Revit-derived string (the `ParameterElement` `UniqueId`). It is used for ordering only and is never returned. `parameter_ref` is not an ordering key, because it is minted randomly. `OrdinalIgnoreCase` folds case without culture rules and does not fold accents, so the order is reproducible on every machine.

## Pagination and consistency

The ADR-0023 section 6 model and the CAP-0014 mechanics are reused unchanged: `complete`, `next_cursor`, `returned_count`; the end is signalled only by `complete: true`; shorter pages when the result budget requires; `RESULT_TOO_LARGE` only when one row and the envelope cannot fit; no retained snapshot and no cache; every page recomputes the matched set and the summary within one EXEC-0001 work item, under the existing 15-second capability timeout.

### Page size

The 120,000-character result budget (`ReadResultBudget.MaxCharacters`) is authoritative. `page_size` only caps the row count.

Estimates from the frozen baseline, re-serialized offline into the row shape above (compact JSON, a 36-character placeholder `parameter_ref`, envelope and summary excluded):

| Measure | Estimate |
|---|---|
| Row size: minimum, median, 90th percentile | 280, 339, 441 characters (the median row has 1 category, the 90th percentile 2) |
| Each category entry | about 72 characters |
| Largest row (107 categories) | 7,960 characters |
| Whole 144-row inventory | about 62,300 characters |
| Largest page of 50 rows, in the specified order | about 23,400 characters |
| Largest page of 100 rows, in the specified order | about 39,600 characters |

CAP-0014 rows have a nearly fixed size. These rows do not: one definition bound to most categories costs as much as 20 to 25 ordinary rows. The ADR-0023 natural-agent run also showed a client that did not display a 79,310-character response, although it was inside the budget.

Decided at review: **default 50, maximum 100.** On the baseline, a default page stays near 23,000 characters and a maximum page near 40,000, both well below that observed client limit. The full baseline takes 3 pages at the default and 2 at the maximum. A larger maximum would rarely help: a project that binds many definitions to many categories would hit the budget first, and the budget, not `page_size`, then shortens the page.

### Cursor

Opaque, signed, stateless, and bound to the instance, `document_id`, a normalized query fingerprint (`category_tokens` as a sorted set, `name_contains`, `identity_kind`, `binding_kind`; not `page_size`), the document query revision, and the position.

The fingerprint must include a **source discriminator** (for example the literal source name). A cursor minted by `revit_query_family_types` must never be accepted here, and the reverse, even if a future implementation shares one signing key. Decided at review: the implementation generalizes the CAP-0014 `FamilyTypeCursorCodec` and query fingerprint into one narrowly scoped, source-tagged query cursor primitive shared by the declarative query sources, rather than duplicating them. The primitive covers only the cursor and fingerprint mechanics: signing, the source tag, the fingerprint, document binding, revision, and position. It is not a generalized query framework. Filtering, ordering, summaries, and shaping stay in each source. The CAP-0014 observable cursor behavior must not change. The validation order is CAP-0014's.

### Document query revision

The existing `DocumentQueryRevisionService` is reused. No second revision system is created.

It advances on `DocumentChanged`, `DocumentSynchronizedWithCentral`, and `DocumentReloadedLatest`, registered during `OnStartup`. Adding, removing, or rebinding a project parameter, or changing its categories, is a committed document transaction, so `DocumentChanged` is expected to fire. A synchronization or reload that brings binding changes from central is covered by the other two events.

The revision is per document, not per source. A change that only affects family types also invalidates outstanding binding cursors. That over-invalidation is allowed by ADR-0023 and only forces a restart.

This is an expectation, not proof. The validation record must show live that a binding change between pages yields `STALE_CURSOR`. The code comments that describe the revision as protecting the family/type inventory are to be generalized to "query inventory" with the implementation.

## Execution

1. Validate the transport-neutral request.
2. Execute through EXEC-0001. Require an active document (`NO_ACTIVE_DOCUMENT`) and a project document (`UNSUPPORTED_DOCUMENT_KIND` for a family document, whose parameter state is not a binding map). Apply the document guard.
3. Validate any cursor, then compare its revision.
4. Read the revision, traverse `Document.ParameterBindings` completely, classify identity and data type, mint or reuse `parameter_ref`s, read categories.
5. Filter, compute the exact summary, order, position, fit the budget, and shape the page.
6. Open no transaction.

The traversal is whole-map on every page. On the baseline it is 144 entries. Minting references for every entry grows the per-document `ParameterIdentityMap` by at most the binding-map size, for the open-document lifetime, which is the existing CAP-0004 lifetime rule.

## Errors

Server routing, reused: `NO_REVIT_INSTANCE`, `INSTANCE_REQUIRED`, `INSTANCE_NOT_FOUND`, `INSTANCE_UNAVAILABLE`.

Reused capability errors: `NO_ACTIVE_DOCUMENT`, `DOCUMENT_CONTEXT_CHANGED`, `UNSUPPORTED_DOCUMENT_KIND`, `REVIT_EXECUTION_TIMEOUT`, `REVIT_EXECUTION_FAILED`, `INVALID_CURSOR`, `STALE_CURSOR`, `RESULT_TOO_LARGE`, and `BRIDGE_PROTOCOL_INCOMPATIBLE`.

New: `INVALID_PARAMETER_BINDING_QUERY` for invalid filters, bounds, or transport-neutral shape, following the per-capability pattern of `INVALID_FAMILY_TYPE_QUERY`. Malformed MCP input stays `INVALID_REQUEST`. Messages never contain a raw Revit exception, a stack trace, a path, a user, machine, process, or pipe name. No retries.

## Transport-neutral request

```text
QueryParameterBindingsRequest
{
    document_id: string
    name_contains?: string
    identity_kind?: "all" | "shared" | "local" | "built_in"
    binding_kind?: "all" | "instance" | "type"
    category_tokens?: string[]
    page_size?: integer
    cursor?: string
}
```

No `instance_id`, provider data, numeric `ElementId`, projection, or ordering. Snake_case JSON. Contract types in `BimIt.Revit.Contracts`, with no Revit API type. The CAP-0004 identity and data-type contract types are reused, not copied.

## Bridge

A new Bridge capability gate, following the BRIDGE-0013 explicit version-set pattern, with RPC `revit.query_parameter_bindings`. The protocol number is allocated with the implementation after acceptance. This specification allocates none. A host that did not negotiate it is refused with `BRIDGE_PROTOCOL_INCOMPATIBLE` and receives no fallback to `revit_describe_parameters`. Production `tools/list` would grow from 19 to 20 with the implementation.

## Text and encoding

Definition and category names are Unicode and unmodified. The baseline has 23 definition names with non-ASCII characters (for example `É`, `Ê`, `Â`, `Î`). They must round-trip exactly through the Addin, the Bridge, the Server, and the MCP result. The pyRevit baseline needed a runtime switch because IronPython 2.7 failed on them. BIM-IT's typed contract path must not.

## Security and data minimization

The result never contains: paths, shared-parameter file locations, user, machine, process, or pipe information, cloud identifiers, numeric `ElementId`s, `UniqueId`s, parameter values, approval or standard claims. Names such as a firm prefix are data for the agent, never a classification. `document_id`, `parameter_ref`, and `next_cursor` are chaining identifiers, not authorization tokens.

## Compatibility

Revit 2025, 2026, and 2027 from the shared Addin project. `ElementId` value access, `Category.BuiltInCategory`, `Definition.GetDataType()`, and `InternalDefinition.GetParameterTypeId()` stay inside the multi-version layer. All variants must compile. CAP-0001 to CAP-0014 behavior is unchanged, apart from the internal classifier refactoring, which must not change any CAP-0004 or CAP-0005 output.

## Acceptance criteria

### Automated (with the implementation)

1. Input bounds and enum validation, including `page_size` 0 and 101, 21 tokens, duplicates, whitespace-only names, cursor length.
2. Category-token validation identical to CAP-0014.
3. Category any-match: a row matched by one token still returns its complete category set.
4. `identity_kind`, `binding_kind`, `name_contains` semantics, including `É`/`é` case folding without accent folding.
5. `parameter_ref` parity: for a definition that CAP-0004 observes on an element, the same definition and source yield the same reference, identity, and data type from this capability and from `revit_describe_parameters`, in both call orders. A binding row's reference resolves through the shared identity service. With an element that exposes the parameter on the bound source, `revit_get_parameter_values` reads its value. With an element that does not expose it, for example a hidden definition, the row stays valid and CAP-0005 returns its existing item-level result, not a capability failure.
6. Classifier parity between the `Parameter` and `Definition` paths for built-in, shared, and local identities and for data types, including a definition whose `GetParameterTypeId()` returns null, which must classify as shared or local without raising.
7. Unknown or unreadable category yields `unknown` and is not dropped; empty category set yields `[]`.
8. A non-instance, non-type binding fails with `REVIT_EXECUTION_FAILED`.
9. Ordering follows the four keys (`OrdinalIgnoreCase` name, `Ordinal` name, identity kind, stable key), is total and independent of iteration order, and never uses `parameter_ref`.
10. Hidden definitions (`Visible == false`) are returned as ordinary rows, with no `visible` field.
11. Exact summary and its invariants on every page; empty match is all zeros.
12. Pagination equivalence across page sizes, including a page size change mid-traversal; final-page semantics.
13. `INVALID_CURSOR` for malformed, forged, wrong-document, changed-filter, and **family-type-query** cursors; `STALE_CURSOR` after a revision change.
14. Budget: a page with a many-category row is shortened, loses and repeats no row; `RESULT_TOO_LARGE` only for an oversize single row.
15. Document guard, no active document, family document.
16. No transaction is opened.
17. Bridge gating without fallback; strict closed MCP schemas; `tools/list` 20; read-facade policy.
18. CAP-0004, CAP-0005, CAP-0014 tests pass unmodified.

### Live gate

Contract validation, analogous to CAP-0014's record: a binding change made in Revit between two pages returns `STALE_CURSOR`, and the restart succeeds. Passed through the typed Bridge on 2026-11-06; see [CAP_0015_PARAMETER_BINDINGS_BRIDGE_VALIDATION.md](../CAP_0015_PARAMETER_BINDINGS_BRIDGE_VALIDATION.md).

Natural-agent gate, on an equivalent state of the reference project, compared with the frozen independent pyRevit baseline:

| Fact | Baseline |
|---|---|
| `matched_definition_count` (no filters) | 144 |
| `shared_count` | 73 |
| `local_count` (non-shared) | 71 |
| `instance_bound_count` | 138 |
| `type_bound_count` | 6 |
| `distinct_category_count` | 109 |
| sum of `categories.length` over the traversal | 354 |
| rows that fail classification | 0 |

These are evidence from one document state, not product constants. If the model changed, the baseline is re-measured first by a method independent of BIM-IT. The gate requires:

- an isolated client that can reach only `BimIt.Revit.Server`;
- no tool, field, or schema coaching in the prompt;
- traversal of every page to `complete: true`, with distinct `parameter_ref`s equal to `matched_definition_count` and an identical summary on every page;
- no generated code and no model change;
- zero BIM-IT, Revit API, or runtime failures;
- the frozen facts reproduced, and at least one accented name intact;
- per-page elapsed time recorded.

## Explicitly deferred

- parameter group (`GetGroupTypeId`);
- `Visible` (hidden definitions are inventoried without the field; see "Hidden definitions") and `VariesAcrossGroups`;
- an inventory of every `ParameterElement` or `SharedParameterElement`, bound or not;
- parameter values and formulas;
- family-document parameters;
- schedule parameters and fields;
- linked-document bindings;
- category type metadata;
- data-type, GUID, or exact-name filters;
- projection, caller ordering, grouping, caching, snapshots;
- any write, including creating or changing bindings.

## Decisions recorded at Tech Lead review

The Tech Lead contract review on 2026-11-05 decided the open questions of the first draft. The specification was accepted by the Product Owner and Tech Lead on 2026-11-05 with these decisions.

1. **Hidden definitions.** All binding-map entries are inventoried, including `Visible == false` definitions. No `visible` field in v1 and no exclusion. Chaining to CAP-0005 is conditional on the element exposing the parameter (see "Rule" and "Hidden definitions").
2. **Page size.** Default 50, maximum 100.
3. **Ordering.** `name` with `OrdinalIgnoreCase`, then `name` with `Ordinal`, then identity kind, then stable identity key.
4. **`built_in_count`.** Kept.
5. **Cursor codec.** The CAP-0014 cursor and fingerprint primitive is generalized into a narrowly scoped, source-tagged query cursor shared by declarative query sources. No generalized query framework beyond the cursor and fingerprint mechanics.
6. **Definition classification.** Null-safe: the result of `GetParameterTypeId()` is classified as built-in only when it is non-null and non-empty (see "Definition classification").

No contract question remains open for this specification.

## References

- ADR-0005, ADR-0006, ADR-0022, ADR-0023
- CAP-0004 `revit_describe_parameters`, CAP-0005 `revit_get_parameter_values`, CAP-0014 `revit_query_family_types`
- BRIDGE-0013, EXEC-0001
- [ADR_0023_PARAMETER_BINDINGS_PYREVIT_BASELINE.md](../ADR_0023_PARAMETER_BINDINGS_PYREVIT_BASELINE.md)
- Autodesk Revit API: `Document.ParameterBindings`, `DefinitionBindingMapIterator`, `InternalDefinition`, `SharedParameterElement`, `InstanceBinding`, `TypeBinding`, `ElementBinding.Categories`, `Definition.GetDataType`
