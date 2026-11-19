# CAP-0017: Bounded Revit API property access

- Status: Proposed. Not accepted. Not implemented.
- Operation class: Read
- Date: 2026-11-19
- Architecture: Proposed [ADR-0025](../adr/ADR-0025-bounded-revit-api-property-access.md). Does not change Accepted ADR-0023 or ADR-0024.
- Bridge: no new RPC and no protocol bump. Reuses protocol 24 and [CAP-0016](CAP-0016-revit-read-operations.md) `revit_lookup` / `revit_read`.
- MCP: no new tool. `revit_lookup` remains a lookup of BIM-IT registered operations.

This specification is not an implementation task. Production code, tests, Skills, and the Server catalogue stay as they are until a later change is approved against an accepted contract.

## Purpose

Expose long-tail scalar facts on Revit `Element` subclasses without a compiled operation per class and without executing arbitrary code.

Two registered operations:

| Id | Version | Title |
|---|---:|---|
| `revit_api.metadata` | 1 | Revit API element metadata |
| `revit_api.element_properties` | 1 | Read element properties |

`revit_lookup` finds these operations. `revit_read` runs them. `revit_api.metadata` is the operation that searches the Revit API. `revit_lookup` does not search it.

The operation descriptions name the Revit API, elements, and properties so an agent can find the operations. They do not name `Level`, `Phase`, `DesignOption`, or any other benchmark class. There is no per-class alias list.

After a later implementation, the production registry contains these two operations plus the compiled operations already approved, including `project.user_worksets` v1. This proposal does not remove or rewrite those operations.

## Shared execution

Both operations use the CAP-0016 envelope:

- required `document_id`, `operation`, `contract_version`, and `inputs`;
- `page_size` 1..200, default 100, legal because pagination is `cursor`;
- `cursor` optional;
- active project document, or `UNSUPPORTED_DOCUMENT_KIND`;
- `NO_ACTIVE_DOCUMENT` and `DOCUMENT_CONTEXT_CHANGED` keep their CAP-0016 meaning;
- cursor fingerprint includes operation id, contract version, document, instance, and the semantic inputs below;
- `page_size` is not part of the fingerprint;
- a changed document revision is `STALE_CURSOR`;
- the serialized structured result, including the generic envelope, stays within `ReadResultBudget.MaxCharacters` (120,000);
- the existing shrink-to-fit page rule applies: a page may be shorter than `page_size`, rows are not skipped or repeated, and one row that cannot fit is `RESULT_TOO_LARGE` with no data.

Both run on EXEC-0001 because they are `revit_read` operations. Metadata does not read elements. It still requires a reachable host and the active project document, because `revit_read` requires `document_id`. This proposal does not add a document-free metadata RPC.

The trusted assembly is `typeof(Element).Assembly` in the running Revit process. No caller-supplied assembly, no `Assembly.Load`, no `RevitAPIUI`, no plugin assembly.

Schemas are closed objects: `additionalProperties: false` on every object node, using the CAP-0016 schema subset. Where that subset cannot say "exactly one of these fields", semantic validation supplies the rule and the error names the field.

## Search normalization

Used only by `revit_api.metadata` search. It is a deterministic fold over identifiers. It is not a stemmer, not a fuzzy match, and not a table of BIM aliases.

**Tokens.** Split the string before an uppercase letter that follows a lowercase letter or a digit, and on every character that is not an ASCII letter or digit. Lowercase each piece with the invariant culture. Drop empty pieces. Then fold each token once:

- if its length is at least 5 and it ends with `ies`, replace that suffix with `y`;
- otherwise, if its length is at least 4, it ends with `s`, and it does not end with `ss`, drop the final `s`.

**Type tokens.** The tokens of a type's simple name only. Namespace segments are not type tokens.

**Match.** Every query token equals at least one type token, or the raw query is an ordinal-ignore-case substring of the simple name. A query matches a type when either rule hits. Results are ordered by full type name, ordinal.

This is why the following queries find the following simple names:

| Query | Tokens | Simple name |
|---|---|---|
| `levels` | `level` | `Level` |
| `phases` | `phase` | `Phase` |
| `design options` | `design`, `option` | `DesignOption` |

`classes` folds to `classe` and does not match `Class`. That miss is accepted in this proposal. `elevation` does not match `Level`, because property names are not in the search corpus. Exact mode for `Autodesk.Revit.DB.Level` lists `Elevation`.

A query whose token list is empty after the fold is `INVALID_READ_OPERATION_INPUT`.

## `revit_api.metadata` v1

### Inputs

Closed object. Exactly one of:

| Field | Rule |
|---|---|
| `query` | String, length 2..128. Search. |
| `type_name` | String, length 1..256. Exact public full name, ordinal, case-sensitive. |

Neither field, or both, is `INVALID_READ_OPERATION_INPUT`.

### Search result

Public types in the trusted assembly that are `Autodesk.Revit.DB.Element` or a subclass, including abstract types. Non-element types are not search hits.

If more than 200 types match, the operation fails with `INVALID_READ_OPERATION_INPUT`, names `query`, and returns no types. The agent must narrow the query.

Each summary:

| Field | Meaning |
|---|---|
| `type_name` | Full name |
| `base_type_name` | Full name of `BaseType` when that type is in the trusted assembly; otherwise null |
| `is_element` | True |
| `is_abstract` | As declared |
| `matched_tokens` | The query tokens that hit, ordinal, at most 8 |

No property schemas and no methods in a search summary. `matched_type_count` is the full match count. `returned_count` is the page. `complete` and `next_cursor` follow CAP-0016.

The cursor fingerprint for search is the raw `query` string, ordinal, not the token list alone.

### Exact result

One type. The page is `complete: true` with `next_cursor: null`. Exact mode does not page properties across cursors.

Unknown full name: `REVIT_API_TYPE_REJECTED`, reason `unknown`, no payload.

A type outside the trusted assembly is unknown. A public type in the assembly that is not an Element subclass returns one summary with `is_element: false`, `is_abstract` as declared, and no properties. The read operation, not metadata, is what rejects it for collection.

An Element subclass, abstract or concrete, returns its public instance properties:

| Field | Meaning |
|---|---|
| `name` | Ordinal property name |
| `return_type` | Declared type name, with one `Nullable<T>` layer unwrapped in the policy check |
| `can_project` | True only when the scalar-read policy below accepts the getter |
| `rejection` | Absent when `can_project` is true. Otherwise `not_readable`, `indexer`, `static`, `unsupported_return_type`, or `outside_trusted_assembly` |

Order: projectable properties by ordinal name, then rejected properties by ordinal name. There is no `methods` field.

If more than 80 properties are projectable, the operation returns `RESULT_TOO_LARGE` and no type payload. Otherwise every projectable property is present. Rejected properties fill the remainder of the 80 slots. `rejected_omitted_count` is how many rejected properties were not listed. Zero means the rejected list is complete.

Metadata does not invoke getters, does not construct a `FilteredElementCollector`, and does not read `Document.IsModifiable`. The modifiable-document gate belongs to `revit_api.element_properties`.

## `revit_api.element_properties` v1

### Inputs

| Field | Rule |
|---|---|
| `type_name` | Required. Exact full name copied from metadata. Length 1..256. |
| `properties` | Required. 1..16 distinct strings. Each length 1..128. Ordinal property names. No dots, no expressions. |

### Type rules

Resolved only in the trusted assembly:

- public;
- a class;
- not abstract;
- `Autodesk.Revit.DB.Element` or a subclass, by walking base types for the full name `Autodesk.Revit.DB.Element`.

Failure is `REVIT_API_TYPE_REJECTED` with reason `unknown`, `not_element`, or `abstract`. No collector is created.

### Property rules

Each name must resolve to one public instance property. Ambiguous match is `INVALID_READ_OPERATION_INPUT`.

The getter runs only when the property is projectable under the policy below. Any other requested property fails the request with `INVALID_READ_OPERATION_INPUT` before any getter runs and before any row is returned. The message names the property and the rejection.

Policy, in order:

1. Indexer (`not_readable` is not used for this case): `indexer`.
2. No public zero-argument instance getter: `not_readable`.
3. Static getter: `static`.
4. Declaring type missing or outside the trusted assembly: `outside_trusted_assembly`.
5. Declared return type not in the scalar list: `unsupported_return_type`.
6. Otherwise projectable.

A setter on an otherwise projectable property is ignored. It is never called.

### Collection and order

After type and property checks succeed:

1. If `Document.IsModifiable` is true, fail with `DOCUMENT_IS_MODIFIABLE`. Do not call `OfClass`. Do not call a getter.
2. Collect with `new FilteredElementCollector(document).OfClass(resolvedType)`. No `WhereElementIsNotElementType`, no category filter, no per-class fallback.
3. If `OfClass` throws `ArgumentException` because the type exists in the API but not in Revit's native object model, fail with `REVIT_API_TYPE_REJECTED`, reason `not_collectable`, and no rows. Do not consult a handwritten list and do not switch filters.
4. If the matched count is greater than 2,000, fail with `RESULT_TOO_LARGE` and no rows.
5. Order by `Element.UniqueId`, ordinal. Do not serialize `UniqueId` or any `ElementId` integer.
6. Project the current page. Invoke each selected getter as `getter.Invoke(target, null)` and no other member.
7. If `Document.IsModifiable` is true at any check after step 1, including after the collector returns and after the page's getters, fail with `DOCUMENT_BECAME_MODIFIABLE` and return no rows.

Derived classes that pass `OfClass` are included. v1 does not add the runtime type name to the row.

### Row shape

`summary.type_name` is the requested type. `summary.matched_element_count` is the whole ordered set, not the page. `returned_count` is the page length.

Each row is `{ "values": [ ... ] }` in the requested property order. Each value is a closed object:

| Field | When |
|---|---|
| `name` | Always. The property name. |
| `kind` | `string`, `boolean`, `integer`, `number`, `enum`, or `null` |
| `text` | `string`, or the enum constant name |
| `boolean` | `boolean` |
| `integer` | `integer` |
| `number` | `number` |

`kind: null` has none of the payload fields. Every other kind has exactly one payload field, the one in the table. Semantic validation rejects any other combination. There is no `element_ref`, `element_id`, or `unique_id`.

The cursor fingerprint is `type_name` plus the property names in request order.

## Scalar types

Accepted declared types, including one `Nullable<T>` wrapping of the same type:

| Declared type | `kind` | Payload |
|---|---|---|
| `string` | `string`, or `null` when the value is null | `text` |
| `bool` | `boolean`, or `null` for `bool?` | `boolean` |
| `sbyte`, `byte`, `short`, `ushort`, `int`, `uint`, `long` | `integer`, or `null` for the nullable form | `integer` |
| `float`, `double` | `number`, or `null` for the nullable form | `number` |
| non-flags `enum` | `enum` | `text`, the constant name from `Enum.GetName` |

Rejected declared types include `ulong`, `decimal`, `char`, flags enums, `ElementId`, arrays, and any other type. Rejection is `unsupported_return_type` before any row is read.

Value failures, after a getter returns, fail the whole request with `REVIT_EXECUTION_FAILED`, name the property, and return no rows:

- a non-finite `float` or `double`;
- an integral value outside the closed JSON safe range [-9007199254740991, 9007199254740991];
- an enum value with no constant name;
- a `string` longer than 4,096 characters (`RESULT_TOO_LARGE` instead, still with no rows).

### Units

`kind: number` is the raw Revit API value. Metadata does not add a unit. The operation description states that a number has no physical unit.

`Level.Elevation` in the Class A record was internal feet and matched a millimeter schedule after conversion. That observation stays in the evidence. It is not a conversion rule and it is not applied to any other `double`.

## Mutation safety

Read-only. Not a sandbox.

- The only invocation is the getter call in the collection section.
- BIM-IT opens no `Transaction`, `SubTransaction`, or `TransactionGroup`.
- `Document.IsModifiable` must be false before collection and must remain false through completion.
- `Document.IsModified` may be written to diagnostics. It is not an output field and it is not the pass/fail check.
- A getter exception, a non-finite value, a safety failure, or a budget failure returns no partial page.
- Autodesk documents that model changes require an open transaction and that modifying a non-modifiable document throws `ModificationOutsideTransactionException`. That bounds model mutation. It does not prove a getter has no other side effect.

## Identity

v1 defers row identity. Chaining to `revit_get_elements` or a write is out of scope. A later version that needs a handle uses ADR-0006 `element_ref` with the same `document_id`. It does not return a raw id and it does not define a new reference format.

## Errors

Reused with their CAP-0016 meaning:

```text
NO_ACTIVE_DOCUMENT
DOCUMENT_CONTEXT_CHANGED
UNSUPPORTED_DOCUMENT_KIND
INVALID_CURSOR
STALE_CURSOR
RESULT_TOO_LARGE
REVIT_EXECUTION_TIMEOUT
REVIT_EXECUTION_FAILED
INVALID_READ_OPERATION_INPUT
READ_OPERATION_RESULT_INVALID
```

`INVALID_READ_OPERATION_INPUT` covers schema failures, exactly-one-of `query`/`type_name`, an empty token list, a query that matches more than 200 types, an unknown or non-projectable property, and an ambiguous property. The message names the field or property and the constraint. It is the wrong code for a type the API refuses to collect, and for a document that is modifiable: those are not bad input shapes.

New codes:

```text
REVIT_API_TYPE_REJECTED
    reason unknown | not_element | abstract | not_collectable
    No payload. not_collectable is only the OfClass ArgumentException
    for a type that exists in the API but not in the native object model.

DOCUMENT_IS_MODIFIABLE
    IsModifiable was true before collection. No OfClass call and no getter.

DOCUMENT_BECAME_MODIFIABLE
    IsModifiable became true during collection or getters.
    No rows.
```

`REVIT_EXECUTION_FAILED` is the getter-threw and bad-value code. The message names the property and a stable reason (`getter_failed`, `non_finite_number`, `integer_out_of_range`, `unnamed_enum`). It includes no stack trace, path, or Revit exception text.

Messages follow the CAP-0016 secrecy rule.

## Admission

Before a new compiled factual read is added, the specification for that read says why `revit_api.element_properties` cannot supply the fact safely. A compiled operation or a dedicated tool is for BIM-semantic aggregation, joins, geometry or topology, special identity, performance, a source this reader cannot collect, or a controlled write. A scalar property on a collectable Element subclass is not a sufficient reason.

## Acceptance criteria

These apply to a future implementation. They are not executed by this proposal.

1. A normal build that does not opt into the spike registers `project.user_worksets` v1 plus, once this contract is implemented and approved, exactly the two operations above. The spike ids are absent. `tools/list` remains the CAP-0016 tools. Protocol remains 24.
2. Search tokens for `levels`, `phases`, and `design options` match `Level`, `Phase`, and `DesignOption` in a metadata-only fixture, with no alias table and no benchmark type names in the production operation sources.
3. The same projector accepts `Name` and `Elevation` on `Level`, `Name` on `Phase`, and `Name` and `IsPrimary` on `DesignOption`, and rejects `Id` as `unsupported_return_type`.
4. `OfClass` is the only collector. An `ArgumentException` for a native-model mismatch becomes `not_collectable` and does not call `RoomFilter` or any other class-specific filter.
5. `IsModifiable == true` before the read prevents `OfClass` and getters. A change to true during the read returns `DOCUMENT_BECAME_MODIFIABLE` and no rows.
6. No `Transaction`, `SubTransaction`, or `TransactionGroup`. The only `Invoke` is `getter.Invoke(target, null)`.
7. Results contain no `ElementId` integer and no `UniqueId`. Ordering is stable across pages.
8. Cursor, revision, page size, and the 120,000-character budget behave as CAP-0016.
9. A natural-agent gate, in the CAP-0016 style, uses a prompt that does not name the operation ids or the API type names, on a Revit document, through BIM-IT Assistant and BIM-IT MCP only. Acceptance of this CAP is not that gate.

## Explicitly deferred

- row `element_ref`;
- runtime type name on each row;
- property-name search (`elevation` finding `Level`);
- units;
- dedicated collectors for types `OfClass` rejects;
- method metadata and method invocation;
- family-document support beyond the existing project-document rule;
- linked documents;
- generated C#;
- changing Skills, including the Electrical Standards Skill.

## Decisions required before acceptance

Listed in [ADR-0025](../adr/ADR-0025-bounded-revit-api-property-access.md). This contract uses those proposed answers. Changing one of them requires editing this specification before implementation.

## Evidence

PR #123, [spike README](../../spikes/BimIt.Revit.ApiAccessSpike/README.md), and [VALIDATION.md](../../spikes/BimIt.Revit.ApiAccessSpike/VALIDATION.md).

One generic implementation, with no type-specific production branch, live-read `Level` (`Name`, `Elevation`), `Phase` (`Name`), and `DesignOption` (`Name`, `IsPrimary`) through BIM-IT Assistant and BIM-IT MCP only. Classification A. Limits recorded there, and kept here: one Revit version and model, no generic unit, incomplete `OfClass` coverage, and getters that are not a sandbox.

## References

- Autodesk Revit 2026 `FilteredElementCollector.OfClass`: https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/b0a5f22c-6951-c3af-cd29-1f28f574035d.htm
- Autodesk Revit 2026 `ElementClassFilter`: https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/4b7fb6d7-cb9c-d556-56fc-003a0b8a51b7.htm
- Autodesk Revit API Developer's Guide, Transactions: https://help.autodesk.com/cloudhelp/2025/ENU/Revit-API/files/Revit_API_Developers_Guide/Basic_Interaction_with_Revit_Elements/Revit_API_Revit_API_Developers_Guide_Basic_Interaction_with_Revit_Elements_Transactions_html.html
