# CAP-0007: `revit_preview_parameter_updates`

- Status: Accepted
- Operation class: Preview
- Date: 2026-09-25

## Purpose

`revit_preview_parameter_updates` validates and previews one bounded batch of proposed instance-parameter updates and, only when every item is eligible, creates one immutable ephemeral `intent_ref`.

It makes zero Revit model modifications. It does not start a transaction, save, synchronize, check out workshared elements, clear a parameter, or apply the proposed values.

It is the preview half of the ADR-0008 write workflow:

```text
revit_query_elements
→ element_refs

revit_describe_parameters
→ parameter_refs + data_type semantics

revit_get_parameter_values
→ current typed values

revit_preview_parameter_updates
→ authoritative preview + immutable intent_ref
```

Apply is a later capability. This specification does not define CAP-0008.

The preview is not human approval. ADR-0008 requires a separate trusted approval of the RevitMCP-generated preview for that exact intent. `intent_ref` and `intent_fingerprint` are not approval, and they are not authorization.

## MCP tool

### Name

`revit_preview_parameter_updates`

### Title

`Preview Revit Parameter Updates`

### Description

Validate and preview one bounded batch of proposed instance-parameter updates in the active project document, and create an immutable ephemeral intent when every update is eligible. Does not modify the Revit model.

### Annotations

- `readOnlyHint: false`
- `destructiveHint: false`
- `idempotentHint: false`
- `openWorldHint: false`

`readOnlyHint` is false because a successful preview creates ephemeral RevitMCP intent state. The hint does not mean the tool writes the Revit model. This tool performs no Revit model write.

`destructiveHint` is false because the tool does not delete, clear, or irreversibly change Revit elements.

`idempotentHint` is false because repeating a preview may create a distinct `intent_ref`. The same ordered semantic contents, including the approval-preview payload below, still produce the same fingerprint. A different order produces a different fingerprint.

`openWorldHint` is false because the tool does not reach outside the addressed Revit document and the RevitMCP intent store.

Annotations are descriptive hints. ADR-0008 forbids treating them as approval.

## MCP input

Conceptually:

```json
{
  "instance_id": "optional opaque instance",
  "document_id": "required opaque document",
  "updates": [
    {
      "element_ref": "opaque-element-ref",
      "parameter_ref": "opaque-parameter-ref",
      "value": {
        "kind": "string",
        "value": "proposed text"
      }
    }
  ]
}
```

### `instance_id`

Optional Server routing value.

Same semantics as CAP-0001 through CAP-0006.

It must not enter the transport-neutral capability request. The intent still binds the Revit instance that actually executed the preview. That binding comes from the routed execution context, not from a client-parsed `instance_id` inside the capability request.

### `document_id`

Required opaque string defined by ADR-0006.

Same active-document guard used by CAP-0003, CAP-0004, and CAP-0005, plus the v1 document-kind and read-only gates below.

Required behavior:

- absent or null where the field is required -> `INVALID_PARAMETER_UPDATE_PREVIEW`;
- no active document -> `NO_ACTIVE_DOCUMENT`;
- a present string is opaque and is never trimmed, normalized, parsed, or treated as omitted;
- exact match against the active open-document lifetime -> continue;
- exact mismatch, including empty or whitespace strings, -> `DOCUMENT_CONTEXT_CHANGED`;
- never fall back to another open document.

### `updates`

Required array of **1..20** explicit updates.

Each update:

```text
element_ref: required opaque string
parameter_ref: required opaque string
value: required closed union
```

Each update must contain non-null string fields for both refs. String content remains opaque and is not trimmed, normalized, or parsed.

Pairs must be unique using ordinal string equality on the raw `(element_ref, parameter_ref)` strings. Uniqueness is on the pair, not on each ref alone.

The same `element_ref` may appear with several parameter refs.

The same `parameter_ref` may appear with several element refs.

Do not use a Cartesian `element_refs[] × parameter_refs[]` request model.

Clients must not construct, parse, or manufacture either ref.

The expected source of `element_ref` is `revit_query_elements`.

The expected source of `parameter_ref` is `revit_describe_parameters`.

An arbitrary, empty, or whitespace `element_ref` that cannot resolve becomes item-level `element_not_found`.

An arbitrary, empty, or whitespace `parameter_ref` not known to the document-lifetime reverse map becomes item-level `parameter_ref_not_found`.

There is no clear, unset, or null value variant. Omitting `value`, sending JSON `null`, or sending a kind outside the closed union is `INVALID_PARAMETER_UPDATE_PREVIEW`.

## Proposed value

Closed discriminated union. The discriminator is `kind`. No additional properties.

### `string`

```text
kind = string
value: string
```

`value` length is **0..512** characters. A longer string is `INVALID_PARAMETER_UPDATE_PREVIEW`.

The empty string is a proposed value. It is not a clear or unset.

### `integer`

```text
kind = integer
value: JSON integer in the Int32 range
```

The JSON value must be an integer token, not a fractional number. Values outside Int32 are `INVALID_PARAMETER_UPDATE_PREVIEW`.

This kind is eligible only when the resolved occurrence has `StorageType.Integer` and `Definition.GetDataType()` is exactly `SpecTypeId.Int.Integer`, compared as a Revit `ForgeTypeId`, not by parsing a TypeId string.

Yes/No and every other integer-storage spec are not supported in v1. Those occurrences are item status `value_type_mismatch` when the proposed kind is `integer`.

### `quantity`

```text
kind = quantity
value: finite JSON number
unit_type_id: non-empty string
```

`value` must be a finite JSON number. `unit_type_id` must be a non-empty string. An empty `unit_type_id`, a non-number, or a non-finite number is `INVALID_PARAMETER_UPDATE_PREVIEW`.

Eligibility:

- target `StorageType` is `Double`;
- `Definition.GetDataType()` is a measurable spec (`UnitUtils.IsMeasurableSpec`);
- the supplied `unit_type_id` satisfies `UnitUtils.IsUnit` and `UnitUtils.IsValidUnit` for that spec.

Conversion uses the typed Revit unit API (`UnitUtils.ConvertFromInternalUnits` / `UnitUtils.ConvertToInternalUnits` or the supported-matrix equivalent). Never use localized `SetValueString`, `AsValueString`, or display-string parsing to interpret or preview the quantity.

The caller-selected unit exists so the human-facing before and proposed numbers share one unit. It does not change CAP-0005, which still does not accept a caller-selected output unit.

## Document gates

v1 previews **project documents only**.

After the active-document and exact `document_id` checks:

- `Document.IsFamilyDocument == true`, or any active document that is not a project document, -> `UNSUPPORTED_DOCUMENT_KIND`;
- `Document.IsReadOnly == true` -> `DOCUMENT_NOT_WRITABLE`.

Both are capability-level errors. Create and store no intent. Do not return a success-shaped preview.

`Document.IsReadOnly` is evaluated at preview execution time. It is not cached as a standing permission.

Do not use `Document.IsModifiable` as a permission signal. Autodesk documents that a document is modifiable only inside an open transaction, and that the flag can still be false during regeneration or failure processing. A preview runs with no transaction, so `IsModifiable == false` is the expected idle state of a writable project. It must not become `DOCUMENT_NOT_WRITABLE`.

## Parameter resolution

Resolve `parameter_ref` through the Addin-owned parameter identity service established for CAP-0004 and used by CAP-0005 (`OpenDocumentParameterIdentityService` / `ParameterIdentityMap`).

A `parameter_ref` remains:

- opaque;
- document-scoped;
- source-bound (`instance` or `type`);
- meaningful only during the matching open-document lifetime;
- owned and resolved by the Revit Addin;
- never parsed or manufactured by clients;
- not an authorization token.

CAP-0007 re-resolves the exact parameter occurrence on the requested element. It does not trust aggregated CAP-0004 discovery as writeability evidence.

`read_only_on_count` is informational aggregation over the elements CAP-0004 happened to inspect. `read_only_on_count == 0` is never sufficient authorization or writeability evidence. CAP-0004 says this explicitly. CAP-0007 checks the actual occurrence's `Parameter.IsReadOnly` and `Parameter.UserModifiable`.

Resolution re-finds the current visible parameter on the instance surface using the accepted CAP-0004 identity semantics:

```text
built-in API identity
or shared GUID
or local document-scoped identity
+
source = instance
```

Do not infer identity by parsing ForgeTypeId namespace or version strings.

No `LookupParameter(name)` identity fallback.

No display-name equality.

No accepting built-in ForgeTypeId or shared GUID as the operational address in v1.

Only a binding whose source is `instance` can become `ok`. A resolved binding whose source is `type` is item status `unsupported_parameter_source`. v1 does not preview type-parameter updates.

## Write eligibility

An item can be `ok` only when, at preview execution time, all of the following are true:

- the element resolves under the existing opaque `element_ref` rules, and the resolved object is not an `ElementType` input;
- the `parameter_ref` resolves in this open-document lifetime;
- the binding source is `instance`;
- the actual parameter occurrence exists on that instance's visible instance parameters (`Element.GetOrderedParameters()`);
- `Parameter.IsReadOnly == false`;
- `Parameter.UserModifiable == true`;
- the proposed kind matches the supported source, storage, spec, and value rules in this specification;
- the exact before-state can be represented without truncation;
- the proposed value is not equal to that before-state under the comparison below.

`Parameter.UserModifiable` means the interactive user can modify the value. Autodesk's related `ExternalDefinition.UserModifiable` remarks say an API application may still be able to modify some parameters the UI grays out. CAP-0007 v1 does not use that API loophole. `UserModifiable == false` is `parameter_not_writable`.

Eligibility is not approval and not a promise that a later apply will commit. ADR-0008 requires apply to revalidate immediately before mutation.

## Item evaluation order

Evaluate every update. Do not stop the batch at the first bad item. Do not drop items.

For each update, after the document gates, apply the first matching status:

```text
reverse-lookup parameter_ref in this document lifetime
    unknown → parameter_ref_not_found

resolve element_ref without parsing UniqueId
    missing / ElementType input → element_not_found

binding source is type
    → unsupported_parameter_source

re-find the visible instance parameter by CAP-0004 identity
    not present → parameter_not_present

IsReadOnly == true or UserModifiable == false
    → parameter_not_writable

proposed kind does not match the supported storage/spec rules
    → value_type_mismatch

quantity unit fails IsUnit or IsValidUnit for the parameter spec
    → invalid_unit

before-state cannot be represented exactly under this contract
    → unsupported_value

before-state equals the proposed value
    → no_change

otherwise
    → ok
```

`StorageType.ElementId` and any reference-valued parameter are not writable in v1. A proposed string, integer, or quantity for that storage is `value_type_mismatch`. There is no `element_reference` proposed kind.

## No-change

`no_change` means the proposed value is already the current value. It is not `ok`.

Comparison:

- `string`: ordinal equality of the exact untrimmed `Parameter.AsString()` and the proposed string;
- `integer`: `Parameter.AsInteger()` equals the proposed Int32;
- `quantity`: `UnitUtils.ConvertFromInternalUnits(Parameter.AsDouble(), supplied unit)` is numerically equal to the proposed finite number. v1 applies no tolerance.

`HasValue == false` is not `no_change`. A proposed value against a parameter with no current value is a change when the other eligibility rules pass. `before.has_value` is false and `before.value` is omitted.

Any `no_change` item prevents intent creation. The caller removes that update and previews again. The capability must not silently omit it and must not create an intent for the remaining rows.

## Before-state

### Strings

The authoritative human preview of a string is exact.

If `HasValue == true` and `AsString()` is longer than 512 characters, the item status is `unsupported_value`. Do not truncate the before-state. Do not create an intent from a truncated before-state.

This is intentionally stricter than CAP-0005, which may return a 512-character prefix with `truncated = true` for a read. A read truncation must not become the bound before-state of an intent.

A null `AsString()` result for a valued string parameter is `unsupported_value`, matching CAP-0005.

### Quantities

When the item is `ok` or `no_change`, `before.value` is the current internal value converted into the **same supplied `unit_type_id`** as the proposed quantity. The human-facing diff is the two numbers in that unit.

Do not return the raw internal-unit double.

If the current value cannot be converted into that unit as a finite number, the item is `unsupported_value`.

### Integers

`before.value` is `Parameter.AsInteger()` exactly, with no Boolean or enum reinterpretation.

## Result

Return exactly one item per requested update, in request order.

Top-level result:

```text
context
  instance_id
  document_id
ready: boolean
items
intent_ref?              // present only when ready = true
intent_fingerprint?      // present only when ready = true
expires_at?              // present only when ready = true
```

`ready = true` only when every item status is `ok` and one immutable intent was stored.

Otherwise `ready = false`. Omit `intent_ref`, `intent_fingerprint`, and `expires_at`. Create and store no intent.

Do not silently drop bad items to make the batch ready.

### Statuses

```text
ok
element_not_found
parameter_ref_not_found
parameter_not_present
unsupported_parameter_source
parameter_not_writable
value_type_mismatch
invalid_unit
unsupported_value
no_change
```

Meaning:

- `ok`: the update is eligible and is included in the created intent;
- `element_not_found`: the element cannot be resolved, or the resolved object is an `ElementType` input, matching CAP-0003/CAP-0004/CAP-0005;
- `parameter_ref_not_found`: the opaque parameter ref is not known for this active document lifetime;
- `parameter_not_present`: the ref is valid and instance-scoped, but that definition is not present on the element's visible instance parameters;
- `unsupported_parameter_source`: the ref resolved with source `type`;
- `parameter_not_writable`: the occurrence exists, and `IsReadOnly` is true or `UserModifiable` is false;
- `value_type_mismatch`: the occurrence exists, but the proposed kind is not supported for that storage and spec, including Yes/No and other non-`SpecTypeId.Int.Integer` integer specs, non-Double or non-measurable quantity targets, and ElementId/reference storage;
- `invalid_unit`: the proposed quantity's `unit_type_id` is non-empty but fails `UnitUtils.IsUnit` or is not valid for the parameter spec;
- `unsupported_value`: the occurrence and proposed kind match, but the exact before-state or converted value cannot be represented under this contract;
- `no_change`: the proposed value equals the current value. Not eligible for an intent.

### `ok` and `no_change` items

```text
element_ref
parameter_ref
status
element_name
element_name_truncated
category_name
category_name_truncated
parameter_name
parameter_name_truncated
data_type
before
  has_value
  value?          // present only when has_value = true
proposed          // the proposed closed union
```

`element_name` comes from the resolved element's display name. `category_name` comes from the element's category display name, or `""` when Revit has no category name. `parameter_name` comes from the occurrence's visible definition name. These names are human context. They are not identity.

Each name is at most **512** characters. If Revit supplies a longer name, return a 512-character prefix and set the matching `*_truncated` flag true. Otherwise the flag is false. Name truncation does not change string parameter values. A string parameter value is never previewed as a truncated prefix.

`data_type` is the complete CAP-0004 object. Do not invent a second model, and do not add a sibling `forge_type_id` field.

```text
data_type
  kind              // measurable_spec | spec | category | unknown
  forge_type_id?    // present only when Revit supplied a non-empty data-type identifier
```

`forge_type_id` is omitted when Revit reports an empty data type.

`before.value`, when present, uses the same closed value shapes as the proposed union, in the proposed kind:

- string: exact `AsString()`;
- integer: exact `AsInteger()`;
- quantity: finite number plus the proposed `unit_type_id`.

`proposed` is the caller's proposed union, not a rewritten or trimmed copy.

`no_change` items include this context so the caller can see the equal values. They still do not enter an intent.

### Other items

```text
element_ref
parameter_ref
status
```

No names, `data_type`, `before`, or `proposed`.

Top-level and nested objects are closed.

## Intent

Intent state is owned by the Revit-local capability/Addin boundary, as ADR-0008 requires.

Create one intent only when every item is `ok`. Creation is all-or-nothing.

The stored intent contains only:

- the executing Revit instance identity;
- the active open-document identity;
- the exact target identities and instance parameter identities;
- the ordered approval-preview payload defined below;
- the exact typed before snapshots and proposed typed state inside that payload;
- fingerprint schema/version metadata.

Request order is semantic. The stored intent preserves the exact order of the requested updates. A later apply, which this specification does not define, must consume that ordered batch. Applying A then B is not treated as equivalent to B then A. A future accepted specification may prove order-independence; until then, order stays part of the intent.

It does not retain live Revit API wrapper objects (`Element`, `Parameter`, `Connector`, or similar). The approval-preview payload is an immutable RevitMCP-owned snapshot of strings and typed values. Preview may use live Revit objects only while executing inside a valid EXEC-0001 context. A later apply must re-resolve current Revit objects in a fresh EXEC-0001 context and compare the re-resolved human-facing fields with the stored approval-preview payload. A mismatch, including a renamed element, category, or parameter, requires a new preview and a new approval. Apply must not silently use an element whose human-facing identity no longer matches the approved preview.

`intent_ref` is opaque and high-entropy. Clients cannot enumerate intents.

The intent is:

- ephemeral;
- immutable after creation;
- scoped to the Revit process and the active open-document lifetime;
- invalid after that document closes or the process exits;
- not persisted into the Revit model;
- not parseable by clients.

### Lifetime

v1 uses a fixed lifetime of **10 minutes** from creation.

The caller cannot choose or extend the TTL.

`expires_at` is the UTC instant when the intent expires, serialized as an ISO-8601 date-time with a `Z` offset. Clients may display it. They cannot extend it. Expiry is enforced by RevitMCP.

### Capacity

The intent store must be bounded. It must fail closed rather than silently evict a still-valid intent.

If every item would be `ok` but storing a new intent would require evicting an unexpired intent, return `INTENT_CAPACITY_REACHED`, store nothing, and do not return a success-shaped preview.

The exact capacity number is deferred to the implementation specification.

### Approval-preview payload

When `ready = true`, the intent stores one approval-preview item per `ok` result item, in request order. That ordered payload is the authoritative human preview ADR-0008 binds to approval.

The public result has no `order` field. The position of each item in the response `items` array is the authoritative human-visible order. `no_change` items use the same public shape and the same array-position rule, and they still do not enter an intent.

Each stored approval-preview item is the exact returned `ok` object plus internal request-position metadata:

```text
element_ref
parameter_ref
status
element_name
element_name_truncated
category_name
category_name_truncated
parameter_name
parameter_name_truncated
data_type
  kind
  forge_type_id?
before
  has_value
  value?                      // present only when has_value = true
proposed
request_position              // internal only; 1-based position in the request
```

`request_position` is not an MCP result field and not a client input. It records the request position so canonical fingerprinting and a later ordered apply do not depend on an unordered set. Reordering the input still produces a different fingerprint.

The same ordered payload, with the same instance and document binding and the same internal target and parameter identities, produces the same fingerprint.

`no_change` and other non-`ok` items are not stored, because they prevent intent creation.

### Fingerprint

`intent_fingerprint` is a collision-resistant cryptographic digest over an unambiguous, versioned canonical encoding of the immutable semantic contents of that intent.

The encoded contents must include:

- instance and document binding;
- internal target and parameter identities;
- the ordered approval-preview payload, including displayed names, truncation flags, the nested CAP-0004 `data_type` object, exact `before` state, exact `proposed` state, and the internal request position;
- a fingerprint schema/version identifier.

The same ordered semantic contents produce the same fingerprint. Any change to that ordered content, including a reordering or a displayed-name change, produces a different fingerprint.

The exact digest algorithm and canonical encoding may remain implementation-level, provided tests can repeat the same encoding and the same digest. Weak or runtime hashes, including `GetHashCode()`, are not acceptable.

The fingerprint is not authorization and not approval. ADR-0008 binds later human approval to this fingerprint; possession of the fingerprint does not grant that approval.

Clients treat `intent_ref` and `intent_fingerprint` as opaque equality material. They must not parse either value or rebuild an intent from it.

A repeated preview is not required to reuse an existing `intent_ref`. It must reuse the fingerprint when the ordered semantic contents match.

## What this capability does not do

- no Revit `Transaction`, `SubTransaction`, or `TransactionGroup`;
- no `Parameter.Set` or equivalent mutation;
- no save or synchronize;
- no worksharing checkout, borrow, or relinquish;
- no automatic use of `WorksharingUtils.GetCheckoutStatus` as eligibility;
- no owner or username in the result;
- no type-parameter preview that can become `ok`;
- no ElementId or reference-parameter update;
- no clear, unset, or null;
- no approval provider, MRTR interaction, MCP App, or Revit UI;
- no apply.

ADR-0008 allows a later preview to show checkout status as advisory display. CAP-0007 v1 does not, because the checkout owner is a username and checkout status is not evidence the update will succeed.

## Transport-neutral request

Conceptually:

```text
PreviewParameterUpdatesRequest
{
    document_id
    updates[1..20]
      element_ref
      parameter_ref
      value
}
```

It contains no:

- `instance_id`;
- MCP metadata;
- LLM/provider information;
- process id;
- pipe name;
- file path;
- user identity;
- agent-controlled timeout;
- TTL override;
- display names used as identity.

Transport-neutral validation must reject with `INVALID_PARAMETER_UPDATE_PREVIEW` when, at minimum:

- `document_id` is absent or null;
- `updates` is missing, empty, or longer than 20;
- any update is missing a non-null `element_ref` or `parameter_ref` string, or a `value`;
- pairs are not unique under ordinal equality of the raw strings;
- `value` is not one of the closed variants, including a string longer than 512 characters, a non-Int32 or fractional integer, a non-finite quantity, or an empty `unit_type_id`;
- a clear, unset, or null value is proposed.

Do not treat empty or whitespace `document_id`, `element_ref`, or `parameter_ref` strings as omitted. Those present strings remain opaque and are handled by the document guard or item statuses.

## Errors

Server routing, unchanged from the existing tools:

```text
NO_REVIT_INSTANCE
INSTANCE_REQUIRED
INSTANCE_NOT_FOUND
INSTANCE_UNAVAILABLE
```

Capability:

```text
NO_ACTIVE_DOCUMENT
DOCUMENT_CONTEXT_CHANGED
UNSUPPORTED_DOCUMENT_KIND
DOCUMENT_NOT_WRITABLE
INVALID_PARAMETER_UPDATE_PREVIEW
INTENT_CAPACITY_REACHED
REVIT_EXECUTION_TIMEOUT
REVIT_EXECUTION_FAILED
```

MCP-boundary malformed requests remain future Server `INVALID_REQUEST`, consistent with existing tools.

Expected per-item misses and ineligible updates remain item statuses on a successful preview with `ready = false`. They are not these capability errors.

`INTENT_CAPACITY_REACHED` is the exception: the batch was otherwise ready, but the store refused to evict a live intent. That is a capability error, not `ready = false`.

`REVIT_EXECUTION_TIMEOUT` means the caller stopped waiting. It is not proof that no intent was created. See Execution.

## Execution

Follow the existing capability pattern:

1. validate the transport-neutral request before Revit execution;
2. execute Revit API access only through EXEC-0001;
3. require an active project document that is not read-only;
4. validate exact `document_id` before resolving items;
5. resolve every update in request order;
6. create no Revit transaction;
7. store an intent only after every item is `ok` and the bounded store accepts it.

No Revit API object escapes into Contracts or into stored intent state.

Timeout and cancellation follow EXEC-0001. The Bridge bounds the caller with a local wait. On timeout it stops waiting. The Addin cannot know whether the MCP caller received the response, and intent storage can race response delivery. CAP-0007 does not add an acknowledgement or claim protocol to close that race.

The achievable invariant is:

- cancellation or timeout before the EXEC-0001 work item begins: the preview does not run and no intent is created;
- once execution has begun inside `Execute`, do not interrupt the Revit thread and do not abort the running preview;
- if the caller times out or cancels while that running preview then completes successfully, an otherwise-valid intent may be stored and remain unreported to that caller;
- that orphan is not discoverable or listable by any caller;
- it remains usable only by a later caller that already possesses its opaque high-entropy `intent_ref`, and only together with the later approval requirements ADR-0008 defines;
- it expires and is purged on the same 10-minute lifetime, document close, and process exit as any other intent;
- `REVIT_EXECUTION_TIMEOUT` must not be reported as proof that no intent was created.

Store an intent only after the full preview is known and every item is `ok`. Do not store a partial intent. Do not store an intent when the preview itself fails.

## Bounds / context efficiency

This capability satisfies ADR-0005 by previewing only the requested batch:

- 1..20 explicit updates, independent of model size;
- no Cartesian expansion;
- exactly one result item per update;
- string values and human names bounded to 512 characters;
- no all-parameter mode;
- no whole-document scan;
- no raw internal-unit doubles;
- compact item statuses for expected misses;
- `intent_ref` and `intent_fingerprint` instead of echoing the stored before/proposed snapshots a second time as a hidden blob. The ordered approval-preview payload is the authoritative preview content, and the fingerprint binds that payload.

## Context / security boundaries

CAP-0007 does not return:

- file paths;
- usernames or checkout owners;
- numeric durable `ElementId` or `Parameter.Id`;
- raw parameter storage identifiers;
- raw internal-unit doubles;
- formulas, geometry, or connectors;
- live Revit API objects;
- write authorization or approval.

`readOnlyHint: false` is not approval and is not evidence of a model write.

## MCP success policy

Successful preview, including `ready = false`:

```text
isError = false
structuredContent = PreviewParameterUpdatesResult
content = []
```

Capability and routing errors:

```text
isError = true
structuredContent absent
one compact JSON TextContent error
```

The official MCP tool is specified here. Do not register it in this specification. `tools/list` remains exactly the six accepted read tools until a later accepted Server specification adds this tool.

## Compatibility

RevitMCP continues to target Revit 2025, 2026, and 2027 from one shared Addin project. The APIs named here (`StorageType`, `HasValue`, `IsReadOnly`, `UserModifiable`, `GetDataType`, `SpecTypeId.Int.Integer`, `UnitUtils`, `Document.IsFamilyDocument`, `Document.IsReadOnly`, and `GetOrderedParameters`) belong to that supported matrix.

This specification does not claim live validation on any Revit version.

Do not treat compile-time availability as live proof.

### Relationship to existing contracts

These differences are intentional and do not change the accepted read contracts:

- CAP-0005 may truncate a string value for a read. CAP-0007 refuses a before-state that would have to be truncated.
- CAP-0005 returns every integer storage value as an integer, including Yes/No. CAP-0007 previews integer updates only for `SpecTypeId.Int.Integer`.
- CAP-0005 does not accept a caller-selected unit. CAP-0007 converts the before-value into the proposed unit so the preview diff is comparable. CAP-0005 stays unchanged.
- CAP-0004 `read_only_on_count` stays observational. CAP-0007 re-checks the occurrence.

ADR-0008 leaves TTL length and store capacity to later specifications. This capability fixes the v1 TTL at 10 minutes and defers the capacity number, while requiring fail-closed behavior.

## Acceptance criteria

CAP-0007 is acceptable as a capability contract when:

1. It is one preview capability and performs no Revit model modification.
2. v1 accepts project documents only. A family document is `UNSUPPORTED_DOCUMENT_KIND`.
3. `Document.IsReadOnly == true` at execution time is `DOCUMENT_NOT_WRITABLE` and creates no intent.
4. `Document.IsModifiable` is not a permission signal.
5. `updates` contains 1..20 unique raw `(element_ref, parameter_ref)` pairs.
6. Ref content is opaque and is never trimmed, normalized, or parsed.
7. `instance_id` remains Server routing-only and is absent from the transport-neutral request.
8. `document_id` is a required opaque string with the existing exact-match guard.
9. The proposed value is the closed `string` / `integer` / `quantity` union, with the bounds in this specification.
10. There is no clear, unset, null, ElementId, or reference proposed value.
11. Only instance-source parameter refs can be `ok`. Type-source refs are `unsupported_parameter_source`.
12. Integer eligibility requires `StorageType.Integer` and `GetDataType()` exactly `SpecTypeId.Int.Integer`.
13. Quantity eligibility requires Double storage, a measurable spec, and a supplied unit that passes `IsUnit` and `IsValidUnit`.
14. Quantity preview uses typed unit conversion, never localized `SetValueString`.
15. Write eligibility re-resolves the occurrence and requires `IsReadOnly == false` and `UserModifiable == true`.
16. CAP-0004 `read_only_on_count` is not writeability evidence.
17. A valued string longer than 512 characters is `unsupported_value`, not a truncated before-state.
18. Quantity `before` is expressed in the proposed `unit_type_id`.
19. Result order matches request order, with one item per update. Array position is the human-visible order. There is no public `order` field.
20. `no_change` is distinct from `ok` and prevents intent creation.
21. An intent is stored only when every item is `ok`. Otherwise `ready = false` and no intent is stored, except the EXEC-0001 timeout race in criterion 33.
22. `intent_ref`, `intent_fingerprint`, and `expires_at` are present only when the caller receives `ready = true`.
23. TTL is fixed at 10 minutes and is not caller-extendable.
24. Stored state is ephemeral, immutable, process- and open-document-scoped, and free of live Revit API wrappers. It retains the ordered approval-preview payload as RevitMCP-owned snapshots.
25. The store fails closed with `INTENT_CAPACITY_REACHED` instead of evicting a still-valid intent.
26. The fingerprint is a collision-resistant cryptographic digest of a versioned canonical encoding of the ordered semantic contents, including the approval-preview payload. `GetHashCode()` is not acceptable. The fingerprint is not approval.
27. MCP hints are `readOnlyHint=false`, `destructiveHint=false`, `idempotentHint=false`, and `openWorldHint=false`, with the no-model-write explanation above.
28. All Revit API access executes through EXEC-0001.
29. No Revit transaction, save, synchronize, or worksharing checkout occurs.
30. Contracts remain transport-neutral. Server remains Revit-API independent.
31. CAP-0008/apply, Bridge, Server, MRTR, approval providers, MCP Apps, and UI are not created by this specification.
32. CAP-0001 through CAP-0006 and ADR-0008 remain unchanged by this specification.
33. Timeout before EXEC-0001 begins creates no intent. Timeout after execution begins does not abort the Revit thread. A completed preview may leave an unreported intent. That orphan is not listable, expires normally, and a timeout response is not proof that no intent exists. CAP-0007 defines no acknowledgement protocol.
34. Stored intent order matches request order through internal request-position metadata, not a public `order` field. Reordered updates produce a different fingerprint. Same ordered semantic contents produce the same fingerprint.
35. `data_type` is the CAP-0004 object. Optional `forge_type_id` is nested inside `data_type`. No sibling `forge_type_id` field exists.

## Explicitly deferred

- CAP-0008 and every apply path;
- approval providers, MRTR, MCP Apps, and Revit product UI;
- Bridge and Server specifications for this tool;
- intent-store capacity number and persistence technology;
- the concrete cryptographic digest and canonical encoding, within the collision-resistance requirement above;
- an acknowledgement or claim protocol for the EXEC-0001 timeout race;
- type-parameter updates;
- ElementId and reference-parameter updates;
- clear, unset, and null;
- Yes/No and other non-integer-spec integer updates;
- element creation, deletion, and geometry changes;
- worksharing checkout automation and checkout display;
- save and synchronize;
- caller-extendable TTL;
- an MCP SDK upgrade;
- live Revit validation.

## Sequencing

CAP-0007 is accepted as a transport-neutral capability contract only. Acceptance does not authorize implementation.

Expected future sequence, none of which is authorized by this specification:

```text
implementation specification for intent storage
-> Bridge design/implementation
-> Server design/implementation
-> CAP-0008 apply specification
```

Do not create those artifacts in this specification.

Do not register the MCP tool until a later accepted Server specification says to. Current `tools/list` remains exactly six tools.

## References

- ADR-0005: Agent context and token efficiency
- ADR-0006: Document and element reference identity
- ADR-0008: Controlled write safety model
- CAP-0004: `revit_describe_parameters`
- CAP-0005: `revit_get_parameter_values`
- EXEC-0001: serialized Revit execution dispatcher
- Autodesk Revit API `Document` class, including `IsReadOnly` and `IsModifiable`: https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/db03274b-a107-aa32-9034-f3e0df4bb1ec.htm
- Autodesk Revit API `Document.IsModifiable`: https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/af884262-3ba2-b0a0-d7ef-f0a49c1bf1bc.htm
- Autodesk Revit API `Parameter` class, including `IsReadOnly` and `UserModifiable`: https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/333ff41b-e6a7-d959-60bf-c3bfae495581.htm
- Autodesk Revit API `ExternalDefinition.UserModifiable`: https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/4568f90c-7d4b-c9f2-da59-1540ca14a22f.htm
- Autodesk Revit API `Definition.GetDataType()`: https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/1c008d27-9e61-362c-308c-8b718ee0f8df.htm
- Autodesk Revit API `FormatOptions.SetUnitTypeId`, which requires `UnitUtils.IsUnit`: https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/756cf4e7-b124-2703-3335-35f376f2c676.htm
- MCP tools, including annotation hints: https://modelcontextprotocol.io/specification/2026-07-28/server/tools
