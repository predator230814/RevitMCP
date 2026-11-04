# CAP-0014: `revit_query_family_types`

- Status: Accepted
- Operation class: Read
- Date: 2026-11-03
- Architecture: Accepted [ADR-0023](../adr/ADR-0023-declarative-revit-read-query-architecture.md)
- Introduced bridge protocol version: 22

## Authorization

This specification is **Accepted**. Product Owner and Tech Lead approved the capability contract after review. It is the first implementation slice of Accepted ADR-0023. The Contracts, Addin read, Bridge protocol v22, and MCP tool are implemented. Live Revit validation has not been run.

Where this specification and ADR-0023 differ, ADR-0023 wins and this document is corrected.

## Purpose

`revit_query_family_types` returns a complete, deterministic, paged inventory of the `FamilySymbol` types in the active host project document. Each row carries Revit facts about the type and how many instances of it are placed. A query-level summary describes the whole matched set. The result is suitable input for a future Standard Pack, where a human decides what is approved.

Typical agent use:

```text
revit_get_context
-> document_id
-> revit_query_family_types (category_tokens, usage, ...)
-> next_cursor
-> revit_query_family_types (same query + cursor) ... until complete: true
```

It is **not**:

- a placement catalogue. `revit_get_mep_device_types` stays unchanged and keeps its placement-specific purpose;
- a Tt standards catalogue;
- a family loader;
- a generic Revit API executor;
- a system `ElementType` inventory;
- a linked-model inventory.

## Operation class and safety

Read-only.

- The capability opens no `Transaction`, `SubTransaction`, or `TransactionGroup`, and calls no Revit API that modifies the document.
- No model-supplied Python, C#, Dynamo, expression, or Revit API source is accepted or executed. The request cannot carry code, expressions, or .NET, runtime, or Revit API type or member names that would select executable API members, reflection targets, or runtime behavior. Family names, Revit `FamilySymbol` type names (`type_name_contains`), and category tokens are ordinary BIM data and are valid closed filter values.
- All live Revit API access runs through the existing valid Revit execution context (EXEC-0001).
- The implementation is trusted compiled BIM-IT code. Read-only safety comes from the closed request vocabulary plus that trusted implementation, as ADR-0023 section 8 states. It does not come from MCP annotations or from a process sandbox.

## MCP tool

### Name

`revit_query_family_types`

### Title

`Query Revit Family Types`

### Description

The description must let an agent use the tool without prior schema coaching. It states, in this order:

1. what it returns: `FamilySymbol` types of the active host project document, with placed-instance counts and a summary of the whole matched set;
2. that it is a paged inventory: repeat the same query with `next_cursor` until `complete` is `true`;
3. that `document_id` comes from `revit_get_context`;
4. that category filters use stable `OST_` tokens and name filters are case-insensitive substrings.

Exact wording is an implementation detail. It must stay concise (ADR-0005).

### Annotations

- `readOnlyHint: true`
- `openWorldHint: false`

Annotations are descriptive hints. They are not the security boundary. The tool must satisfy the BIM-IT Assistant read-facade policy (ADR-0022): a `revit_` name, read-only, not destructive, closed world, an object input schema that includes `instance_id`, and a description.

## MCP input

Closed object. `additionalProperties` is false.

```json
{
  "instance_id": "optional Server routing identity",
  "document_id": "required active-document guard",
  "category_tokens": ["OST_ElectricalFixtures"],
  "family_name_contains": "Tt_INS",
  "type_name_contains": "15A",
  "usage": "all",
  "page_size": 100,
  "cursor": null
}
```

Malformed MCP types and unknown properties are Server `INVALID_REQUEST`. A JSON `null` for an optional field means the field is omitted.

### `instance_id`

Optional. Server routing only, with the same ADR-0003 semantics as the other read tools. It never enters the transport-neutral Bridge request.

### `document_id`

Required string. Opaque active-document guard from ADR-0006.

- A present string that ordinal-matches the active open-document identity continues.
- A present string that does not ordinal-match returns `DOCUMENT_CONTEXT_CHANGED`. The capability never switches to another document.
- An empty or whitespace string is present text and is compared exactly, so it does not match.
- Missing or `null` is `INVALID_REQUEST` at the MCP layer.

### `category_tokens`

Optional array of strings.

- Each value is a stable Revit `BuiltInCategory` enum member name, for example `OST_ElectricalFixtures`, `OST_ElectricalEquipment`, `OST_LightingFixtures`. This matches the existing BIM-IT category-token usage (the `CategoryToken` of `revit_get_mep_device_types`).
- 1 to 20 values when supplied. An empty array is invalid.
- Duplicates (ordinal equality) are invalid.
- Semantics: OR within the array. It ANDs with every other filter.
- Any defined `BuiltInCategory` member is accepted. There is no MEP allowlist.
- A token that matches a defined member but has no `FamilySymbol` in the document is valid and contributes zero matches.
- A value that is not the exact, case-sensitive name of a `BuiltInCategory` member of the running Revit version, a numeric string, or the member `INVALID` is `INVALID_FAMILY_TYPE_QUERY`.
- Localized category display names are not accepted as input. The result carries both the token and the localized display name.

Because validity depends on the enum of the running Revit version, defined-ness is checked in the Addin. Shape (count, length, duplicates, characters `A-Z a-z 0-9 _`, at most 80 characters per value) is checked before Revit execution.

### `family_name_contains` and `type_name_contains`

Each is optional, 1 to 256 characters when supplied.

- Case-insensitive substring match with `StringComparison.OrdinalIgnoreCase` semantics. This folds case (`É` matches `é`). It does not fold accents (`E` does not match `É`).
- No regex, no wildcards, no expression language, no exact-name arrays.
- A value made only of whitespace is `INVALID_FAMILY_TYPE_QUERY`. The value is otherwise used exactly, with no trimming.
- `family_name_contains` matches `family_name`. `type_name_contains` matches `type_name`. Both must match when both are supplied.

### `usage`

Optional enum, default `all`.

| Value | Matches a type when |
|---|---|
| `all` | always |
| `placed` | `placed_instance_count > 0` |
| `unused` | `placed_instance_count == 0` |

Any other value is `INVALID_REQUEST` at the MCP layer and `INVALID_FAMILY_TYPE_QUERY` at the transport-neutral layer.

### `page_size`

Optional integer.

```text
default: 100
minimum: 1
maximum: 200
```

`page_size` is a maximum. A page may return fewer rows (see "Result budget"). There is no unbounded mode. A present value that is not an integer or is outside 1..200 is `INVALID_REQUEST` at the MCP layer and `INVALID_FAMILY_TYPE_QUERY` at the transport-neutral layer.

`page_size` is not part of the query fingerprint. A client may change it between pages of one traversal.

### `cursor`

Optional opaque continuation string.

- A client passes `next_cursor` back unchanged. It must not parse, construct, or modify it.
- MCP: a string of 1 to 2048 characters. Anything else is `INVALID_REQUEST`. Transport-neutral: a present value that is empty or longer than 2048 characters is `INVALID_CURSOR`.
- The query must be identical to the one that produced the cursor, except for `page_size`. This includes `document_id`, `category_tokens`, both name filters, and `usage`.

## Query population

The source is **all `FamilySymbol` types in the active host project document**.

Include:

- loadable families;
- in-place families, when Revit exposes them as `FamilySymbol`s.

Never discard a `FamilySymbol` because of its `FamilyPlacementType`, its category, or because BIM-IT placement workflows cannot place it. A type that `revit_get_mep_device_types` omits, such as a `CurveBased` or `ViewBased` type, must appear here.

Exclude:

- system `ElementType`s (walls, floors, conduit, cable tray, wire, pipe, duct, and similar);
- `FamilySymbol`s that exist only in a linked document;
- the contents of family documents;
- instances as result rows.

A symbol whose `Family` cannot be resolved is a capability failure (`REVIT_EXECUTION_FAILED`). It is never silently omitted.

Family documents are unsupported (see "Errors").

## Result

Closed object. `additionalProperties` is false at every level.

```json
{
  "context": {
    "instance_id": "opaque",
    "document_id": "opaque"
  },
  "summary": {
    "matched_type_count": 0,
    "distinct_family_count": 0,
    "placed_type_count": 0,
    "placed_instance_count": 0,
    "used_family_count": 0,
    "unused_family_count": 0
  },
  "returned_count": 0,
  "complete": true,
  "next_cursor": null,
  "types": []
}
```

### Type row

```json
{
  "type_ref": "opaque",
  "family_ref": "opaque",
  "category_token": "OST_ElectricalFixtures",
  "category_name": "Electrical Fixtures",
  "family_name": "Example_Family",
  "type_name": "Example Type",
  "family_placement_type": "one_level_based",
  "family_is_in_place": false,
  "placed_instance_count": 12,
  "is_used": true
}
```

All ten fields are required.

- **`type_ref`, `family_ref`:** opaque ADR-0006 references to the `FamilySymbol` and its `Family`. They are meaningful only with the matching `document_id`. The initial implementation may derive them from Revit stable identity (`Element.UniqueId`). Clients must not parse them or depend on their representation. A family name is human-readable and is not cross-call identity.
- **`category_token`:** the `BuiltInCategory` enum member name of the symbol's category. If the category is null or its value is not a defined `BuiltInCategory` member, the token is the literal `unknown` and the row is still returned. Only a query without `category_tokens` can return such rows.
- **`category_name`:** the localized Revit category display name, or an empty string when the category is null.
- **`family_name`, `type_name`:** the Revit names, returned as Unicode text and unmodified (see "Text and encoding").
- **`family_placement_type`:** a token from the closed table below.
- **`family_is_in_place`:** `Family.IsInPlace`. It distinguishes in-place families from loadable ones.
- **`placed_instance_count`:** see "Instance counting".
- **`is_used`:** `placed_instance_count > 0`.

### Family placement type tokens

Revit 2025, 2026, and 2027 define the same ten `FamilyPlacementType` members, read from `RevitAPI.dll` metadata for each year on 2026-11-03. The mapping is closed and covers all of them:

| Revit member | Token |
|---|---|
| `OneLevelBased` | `one_level_based` |
| `OneLevelBasedHosted` | `one_level_based_hosted` |
| `TwoLevelsBased` | `two_levels_based` |
| `ViewBased` | `view_based` |
| `WorkPlaneBased` | `work_plane_based` |
| `CurveBased` | `curve_based` |
| `CurveBasedDetail` | `curve_based_detail` |
| `CurveDrivenStructural` | `curve_driven_structural` |
| `Adaptive` | `adaptive` |
| `Invalid` | `invalid` |

If Revit returns a value that is not in this table, or the value cannot be read, the token is `unknown`. The type is still returned. The existing three-value mapping of `revit_get_mep_device_types` is not reused. The set of tokens in a result is `{one_level_based, one_level_based_hosted, two_levels_based, view_based, work_plane_based, curve_based, curve_based_detail, curve_driven_structural, adaptive, invalid, unknown}`. A new Revit release that adds an enum member yields `unknown` until this specification is amended.

### Instance counting

`placed_instance_count` is the number of elements in the **active host project document** that are not element types and whose type is that `FamilySymbol`. It:

- includes instances inside groups, when Revit exposes them as project-document elements;
- includes instances in any design option, when they exist as project-document elements;
- includes nested shared family instances, when Revit exposes them as project-document elements;
- excludes everything that exists only in a linked document;
- counts each element once.

It does not depend on the active view, a view filter, the active design option, or worksets. A symbol with no such element has `placed_instance_count` 0 and `is_used` false.

### Query-level summary

Every page returns the same exact `summary` over the **entire matched set**, not the rows of that page. The matched set is every `FamilySymbol` that satisfies `category_tokens`, both name filters, and `usage`.

| Field | Definition |
|---|---|
| `matched_type_count` | number of matched `FamilySymbol`s |
| `distinct_family_count` | number of distinct `Family` elements among matched symbols, identified by `family_ref`, not by name |
| `placed_type_count` | matched symbols with `placed_instance_count > 0` |
| `placed_instance_count` | sum of `placed_instance_count` over all matched symbols |
| `used_family_count` | matched families with at least one matched symbol that has `placed_instance_count > 0` |
| `unused_family_count` | number of distinct matched families for which **every matched** symbol has `placed_instance_count == 0` under the counting semantics above |

Family-level counts are computed over the matched set. They are internally consistent with the rows and filters:

- `used_family_count + unused_family_count == distinct_family_count`;
- `placed_type_count <= matched_type_count`;
- `placed_instance_count` equals the sum of the rows' `placed_instance_count` across a completed traversal;
- the sum of `returned_count` over a completed traversal equals `matched_type_count`.

Family classification is relative to the matched set. With `usage: "unused"` or a name filter, a family can have placed types outside the matched set and still count in `unused_family_count` when every matched symbol is unplaced. For an unfiltered family classification, use `usage: "all"` with no name filters, optionally narrowed by `category_tokens`.

An empty match returns all six values 0.

## Ordering

Ordering is defined by the capability. The caller cannot configure it. The total order is, in precedence:

1. `category_token`
2. `family_name`
3. `type_name`
4. `type_ref`

Each key is compared with ordinal comparison of UTF-16 code units (`string.CompareOrdinal` semantics): case-sensitive, no culture, no Unicode normalization, ascending. Uppercase ASCII therefore sorts before lowercase, and accented letters sort after ASCII letters. The ordering is chosen for reproducibility, not for natural reading order. `type_ref` is a final tie-breaker. The three name keys already make ties practically impossible, since Revit family names are unique per document and type names are unique per family.

The implementation must not depend on `FilteredElementCollector` iteration order. The same query against an unchanged document returns the same sequence on every call.

## Pagination

Fields:

- `complete` (boolean, required);
- `next_cursor` (string or `null`, required);
- `returned_count` (integer, required, the number of rows in `types`).

A non-final page returns `complete: false` and a non-null `next_cursor`. The final page returns `complete: true` and `next_cursor: null`. A client decides that a traversal has ended only from `complete: true`. It must not infer the end from a missing cursor, from a short page, or from a `truncated` flag. This capability has no `truncated` field.

The final page is the page that contains the last matched row. A page that exactly fills `page_size` and contains the last matched row is final. An empty match returns `types: []`, `complete: true`, `next_cursor: null`.

Each page returns the rows that follow, in the total order, the position held by the cursor. The first page (no cursor) starts at the first row.

### Cursor binding

A cursor is opaque and server-minted. It is bound to:

- the Revit instance and document identity;
- a normalized fingerprint of the query (`category_tokens` as a set, both name filters, and `usage`; not `page_size`);
- the document query revision at the time of the first page of the traversal;
- the paging position.

The cursor encoding and any state kept for it are implementation details. The observable behavior below is the contract.

### Document query revision

The Addin maintains a document-scoped query revision (epoch) that advances whenever a change to the host document that Revit makes observable to the Addin could affect the family/type inventory or its instance counts. The implementation must use Revit lifecycle and change events sufficient to preserve this invariant, including worksharing synchronization effects where applicable.

This specification does not claim that any particular Revit event guarantees the invariant. The implementation must name the events it uses and record its evidence in its validation record. Advancing the revision more often than necessary is allowed, because it only forces a restart. A change that affects the inventory and does not advance the revision is a defect.

A traversal is complete only if every page was produced at the same revision. The revision is read and the inventory is enumerated within one Revit execution work item.

Observable behavior:

- A cursor created at revision N is valid only while the document's revision is N.
- If the revision has advanced when the next page is requested, the result is `STALE_CURSOR`. The client restarts the traversal without a cursor.
- A cursor is never silently re-based onto a newer revision.
- When the document is closed and reopened, or the Addin or Revit restarts, `document_id` changes (ADR-0006). The client's old `document_id` no longer matches, so the call returns `DOCUMENT_CONTEXT_CHANGED` before any cursor is examined.

### Cursor validation order

1. Validate the transport-neutral request shape.
2. Execute through EXEC-0001, require a project document, and apply the document guard.
3. If a cursor is present, check that it is a cursor this Addin issued, for this document and this query fingerprint. Otherwise return `INVALID_CURSOR`.
4. Compare the cursor revision with the current revision. If they differ, return `STALE_CURSOR`.
5. Execute the query and shape the page.

## Execution

1. Validate the transport-neutral request before Revit execution.
2. Execute Revit API access only through EXEC-0001.
3. Require an `ActiveUIDocument` (`NO_ACTIVE_DOCUMENT`) and a project document (`UNSUPPORTED_DOCUMENT_KIND`).
4. Compare `document_id` with ordinal equality to the active document identity (`DOCUMENT_CONTEXT_CHANGED`).
5. Validate any cursor (above).
6. Read the current document query revision.
7. Enumerate matched `FamilySymbol`s and count their instances.
8. Compute the exact summary over the whole matched set.
9. Order, position, and take the page.
10. Create no `Transaction`, `SubTransaction`, or `TransactionGroup`.

### Cost and caching (v1)

The simplest correct behavior is required:

- no retained full inventory snapshot;
- no query-result cache;
- every page may recompute the matched inventory and the exact summary from the current, unchanged document;
- the revision guard, not a snapshot, prevents a traversal from mixing model states.

Caching and snapshot optimization are explicitly deferred until live measurements justify them.

### Timeout

v1 uses the existing capability execution timeout unchanged. The repository value is 15 seconds (`ServerTimeouts.CapabilityTimeout`). No capability-specific timeout is introduced, and this specification adds no fixed element-scan limit.

The live benchmark records elapsed time per page. If representative queries cannot complete within the existing timeout, optimization versus a future timeout change is decided from that evidence.

If execution exceeds the accepted timeout, the call fails with `REVIT_EXECUTION_TIMEOUT`. No partial result is returned, and no result is represented as complete. Under EXEC-0001, work that has already started inside Revit is allowed to finish, and its result is discarded. A timeout does not prove that Revit is dead.

## Result budget

The tool respects two bounds:

- `page_size <= 200`;
- the serialized structured result stays within a 120,000-character compatibility target, which equals the BIM-IT Assistant read-result budget (`AssistantMcpReadFacade.MaxResultCharacters`). The Addin and Contracts must not reference the Assistant assembly, and no reverse dependency from core or Revit code to `BimIt.Revit.Assistant` is allowed. The implementation uses either a provider-neutral shared limit that is accessible without depending on `BimIt.Revit.Assistant`, or explicit parity tests proving that the capability budget and the Assistant facade budget remain compatible.

If a page of `page_size` rows would exceed the budget, the capability returns a **shorter page**: the longest prefix of the page's rows, in the total order, that fits. This is correct pagination because `page_size` is a maximum and only `complete` ends a traversal:

- the shorter page has `complete: false` and a `next_cursor` positioned after its last returned row;
- no row is skipped or repeated;
- `returned_count` is the number of rows returned;
- the summary is unchanged.

A page never has zero rows unless the matched set is empty. If even the first row plus the summary cannot fit, the capability returns `RESULT_TOO_LARGE`. This is not expected with the specified fields.

The budget is measured on the serialized structured content as the MCP client receives it, including escaping of non-ASCII text.

## Text and encoding

Family names, type names, and category names are returned as Unicode text, unmodified. Accented French names such as `L1 (GÉNÉRAL)` or `14-50R CUISINIÈRE`, which occur in the reference project, must round-trip exactly through the Addin, the Bridge, the Server, and the MCP result. No caller-side encoding workaround may be needed.

## Errors

Server routing, unchanged from the ordinary read tools:

```text
NO_REVIT_INSTANCE
INSTANCE_REQUIRED
INSTANCE_NOT_FOUND
INSTANCE_UNAVAILABLE
```

Reused capability errors:

```text
NO_ACTIVE_DOCUMENT
DOCUMENT_CONTEXT_CHANGED
UNSUPPORTED_DOCUMENT_KIND      (the active document is a family document)
REVIT_EXECUTION_TIMEOUT
REVIT_EXECUTION_FAILED
```

Bridge compatibility, existing:

```text
BRIDGE_PROTOCOL_INCOMPATIBLE   (the host did not negotiate protocol 22)
```

New capability errors (the minimum for this capability):

```text
INVALID_FAMILY_TYPE_QUERY      invalid filters, bounds, or transport-neutral shape
INVALID_CURSOR                 malformed, forged, or mismatched cursor
STALE_CURSOR                   the document query revision changed; restart without a cursor
RESULT_TOO_LARGE               a single row cannot fit the result budget
```

`INVALID_FAMILY_TYPE_QUERY` follows the existing per-capability pattern (`INVALID_SELECTION`, `INVALID_MEP_DEVICE_TYPE_CATALOG`) so it never changes the meaning of `INVALID_QUERY`, which belongs to `revit_query_elements`.

Malformed MCP input maps to `INVALID_REQUEST`. It is not converted to `INVALID_FAMILY_TYPE_QUERY`.

Errors are compact and deterministic. A message never contains a raw Revit exception, a stack trace, a filesystem path, a user name, a machine name, a process id, or a pipe name. Do not add retries.

## Transport-neutral request

Conceptually:

```text
QueryFamilyTypesRequest
{
    document_id: string
    category_tokens?: string[]
    family_name_contains?: string
    type_name_contains?: string
    usage?: "all" | "placed" | "unused"
    page_size?: integer
    cursor?: string
}
```

It contains no `instance_id`, MCP metadata, provider information, process id, pipe name, file path, user identity, agent-controlled timeout, numeric `ElementId` values, projection, or ordering. Omitted `usage` is `all` and omitted `page_size` is 100 before execution. JSON property names are snake_case. Contract types live in `BimIt.Revit.Contracts` and must not reference Autodesk Revit API types.

## Bridge

Allocate **Bridge protocol v22** for this capability, following the existing cumulative compatibility pattern (BRIDGE-0013 and the later version sets in `BridgeProtocol`):

- the capability matrix is explicit set membership, not a numeric `version >= N` rule;
- `query_family_types` is `{22}`;
- every existing capability set gains 22, and `CurrentVersion` becomes 22;
- a full v22 host advertises `[22, 21, ..., 1]`;
- a host advertises 22 only when it implements this capability;
- a host that did not negotiate 22 never receives `revit.query_family_types`. The Server returns `BRIDGE_PROTOCOL_INCOMPATIBLE` and does **not** fall back to another tool or pretend the capability exists. In particular it does not substitute `revit_get_mep_device_types` or `revit_query_elements`.

The RPC name is `revit.query_family_types`. This specification does not implement Bridge v22 and does not write the BRIDGE document. That document is created with the implementation, from this specification.

## MCP structured output

The tool follows the modern structured-content policy of the accepted capabilities:

- a strict output schema (closed objects, all required fields declared, the placement and `usage` enums listed);
- `structuredContent` is the authoritative result;
- no duplicated full JSON text in `content` on a structured-content-capable MCP client;
- errors are compact, with `isError = true`, no `structuredContent`, and one compact JSON text error.

```text
success: isError = false, structuredContent = QueryFamilyTypesResult, content = []
error:   isError = true,  structuredContent absent, one compact JSON TextContent error
```

Production `tools/list` grows from 18 to 19 tools when the Server implements this capability. That count change is part of the implementation, not of this specification.

## Security and data minimization

The result never contains:

- filesystem paths, `.rfa` source paths, or library locations;
- user names;
- machine, process, or pipe information;
- cloud project or model identifiers;
- approval or standard claims;
- arbitrary parameter values;
- geometry;
- `FamilySymbol.IsActive`, worksets, phases, design-option names, or levels.

`Tt approved`, `preferred`, `company standard`, and `.rfa` provenance are **not Revit facts**. They belong to future Standard Packs and human governance (ADR-0022). A `Tt_` name prefix or any other naming pattern is data for the agent to interpret. The capability asserts no classification from names.

`document_id`, `type_ref`, `family_ref`, and `next_cursor` are identifiers for chaining. They are not authorization tokens. `readOnlyHint` is not a write-authorization substitute.

## Compatibility

BIM-IT targets Revit 2025, 2026, and 2027 from one shared Addin project. Revit-version API differences, such as the `ElementId` value accessor, stay inside the existing multi-version compatibility layer (ADR-0004). Numeric `ElementId` values and Revit API objects never enter Contracts or the MCP result. The Revit 2025, 2026, and 2027 variants must compile. Compile-time availability is not live proof.

CAP-0001 through CAP-0013, `revit_get_mep_device_types`, and `revit_query_elements` remain behaviorally unchanged.

## Acceptance criteria

### Automated

Tests are required with the implementation and must cover at least:

1. **Input bounds.** `page_size` 0, 201, non-integer; `category_tokens` empty, 21 values, duplicates; name filters empty, 257 characters, whitespace only; `cursor` empty and over 2048 characters; missing `document_id`.
2. **Category-token validation.** A defined token with zero symbols returns zero matches. A non-enum string, a numeric string, a wrong-case token, a localized display name, and `INVALID` are `INVALID_FAMILY_TYPE_QUERY`. A token outside the former MEP allowlist is accepted.
3. **Name filtering.** Case-insensitive substring on family and type; `É` matches `é`; `E` does not match `É`; both filters AND; no regex or wildcard behavior (`.` and `*` match literally).
4. **Usage filtering.** `all`, `placed`, `unused`, and the default.
5. **Deterministic ordering.** Ordinal, case-sensitive order on the four keys; identical sequence across repeated calls and independent of collector order.
6. **Exact summary.** The six fields and the invariants in "Query-level summary", including family-level counts under `usage` and name filters; every page of a traversal returns the same summary; an empty match returns zeros.
7. **All placement mappings.** One test per row of the token table, and `unknown` for an unlisted value and for an unreadable value.
8. **In-place vs loadable.** `family_is_in_place` is correct for both.
9. **Placement-unsupported types are not dropped.** `CurveBased`, `ViewBased`, `Adaptive`, and `TwoLevelsBased` types appear, although `revit_get_mep_device_types` omits them.
10. **Instance counting.** Group members, design-option elements, nested shared family instances, and zero-instance symbols; linked-document instances excluded.
11. **Pagination across multiple pages.** Concatenated pages equal the full ordered set with no gap or duplicate; the same result for `page_size` values 1, 25, 100, and 200, and when `page_size` changes between pages.
12. **Final-page semantics.** `complete: true` with `next_cursor: null` exactly on the page holding the last row; a full page that holds the last row is final; an empty match is final.
13. **Invalid cursor.** Malformed, forged, wrong document, and a changed filter each return `INVALID_CURSOR`.
14. **Stale cursor.** After a simulated revision change, the next page returns `STALE_CURSOR`, and a restart without a cursor succeeds.
15. **Document guard.** Mismatch and empty string return `DOCUMENT_CONTEXT_CHANGED`; no active document returns `NO_ACTIVE_DOCUMENT`; a family document returns `UNSUPPORTED_DOCUMENT_KIND`.
16. **Result budget.** A page that would exceed 120,000 serialized characters is shortened to a prefix, keeps `complete: false` and a correct `next_cursor`, and loses and repeats no row; `RESULT_TOO_LARGE` for a single oversize row; the budget constant matches the Assistant's.
17. **Read-only execution.** No `Transaction`, `SubTransaction`, or `TransactionGroup` is created in a run.
18. **Bridge v22 gating.** A v22 host advertises the capability. A v21 host is refused with `BRIDGE_PROTOCOL_INCOMPATIBLE` and receives no fallback.
19. **MCP schemas and annotations.** The input and output schemas are closed and strict, `readOnlyHint` is true, `openWorldHint` is false, `tools/list` is 19, the tool passes the Assistant read-facade policy, and no `content` duplicate appears on success.
20. **Text.** A name with `É` or `È` round-trips unchanged through Contracts serialization and the MCP result.
21. **Existing tools unchanged.** `revit_get_mep_device_types` and `revit_query_elements` tests pass unmodified.

### Live Revit benchmark gate

On the same electrical reference project, in an equivalent document state, a query over the nine benchmark categories:

```text
OST_ElectricalFixtures, OST_ElectricalEquipment, OST_LightingFixtures,
OST_LightingDevices, OST_DataDevices, OST_CommunicationDevices,
OST_FireAlarmDevices, OST_SecurityDevices, OST_NurseCallDevices
```

must reproduce the ADR-0023 baseline in its `summary`:

| Fact | Baseline |
|---|---|
| `distinct_family_count` | 196 |
| `matched_type_count` | 1,205 |
| `placed_type_count` | 254 |
| `placed_instance_count` | 8,278 |
| `used_family_count` | 87 |
| `unused_family_count` | 109 |

The numbers are evidence from one reference project at one point in time, not product constants. If the model changed after the baseline, the baseline is re-measured on the same document state first, by a method independent of BIM-IT, and the document state is recorded. Any difference is explained against the counting semantics in this specification. The baseline counted instances by category. This capability counts them by `FamilySymbol`, and equality is expected.

The live test must:

- use an isolated MCP client connected only to `BimIt.Revit.Server`;
- complete without Python, C#, or Dynamo scripts;
- complete without pyRevit, Nonica, or any other Revit MCP;
- use no prompt-level schema coaching (the prompt states the goal only, and the tool description and schema are enough);
- not modify the model;
- have no failed call caused by a Revit API, runtime, or version quirk;
- page through every page to an explicit `complete: true`;
- verify that the sum of `returned_count` equals `matched_type_count`, that every page returned an identical `summary`, and that at least one accented French family or type name survives exactly;
- record per-page elapsed time, so that a decision on caching has measurements.

Live validation does not replace the automated tests, and is not a merge verdict.

## Explicitly deferred

- system `ElementType`s;
- linked-document inventory;
- arbitrary projection;
- caller-selected ordering;
- a generic group-by or distinct language;
- instance rows;
- parameter values;
- view templates;
- schedules;
- shared parameter bindings;
- migration of `revit_get_mep_device_types` to the query kernel;
- migration or pagination of `revit_query_elements`;
- caching or retained inventory snapshots;
- any write, preview, or approval.

## Decisions recorded at acceptance

Product Owner and Tech Lead approved the contract and decided the three questions raised in review:

1. **Execution timeout.** v1 accepts the existing 15-second capability timeout (`ServerTimeouts.CapabilityTimeout`) unchanged. No capability-specific timeout is introduced. The live benchmark records elapsed time per page. If representative queries cannot complete within the existing timeout, optimization versus a future timeout change is decided from that evidence.
2. **Error name.** `INVALID_FAMILY_TYPE_QUERY` is the accepted v1 validation error. The repository already uses capability-specific invalid-request codes, and `INVALID_QUERY` stays scoped to `revit_query_elements`.
3. **Shorter page on budget overflow.** Accepted. `page_size` is a maximum. If a page would exceed the structured-result budget, the capability returns the longest ordered prefix that fits, with `complete: false`, a correct `next_cursor`, no row skipped or repeated, and an unchanged summary. `RESULT_TOO_LARGE` applies only when one row plus the required envelope and summary cannot fit.

No question remains open for this specification.

## References

- ADR-0003: Revit instance registration, discovery, and addressing
- ADR-0004: solution structure and multi-version build
- ADR-0005: agent context and token efficiency
- ADR-0006: document and element reference identity
- ADR-0022: Agent Skills and knowledge architecture (read facade)
- ADR-0023: declarative Revit read-query architecture
- BRIDGE-0013: Bridge protocol version-set pattern
- EXEC-0001: serialized Revit execution dispatcher
- CAP-0002: `revit_query_elements`
- CAP-0010: `revit_get_selection`
- `revit_get_mep_device_types` (ADR-0018)
- Autodesk Revit API `FamilySymbol`, `Family.FamilyPlacementType`, `Family.IsInPlace`, `BuiltInCategory`
