# CAP-0016: Registry-driven Revit read operations

- Status: Proposed
- Operation class: Read
- Date: 2026-11-11
- Architecture: Accepted [ADR-0024](../adr/ADR-0024-registry-driven-compiled-revit-read-operations.md)
- Introduced bridge protocol version: 24 ([BRIDGE-0020](../bridge/BRIDGE-0020-read-operations-capability-rpc.md))
- MCP exposure: [SERVER-0015](../server/SERVER-0015-stdio-read-operations-tools.md)

## Authorization

This specification is the implementation contract for the first ADR-0024 vertical slice. It is not implemented. It does not change [CAP-0014](CAP-0014-revit-query-family-types.md) or [CAP-0015](CAP-0015-revit-query-parameter-bindings.md). It does not modify the external Electrical Standards Skill.

Where this specification and Accepted ADR-0024 differ in envelope spelling, lookup paging, or the shared validator location, this specification is the decision ADR-0024 left to the implementing specs. The one execution-order constraint discovered against the current Addin is recorded under "Execution" and does not change the architecture.

## Purpose

Two MCP tools expose a closed Addin registry of compiled C# read operations:

- `revit_lookup` discovers registered operations;
- `revit_read` executes one registered operation by id and contract version.

The first production registry contains exactly one operation: `project.user_worksets`, contract version 1. It inventories the user-created worksets of the active host project.

No model-supplied code runs. The tools do not select code by reflection, do not invoke an arbitrary Revit API member, and do not write.

## Tools

| Tool | Title | Annotations |
|---|---|---|
| `revit_lookup` | Lookup Revit read operations | `readOnlyHint: true`, `openWorldHint: false`. `destructiveHint` is absent. |
| `revit_read` | Read a registered Revit operation | `readOnlyHint: true`, `openWorldHint: false`. `destructiveHint` is absent. |

Both accept `instance_id` and an object input schema, so the ADR-0022 Assistant read facade discovers them without an Assistant change. `revit_read` takes `document_id`, which that facade injects the same way it does for current reads.

`revit_lookup` is not a Revit API lookup. It never searches or describes Revit API types, members, or assemblies.

After implementation, production `tools/list` contains exactly 22 tools: the 20 tools recorded for CAP-0015 plus `revit_lookup` and `revit_read`. CAP-0014 and CAP-0015 stay in that list with their current contracts.

## Shared JSON Schema subset

Runtime and registry validation use one deterministic subset, extracted into `BimIt.Revit.Contracts` as transport-neutral pure contract code. The Addin and the Server already reference Contracts. This slice adds no project and no third-party JSON Schema library.

The shared type is `JsonSchemaSubset`. It provides:

1. **Instance validation.** A JSON instance either matches a schema in the supported subset or it does not.
2. **Definition validation.** An operation schema document is either an accepted closed schema or it is rejected, with the failing keyword or node identified.

Supported validation keywords, and no others:

`type`, `enum`, `const`, `properties`, `required`, `additionalProperties`, `items`, `minLength`, `maxLength`, `minimum`, `maximum`, `minItems`, `maxItems`, `uniqueItems`.

`minimum` and `maximum` are inclusive. `integer` rejects a fractional JSON number. `uniqueItems` uses the same JSON equality the current Server validator uses.

Not supported, and rejected by definition validation: `oneOf`, `anyOf`, `allOf`, `$ref`, `pattern`, `format`, and every other validation or structural keyword.

Annotation keywords that do not affect validation are allow-listed: `description`, `title`, and `default`. Production MCP schemas already use all three. They stay annotations.

Instance validation ignores keywords outside the supported validation set, which is the current `ClosedSchemaArgumentValidator` behavior. Legacy Server MCP argument checks may keep that behavior. Definition validation does not. A keyword that is neither a supported validation keyword nor an allow-listed annotation fails definition validation. A supported keyword used with a malformed shape or value also fails definition validation. It must never become a constraint that instance validation silently skips.

`ClosedSchemaArgumentValidator` remains the Server entry point for MCP argument checks and becomes a thin wrapper over instance validation. Existing tool-argument tests must keep passing. Every operation schema passes strict definition validation before the registry activates, and instance validation again on every read.

### Definition validation

This is a small deterministic BIM-IT meta-schema for the supported subset. It is not full JSON Schema compliance. Definition validation checks keyword shape and values, then validates nested schemas. A failure names the keyword or node and fails registry startup.

| Keyword | Accepted shape |
|---|---|
| `type` | One of `object`, `array`, `string`, `number`, `integer`, `boolean`, `null`, or a non-empty array of those names with no duplicates and no unknown name. This includes the nullable arrays already used by MCP schemas, such as `["string", "null"]`, `["integer", "null"]`, and `["object", "null"]`. |
| `properties` | A JSON object. Every property value is a schema object and is validated recursively. |
| `required` | An array of unique strings. For an operation contract, every name is declared in `properties`. |
| `additionalProperties` | On an object-capable operation node, exactly the boolean `false`. A schema value or `true` is rejected. |
| `items` | One schema object, validated recursively. An array of schemas is rejected. |
| `minLength`, `maxLength` | Non-negative integers. When both are present, `minLength` is less than or equal to `maxLength`. |
| `minimum`, `maximum` | JSON numbers. When both are present, `minimum` is less than or equal to `maximum`. |
| `minItems`, `maxItems` | Non-negative integers. When both are present, `minItems` is less than or equal to `maxItems`. |
| `uniqueItems` | A boolean. |
| `enum` | A JSON array. |
| `const` | Any JSON value. |
| `description`, `title` | Strings when present. |
| `default` | Any JSON value. It is an annotation only. No default is injected when a property is absent. |

A validation keyword is rejected when the node's `type` does not permit the instance kind that keyword constrains:

- `properties`, `required`, and `additionalProperties` only where `type` permits `object`;
- `items`, `minItems`, `maxItems`, and `uniqueItems` only where `type` permits `array`;
- `minLength` and `maxLength` only where `type` permits `string`;
- `minimum` and `maximum` only where `type` permits `integer` or `number`.

`enum`, `const`, annotations, and constraints may refine a typed node. They do not replace `type`.

For an ADR-0024 operation contract, every schema node declares an explicit `type`. That node is the `input_schema` root, the `output_schema` root, every schema under `properties`, every `items` schema, and every schema inside a composition branch if one is later added to this subset. `type` is one supported type name, or a non-empty array of unique supported type names, under the shape rules above. A missing `type`, including `{}` and `{"enum":["a","b"]}`, fails definition validation and therefore the existing fail-closed registry behavior. `{"type":"string","enum":["a","b"]}` satisfies this rule.

This explicit-`type` requirement applies only to operation contracts. Legacy MCP tool schemas that share the instance validator are not rejected for omitting `type`.

The root `input_schema` and `output_schema` each remain an object contract: `type` is `object`, not a union.

### Recursive closure

An operation-schema node is **object-capable** when its `type` is `object`, or when its `type` is an array that contains `object`. Every object-capable node in an operation `input_schema` and `output_schema` must carry `"additionalProperties": false`. That includes nullable objects such as `"type": ["object", "null"]`, and it applies recursively to:

- the root object;
- nested properties at any depth;
- object schemas used as `items`;
- an object-capable node that declares no `properties`, including the empty object;
- object-capable nodes inside any composition keyword added to this subset later.

A node whose allowed types do not include `object` does not carry `additionalProperties`.

## Registry

The Addin composition root passes one explicit list of operation instances to an immutable registry built once at host startup. The first production list contains exactly `project.user_worksets` version 1. Adding an operation is a reviewed change to that list. Operations are not discovered by reflection, attributes, or scanning.

Startup validation of that list checks all of the following. Any failure disables the whole registry for that host:

- operation id matches `^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$` and is 1..64 characters;
- at least two dot-separated segments;
- `(id, contract_version)` is unique;
- `contract_version` is an integer greater than or equal to 1;
- `effects` is `read`;
- `context` is `document`;
- `pagination` is `none` or `cursor`;
- `input_schema` and `output_schema` are present, parse as JSON objects, pass definition validation, and each have an object root.

The 64-character cap is the existing `DeclarativeQuerySources.MaxLength`. Cursor-paginated operations use the operation id as the cursor source, so an id that cannot be a cursor source cannot be registered.

On failure:

- the read-operation service is not composed;
- Bridge protocol 24 is not advertised;
- the host continues to start;
- earlier Bridge capabilities stay usable;
- no operation from the invalid list remains callable.

Invalid operations are not dropped one by one. The same validation runs in CI over the production registration list. Fixture registries cover the negative cases. A fixture registry is not composed into the production host.

The connected host's registry is the only catalogue. Lookup does not list operations that another build or Revit version might have.

## `revit_lookup`

Lookup reads the immutable in-memory registry of the selected host. It requires a reachable selected Revit instance. It does not require an active document, does not enter EXEC-0001, and does not call the Revit API.

### Request

Server routing field, removed before the Bridge call:

- `instance_id`, optional string.

Bridge request. Every field is optional. A JSON `null` is treated exactly as absence, in both search mode and exact mode. Bounds such as `minLength`, `minimum`, and `maximum` apply only to a non-null value.

| Field | Rule |
|---|---|
| `operation` | Present selects exact mode. Absent selects search mode. |
| `contract_version` | Integer greater than or equal to 1. Legal only in exact mode. |
| `query` | String, 1..256 characters when present. Legal only in search mode. Absent lists every registered operation. |
| `offset` | Integer greater than or equal to 0. Default 0. Legal only in search mode. |
| `limit` | Integer, 1..50. Default 25. Legal only in search mode. |

Search mode is the absence of `operation`, including a null `operation`. Exact mode is a non-null `operation`. The match is ordinal and exact. The id is not trimmed.

`query` uses `StringComparison.OrdinalIgnoreCase` substring over operation id, title, and description. A match on any one of the three includes the operation. There is no fuzzy match, ranking, or tag search. A whitespace-only query is a literal substring, not a request to list everything.

Search results are ordered by operation id, `StringComparer.Ordinal`, then by `contract_version` ascending. `offset` past the end succeeds with an empty `operations` array, `returned_count` 0, and `next_offset` null. `total` stays the full match count.

`contract_version` in search mode, or `query`, `offset`, or `limit` in exact mode, is `INVALID_READ_OPERATION_LOOKUP`. The message names the illegal fields. This combination cannot be expressed with the supported schema subset, so it is not `INVALID_REQUEST`.

An unknown operation id, including a known id written with different case, is `UNKNOWN_READ_OPERATION`. That code wins when the id is unknown even if `contract_version` is also wrong. A known id whose requested version is not registered is `READ_OPERATION_VERSION_UNSUPPORTED`. The message names the registered versions of that id. An omitted version in exact mode returns every registered version of that exact id, ordered by contract version ascending, each as a full descriptor.

Search with no matches succeeds. It is not an error.

### Result

One envelope for both modes:

```json
{
  "mode": "search",
  "total": 1,
  "returned_count": 1,
  "next_offset": null,
  "operations": [
    {
      "id": "project.user_worksets",
      "contract_version": 1,
      "title": "User worksets",
      "description": "User-created worksets of the active host project. Omits view, family, standard, and other internal worksets, and omits worksets that belong to linked documents. A project that is not workshared returns an empty inventory.",
      "context": "document",
      "effects": "read",
      "pagination": "cursor"
    }
  ]
}
```

`mode` is `search` or `exact`. `total` is the number of matches before paging. `returned_count` is `operations.length`. In search mode, `next_offset` is `offset + returned_count` when that sum is less than `total`, and null otherwise. In exact mode, `next_offset` is null and `returned_count` equals `total`.

Every operation entry contains:

| Field | Value |
|---|---|
| `id` | Stable operation id |
| `contract_version` | Integer |
| `title` | Short title |
| `description` | What the operation returns |
| `context` | `document` in v1 |
| `effects` | `read` |
| `pagination` | `none` or `cursor` |

`input_schema` and `output_schema` are present only on exact-mode entries. They are the schema objects, not strings. Search entries omit those two properties. Search never returns schemas.

Exact lookup is not paged. The serialized lookup result uses `ReadResultBudget.MaxCharacters` (120,000). A search page or an exact result that exceeds it is `RESULT_TOO_LARGE` and returns no operations. There is no second catalogue and no unavailable-operation list.

## `revit_read`

### Request

Server routing field, removed before the Bridge call:

- `instance_id`, optional string.

Bridge request:

| Field | Rule |
|---|---|
| `document_id` | Required string. Active-document guard. Ordinal equality. |
| `operation` | Required string. Exact ordinal id. |
| `contract_version` | Required integer greater than or equal to 1. No implicit latest version. |
| `inputs` | Required JSON object. Validated against the operation input schema. |
| `page_size` | Optional integer, 1..200. Legal only when `pagination` is `cursor`. Default 100 for those operations. |
| `cursor` | Optional string, 1..2048 characters. Legal only when `pagination` is `cursor`. |

A JSON `null` on `instance_id`, `page_size`, or `cursor` means omitted. Required `document_id`, `operation`, `contract_version`, and `inputs` are not nullable. Bounds apply to the non-null value. Paging fields are never accepted inside `inputs`. An operation whose `pagination` is `none` rejects either paging field with `INVALID_READ_OPERATION_INPUT`, and the message names the field.

Unknown envelope fields are rejected. Unknown `inputs` properties are rejected by the closed operation schema. They are not ignored.

### Result

```json
{
  "context": {
    "instance_id": "selected host",
    "document_id": "active document that matched the request"
  },
  "operation": "project.user_worksets",
  "contract_version": 1,
  "result": {},
  "complete": true,
  "next_cursor": null
}
```

`context.instance_id` is the selected host id. `context.document_id` is the active document id after the guard succeeds. `contract_version` is the version requested and executed. `result` is the operation payload and matches that version's output schema. The generic envelope has no `returned_count`. Collection counts belong to `result`.

For `pagination = none`, `complete` is true and `next_cursor` is null.

For `pagination = cursor`, `complete` and `next_cursor` follow CAP-0014: only `complete: true` ends a traversal; the final page has `next_cursor: null`; a short page is not evidence that the traversal ended.

### Execution order

Revit-free checks run before any EXEC-0001 enqueue and never touch Revit:

1. Resolve `(operation, contract_version)` on this host.
2. Reject paging fields that the descriptor does not accept.
3. Validate `inputs` against `input_schema`.
4. Bind `inputs` to the operation's typed request.
5. Run semantic input validation on that request.
6. When a cursor is present, reject a token that this process did not issue for this operation id, instance, document id, contract version, and normalized inputs.

A failure in steps 1 through 6 does not enqueue work and does not call the Revit API. Steps 3 through 5, and a paging-field failure at step 2, return `INVALID_READ_OPERATION_INPUT`. Step 1 returns `UNKNOWN_READ_OPERATION` or `READ_OPERATION_VERSION_UNSUPPORTED`. Step 6 returns `INVALID_CURSOR`.

The active document and the document query revision are readable on the Revit thread. Those checks are the start of the one EXEC-0001 work item, before the operation reads the model:

7. Require an active document (`NO_ACTIVE_DOCUMENT`).
8. Require a project document (`UNSUPPORTED_DOCUMENT_KIND` when `Document.IsFamilyDocument` is true).
9. Compare `document_id` to the active document with ordinal equality (`DOCUMENT_CONTEXT_CHANGED`).
10. For a cursor operation, compare the cursor revision with the current document query revision (`STALE_CURSOR`). A cursor whose document does not match the active document is `INVALID_CURSOR`.
11. Execute the operation. A cursor operation fits its own page before it returns. The generic dispatcher does not truncate or inspect the opaque `result` after `Execute`.
12. Validate the typed result.
13. Serialize it on the shared UTF-8 contract path.
14. Validate that JSON against `output_schema`.
15. Measure the final complete envelope against `ReadResultBudget.MaxCharacters`. If it still exceeds the hard limit, return `RESULT_TOO_LARGE` and no data.
16. Return.

Steps 12 and 14 return `READ_OPERATION_RESULT_INVALID` and no `result`. There is one work item per read. Lookup uses none.

This split is required because ADR-0024 places the document guard and the revision check before execution, while the current Addin can read the active document and sample the revision only inside EXEC-0001. Sampling the revision in the same work item as the read is the CAP-0014 invariant, which ADR-0024 keeps.

`REVIT_EXECUTION_TIMEOUT` uses the existing capability timeout (`ServerTimeouts.CapabilityTimeout`, 15 seconds). No new timeout is introduced. A timeout returns no partial result. An unexpected failure inside the work item is `REVIT_EXECUTION_FAILED` with a compact message and no Revit exception text.

### Cursor

Reuse `DeclarativeQueryCursorCodec` and `DeclarativeQueryFingerprintBuilder`. Do not add a cursor format and do not add a central source enum that each new operation must edit. `DeclarativeQuerySources.FamilyTypes` and `ParameterBindings` stay as they are. Cursor body version stays 2.

For a registered operation:

- cursor source is the operation id;
- the fingerprint starts with that source, then includes the decimal contract version, the document id, and the normalized inputs;
- `page_size` is not part of the fingerprint;
- the cursor payload still binds instance, document, revision, and offset;
- a cursor for one operation, version, or input object never validates for another.

Normalized inputs for v1 are the operation's own matched-set fields. For `project.user_worksets` the only field is `name_contains`, and an omitted value is distinct from every string. The value is not trimmed.

A family-type cursor, a parameter-binding cursor, a tampered token, a token longer than 2048 characters, and a cursor minted for another operation id, version, document, instance, or `name_contains` are `INVALID_CURSOR`. A cursor minted at revision N is `STALE_CURSOR` after that document's revision advances. The client restarts without a cursor. The cursor is not re-based.

`page_size` may change between pages. The document query revision is the existing per-document revision used by CAP-0014 and CAP-0015. A change that advances it invalidates a workset cursor. Advancing more often than necessary is allowed. The operation does not keep an inventory snapshot.

### Result budget

`page_size` is a maximum. The serialized structured read result, as the MCP client receives it, stays within 120,000 characters (`ReadResultBudget.MaxCharacters`). The Addin and Contracts do not reference the Assistant assembly. CAP-0014 and CAP-0015 keep their own paging and cursor behavior.

The generic dispatcher does not truncate an opaque operation `result` and does not walk its rows after `Execute`.

For `pagination = cursor`, the operation's typed paging logic chooses the longest ordered row prefix that fits. Later registered inventories share one page-fitting helper for that measurement. The helper's class name is an implementation detail. The measurement includes the complete generic `ReadOperationResult` envelope, not only the operation payload. The operation may therefore return fewer rows than `page_size`. It mints `next_cursor` after the last returned row. No row is skipped or repeated. `complete` is false on a shortened page. The operation summary stays the whole matched set, and the operation `returned_count` is the number of rows in that page.

A page has zero rows only when the matched set is empty. If one row plus the required envelope and summary cannot fit, the operation does not invent a smaller row. The dispatcher then returns `RESULT_TOO_LARGE`.

After the operation returns its fitted typed result, the dispatcher still:

1. validates the typed result;
2. serializes it;
3. validates that JSON against `output_schema`;
4. measures the final complete envelope against `ReadResultBudget.MaxCharacters`;
5. returns `RESULT_TOO_LARGE` and no data when that measurement still exceeds the hard limit.

For `pagination = none` there is no fitting and no truncation. An envelope over the hard limit is `RESULT_TOO_LARGE`.

## `project.user_worksets`, contract version 1

| Descriptor field | Value |
|---|---|
| `id` | `project.user_worksets` |
| `contract_version` | 1 |
| `title` | `User worksets` |
| `description` | User-created worksets of the active host project. Omits view, family, standard, and other internal worksets, and omits worksets that belong to linked documents. A project that is not workshared returns an empty inventory. |
| `context` | `document` |
| `effects` | `read` |
| `pagination` | `cursor` |

The source is `FilteredWorksetCollector` on the active host project, restricted with `OfKind(WorksetKind.UserWorkset)`, and only when `Document.IsWorkshared` is true.

Excluded, even if the Revit API would otherwise surface them:

- `WorksetKind.ViewWorkset`
- `WorksetKind.FamilyWorkset`
- `WorksetKind.StandardWorkset`
- every other non-user kind
- worksets of a linked document
- the single internal workset of a project that is not workshared

The Revit API still has a workset table when worksharing is off. This operation does not enumerate it. When `IsWorkshared` is false, the operation returns the empty success below and does not construct a `FilteredWorksetCollector`.

The operation does not open a link, does not call `WorksetTable.SetActiveWorksetId`, and does not rename a workset.

### Input schema

```json
{
  "type": "object",
  "additionalProperties": false,
  "properties": {
    "name_contains": {
      "type": "string",
      "minLength": 1,
      "maxLength": 256,
      "description": "OrdinalIgnoreCase substring of the workset name. Omitted matches every user workset."
    }
  }
}
```

When `name_contains` is present it is not trimmed. A value made only of whitespace fails semantic validation with `INVALID_READ_OPERATION_INPUT`. The match is `StringComparison.OrdinalIgnoreCase` substring. There is no wildcard and no regular expression. Omitted matches every user workset.

### Output schema

```json
{
  "type": "object",
  "additionalProperties": false,
  "required": ["summary", "returned_count", "worksets"],
  "properties": {
    "summary": {
      "type": "object",
      "additionalProperties": false,
      "required": ["worksharing_enabled", "matched_workset_count"],
      "properties": {
        "worksharing_enabled": { "type": "boolean" },
        "matched_workset_count": { "type": "integer", "minimum": 0 }
      }
    },
    "returned_count": { "type": "integer", "minimum": 0 },
    "worksets": {
      "type": "array",
      "items": {
        "type": "object",
        "additionalProperties": false,
        "required": ["name", "is_default", "is_visible_by_default"],
        "properties": {
          "name": { "type": "string" },
          "is_default": { "type": "boolean" },
          "is_visible_by_default": { "type": "boolean" }
        }
      }
    }
  }
}
```

Row mapping, and nothing else:

| Field | Revit API |
|---|---|
| `name` | `Workset.Name`, unmodified |
| `is_default` | `Workset.IsDefaultWorkset` |
| `is_visible_by_default` | `Workset.IsVisibleByDefault` |

Not exposed: `Workset.Id`, `WorksetId`, `UniqueId`, `Owner`, `IsEditable`, `IsOpen`, element counts, and any other Revit integer identity.

`summary` is exact over the whole matched set and is repeated unchanged on every page. `matched_workset_count` is that set's length, not the page length. `returned_count` is `worksets.length` for this page. `worksharing_enabled` is `Document.IsWorkshared` and does not change because a name filter matched a subset.

Order of the matched set:

1. `name`, `StringComparer.OrdinalIgnoreCase`
2. `name`, `StringComparer.Ordinal`

The collector's iteration order is not the result order. The same inputs against an unchanged document return the same sequence. If two rows share a name, both are returned and the second key orders them. The implementation does not drop a row to force uniqueness.

### Non-workshared project

Success, not an error:

```json
{
  "summary": {
    "worksharing_enabled": false,
    "matched_workset_count": 0
  },
  "returned_count": 0,
  "worksets": []
}
```

The generic envelope has `complete: true` and `next_cursor: null`. A `name_contains` value does not change this result. No cursor is minted.

### Workshared project with no name match

`worksharing_enabled` is true, `matched_workset_count` is 0, `returned_count` is 0, `worksets` is empty, `complete` is true, and `next_cursor` is null.

### Family document

`UNSUPPORTED_DOCUMENT_KIND`. The message keeps the current meaning: the active document is not a project document. The operation does not read worksets.

## Errors

Messages are compact and stable enough for a test to assert the code and the named field or version. A message never contains a stack trace, a filesystem path, a raw Revit exception, a user name, a machine name, a process id, or a pipe name.

Server routing, unchanged:

```text
NO_REVIT_INSTANCE
INSTANCE_REQUIRED
INSTANCE_NOT_FOUND
INSTANCE_UNAVAILABLE
```

Reused capability codes, with their current meaning:

```text
NO_ACTIVE_DOCUMENT
DOCUMENT_CONTEXT_CHANGED
UNSUPPORTED_DOCUMENT_KIND
INVALID_CURSOR
STALE_CURSOR
RESULT_TOO_LARGE
REVIT_EXECUTION_TIMEOUT
REVIT_EXECUTION_FAILED
```

Bridge compatibility, existing:

```text
BRIDGE_PROTOCOL_INCOMPATIBLE
```

A host that did not negotiate protocol 24, including a host whose registry failed startup validation, returns `BRIDGE_PROTOCOL_INCOMPATIBLE` for both RPCs. No fallback calls another method.

New codes:

```text
UNKNOWN_READ_OPERATION
    No operation with this id is registered on the connected host.

READ_OPERATION_VERSION_UNSUPPORTED
    The id is registered, and the requested contract version is not.
    The message names the registered versions.

INVALID_READ_OPERATION_LOOKUP
    Search mode included contract_version, or exact mode included
    query, offset, or limit. The message names those fields.

INVALID_READ_OPERATION_INPUT
    inputs or a paging field failed schema or semantic validation.
    The message names the accepted input names and the failing constraint.

READ_OPERATION_RESULT_INVALID
    Typed or output-schema validation failed. No result is returned.
```

`READ_OPERATION_UNAVAILABLE` is not a v1 code.

Malformed MCP envelope JSON, unknown envelope properties, and envelope values outside the generic schema map to `INVALID_REQUEST`. They are not rewritten as `INVALID_READ_OPERATION_INPUT` or `INVALID_READ_OPERATION_LOOKUP`. An `inputs` object that fails the operation schema maps to `INVALID_READ_OPERATION_INPUT`, because the MCP envelope leaves `inputs` open on purpose.

## Safety

- no `Transaction`, `SubTransaction`, or `TransactionGroup`;
- no Revit API call that modifies the document;
- no model-supplied code, type name, or member name;
- no generic write and no write operation in the registry;
- operation code runs only inside the dispatcher's work item and receives the active `Document` for reading, not `UIApplication`;
- Skills cannot register an operation or change `effects`.

## Acceptance criteria

Automated tests, without a live Revit session unless a criterion says otherwise:

1. **Startup validation.** The production list registers only `project.user_worksets` version 1 and passes. Fixture failures each disable the whole fixture registry: open object, missing `additionalProperties` on an empty object, on a nested object, on an array item, or on `"type": ["object", "null"]`, a nested property schema `{}`, a nested property schema `{"enum":["a","b"]}` with no `type`, a malformed supported keyword (`type` with an unknown name, `required` naming an undeclared property, `minLength` greater than `maxLength`, `items` as an array, string-length keywords on a number), `pattern` or `oneOf`, duplicate `(id, version)`, one-segment id, id longer than 64 characters, `effects` other than `read`, `pagination` other than `none` or `cursor`, contract version less than 1. `{"type":"string","enum":["a","b"]}` passes definition validation. A non-object node is not required to carry `additionalProperties`. An existing production MCP tool schema that omits `type` still passes instance validation.
2. **Fail closed at the host.** A startup failure leaves protocol 24 unadvertised, leaves protocol 23 usable, and does not fail Addin startup.
3. **Lookup search.** Absent `query` lists the operation without schemas. A case-insensitive substring matches id, title, or description. Order is id ordinal, then version. Defaults are offset 0 and limit 25. Limit 51, limit 0, a negative offset, and a 257-character query are `INVALID_REQUEST`. `contract_version` in search mode is `INVALID_READ_OPERATION_LOOKUP`. No match returns an empty success. `offset` past `total` returns an empty page and a correct `total`.
4. **Lookup exact.** The id returns the full descriptor, including both schemas. An omitted version returns every fixture version in ascending order. Unknown id is `UNKNOWN_READ_OPERATION`. A known id with a missing version is `READ_OPERATION_VERSION_UNSUPPORTED` and the message lists the registered versions. `query`, `offset`, or `limit` in exact mode is `INVALID_READ_OPERATION_LOOKUP`.
5. **Lookup without a document.** Lookup succeeds when no document is active and does not enter EXEC-0001.
6. **Read version.** A read without `contract_version` is `INVALID_REQUEST`. A read never selects the highest registered version by omission.
7. **Isolation.** A cursor or call for one operation id, contract version, or `name_contains` is `INVALID_CURSOR` for any other. A CAP-0014 or CAP-0015 cursor is `INVALID_CURSOR` here, and a workset cursor is `INVALID_CURSOR` for those tools.
8. **Worksets.** Only `WorksetKind.UserWorkset` rows are returned, in the specified order. View, family, standard, and other kinds are absent. Linked documents are not opened. The forbidden identity and ownership fields are absent, and an output that contains one fails `READ_OPERATION_RESULT_INVALID`.
9. **Non-workshared.** `IsWorkshared == false` returns the empty success, does not construct a collector, and is not an error.
10. **Family document.** `UNSUPPORTED_DOCUMENT_KIND`, with no workset read.
11. **Cursor.** Tampering, a wrong source, a wrong version inside the fingerprint, a wrong document, a wrong instance, and a wrong `name_contains` are `INVALID_CURSOR`. A changed revision is `STALE_CURSOR`. Changing `page_size` between pages is allowed. `page_size` is not part of the fingerprint.
12. **Summary.** On every page, `matched_workset_count` equals the full filtered set and `worksharing_enabled` is unchanged. `returned_count` equals the page length. Concatenated pages equal the ordered set once. `page_size` values 1, 100, and 200 agree.
13. **Schema enforcement.** An unknown input property, a whitespace-only `name_contains`, and a string outside 1..256 characters are `INVALID_READ_OPERATION_INPUT` and do not enqueue. A typed result that drifts from the output schema is `READ_OPERATION_RESULT_INVALID` and returns no data.
14. **Budget.** The cursor operation shortens a page that would exceed 120,000 characters, including the generic envelope, without skipping or repeating a row. The dispatcher does not shorten the returned `result`. One row that cannot fit, a fitted result that still exceeds the hard limit, and any non-paged oversize result are `RESULT_TOO_LARGE`.
15. **Protocol.** Support for the two RPCs is `{24}` only. A negotiated protocol 23 returns `BRIDGE_PROTOCOL_INCOMPATIBLE` and does not invoke the registry. Protocol 23 still serves `revit.query_parameter_bindings` and `revit.query_family_types`.
16. **Tool surface.** Production `tools/list` has exactly the 22 names in SERVER-0015. No CAP-0014 or CAP-0015 request or result field changes.
17. **No write.** The operation path contains no transaction API and no mutating workset API.

## Natural-agent gate

Required after implementation, and not part of this specification's authoring. An isolated official MCP client calls the stdio Server, which calls Bridge protocol 24, which calls Revit. The agent is not coached with the operation schema.

The agent must:

1. discover `project.user_worksets` and contract version 1 through `revit_lookup`;
2. read it through `revit_read`;
3. follow `next_cursor` until `complete` is true;
4. match an independent baseline taken from the same document state, including names, `is_default`, `is_visible_by_default`, order, and the whole-set summary.

The workshared document used for that gate must contain enough user worksets to cross at least one page at the requested `page_size`. Non-workshared success and family-document failure are covered by the automated criteria and may be repeated live. The Skill's `missing_fact` for worksets is not changed by this specification or by that gate.

## Deferred

- a second registered operation;
- migration of CAP-0014, CAP-0015, or any other dedicated tool into the registry;
- promotion of `project.user_worksets` to its own MCP tool;
- application-level or linked-document operation context;
- any write, preview, or approval operation;
- `oneOf`, `anyOf`, `allOf`, `$ref`, `pattern`, and `format`;
- a rename of the internal ADR-0023 cursor types. A later rename is allowed only if cursor bytes for `family_types` and `parameter_bindings` stay valid.
