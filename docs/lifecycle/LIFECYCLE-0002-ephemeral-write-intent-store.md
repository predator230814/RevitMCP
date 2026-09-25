# LIFECYCLE-0002: Ephemeral write-intent store

- Status: Accepted
- Date: 2026-09-25

## Purpose

Define the Addin-owned ephemeral store required by accepted ADR-0008 and accepted CAP-0007.

The store holds one immutable preview intent until it expires, the document closes successfully, or the Revit process ends. It performs no Revit model mutation. It does not start a transaction, call `Parameter.Set`, save, or synchronize.

This specification does not implement the store. Acceptance records the contract only. It does not authorize a Revit model write, a Bridge or Server specification, or CAP-0008.

## Ownership

There is one intent store per RevitMCP Addin process lifetime.

Ownership stays inside the Revit-local Addin boundary. The store is not a Server, Bridge, MCP-client, or orchestrator responsibility. It is not written into the ADR-0003 registration file, the Revit model, or any durable database.

Entries are scoped to `instance_id` + `document_id`. `instance_id` is the process-lifetime identity from LIFECYCLE-0001. `document_id` is the opaque open-document identity from ADR-0006. This specification does not change ADR-0006 public `document_id` semantics.

The store is empty when the process starts. Process restart never restores intents.

## What may be stored

Stored data is immutable RevitMCP-owned state only:

- strings;
- typed preview values;
- identities already used to mint opaque refs;
- timestamps and monotonic expiry metadata;
- ordering metadata;
- fingerprint metadata.

No live Autodesk Revit API wrapper may be retained. No `Document`, `Element`, `Parameter`, `Connector`, or other native-wrapper lifetime may become intent state. Preview may touch those objects only inside the EXEC-0001 call that builds the snapshot. The snapshot copied into the store must already be free of them.

Do not store a separate numeric `ElementId`, `Parameter.Id`, or raw-id field. Do not store raw internal-unit doubles, paths, or usernames.

The current local-parameter identity may already embed a numeric Revit id inside `ClassifiedParameterIdentity.StableKey`. `RevitParameterIdentity.ResolveLocalKey` uses a `ParameterElement.UniqueId` when one exists, and otherwise a `local:` prefix plus `definition.Id.Value` or `parameter.Id.Value`. Copy that existing `StableKey` verbatim as an opaque internal document-lifetime token. The canonical fingerprint treats it as that exact string. Do not parse, reinterpret, or promote it to the MCP or public contract. This specification does not redesign CAP-0004 parameter identity.

## `intent_ref`

`intent_ref` is opaque.

Generation:

- draw at least 256 bits (32 bytes) from a cryptographically secure random source;
- the production source is the BCL random-number generator already available to the Addin;
- tests replace that source through an abstraction;
- do not add a NuGet package solely to generate the token.

The stored and returned token is the lowercase hexadecimal encoding of those 32 bytes: 64 characters from `0-9` and `a-f`. That encoding has no element, parameter, document, time, or fingerprint meaning.

`intent_ref` is not derived from `intent_fingerprint`. It is not sequential. Clients must not parse it.

There is no public listing, prefix search, enumeration, or "recent intents" operation. Lookup is only by exact ordinal match of the full `intent_ref`.

If a newly drawn ref is already present, draw again. Do not overwrite the existing entry. After 8 colliding draws in one creation attempt, fail that creation, store nothing, and do not report `INTENT_CAPACITY_REACHED`. A later CAP-0007 implementation maps that unexpected failure to `REVIT_EXECUTION_FAILED`.

## Lifetime

CAP-0007 fixes the TTL at 10 minutes. The caller cannot extend, refresh, or choose it.

At creation the store records:

- `created_at`, the UTC instant from the injectable UTC clock;
- `expires_at`, `created_at` plus exactly 10 minutes, which is the contract timestamp CAP-0007 returns as an ISO-8601 `Z` value.

Expiry enforcement uses a separate injectable monotonic elapsed-time source captured at creation. An intent is expired when monotonic elapsed time since that capture is greater than or equal to 10 minutes.

`expires_at` is not the enforcement clock. Comparing the current wall clock with `expires_at` must not decide validity. Moving the wall clock backward or forward must not extend a live intent. A pure test must be able to advance the monotonic clock and the UTC clock independently.

Expired intents are invalid. `TryGet` does not return them as valid. They may be removed lazily. The next capacity check purges them before it counts live entries. `PurgeExpired` removes them immediately.

Successful document close invalidates every intent for that `document_id`, including unexpired ones.

A cancelled or failed close does not invalidate intents for a document that is still open.

Process shutdown clears every entry, including unexpired ones. A restarted process has an empty store.

## Capacity

v1 allows at most **64** unexpired intents in one Revit process. The limit is per process, not per document.

Before evaluating capacity, purge expired entries.

If 64 unexpired entries remain, creation fails closed with CAP-0007 `INTENT_CAPACITY_REACHED`. Store nothing. Do not return a success-shaped preview. Never evict an unexpired intent to make room. No LRU, FIFO, or other replacement of live intents is allowed.

Document forget and shutdown clear are removal, not eviction-for-capacity. They do not create a replacement slot by discarding a live intent in order to accept a new one during the same failed creation.

## Stored entry

One entry contains only:

- `instance_id`;
- `document_id`;
- `created_at` and monotonic expiry metadata;
- `expires_at`;
- `intent_ref`;
- the ordered internal target identity for each update;
- the ordered parameter identity and source for each update;
- the ordered CAP-0007 approval-preview payload, including internal `request_position`;
- the exact typed before and proposed snapshots inside that payload;
- fingerprint schema version;
- `intent_fingerprint`.

No extra hidden mutation instruction is stored. There is no apply flag, transaction id, approval bit, or "set this parameter" command beyond the proposed typed value already in the preview payload.

### Internal identities

The internal target identity is the ADR-0006 element identity string already used as `element_ref`, copied into RevitMCP-owned memory. In v1 it must be ordinally equal to the payload `element_ref`. It is not a numeric `ElementId` and not an `Element`.

The parameter identity is a copy of the classified binding the Addin already uses to mint `parameter_ref`:

- source, which v1 stores only as `instance`;
- identity kind `built_in`, `shared`, or `local`;
- optional parameter type id;
- optional shared GUID string;
- `StableKey`, copied verbatim from the existing classified identity.

Those fields are strings and a fixed source token. They are not a `Parameter`. `StableKey` stays an opaque internal token, including when the current local fallback was derived from a numeric Revit id. A `type` source is rejected at creation because CAP-0007 does not store type-parameter updates.

`request_position` is the 1-based request index from CAP-0007. It is internal metadata. It is not an MCP result field.

## Fingerprint

`intent_fingerprint` is the lowercase hexadecimal SHA-256 digest of one unambiguous versioned canonical byte string. SHA-256 is the BCL implementation. Do not add a package for it.

The digest covers the semantic intent. It does not cover `intent_ref`, `created_at`, `expires_at`, or store capacity. The same ordered semantic intent therefore produces the same fingerprint after a new ref is drawn.

`GetHashCode()` and every non-cryptographic hash are forbidden.

The fingerprint is not approval, authentication, or authorization.

### Canonical bytes

Schema version is the unsigned integer **1**. The encoding is big-endian and has no whitespace, BOM, or trailing padding.

```text
u32 schema_version          // 1
string instance_id
string document_id
u32 item_count
item[item_count]            // request order, not sorted
```

Each item, in this fixed order:

```text
u32 request_position        // 1-based
string element_ref
string parameter_ref
string source               // "instance"
string identity_kind        // "built_in" | "shared" | "local"
optional-string parameter_type_id
optional-string shared_guid
string stable_key
string status               // "ok"
string element_name
bool element_name_truncated
string category_name
bool category_name_truncated
string parameter_name
bool parameter_name_truncated
string data_type_kind       // measurable_spec | spec | category | unknown
optional-string forge_type_id
bool before_has_value
value before_value          // present only when before_has_value is true
value proposed
```

Encodings:

- `string`: `u32` byte length, then that many UTF-8 bytes, with no Unicode normalization and no trimming;
- `optional-string`: one byte `0` when absent, or one byte `1` followed by a `string` when present; absence is not the same as a present empty string;
- `bool`: one byte `0` or `1`;
- `u32`: four bytes, big-endian;
- `value`: one byte kind, then the payload;
- kind `1` string: a `string` of 0..512 characters, matching the CAP-0007 string;
- kind `2` integer: four bytes, big-endian two's-complement Int32;
- kind `3` quantity: IEEE-754 binary64, big-endian, then a non-empty `string` `unit_type_id`.

`-0.0` and `+0.0` are the same canonical quantity value. Encode both as `+0.0`. Non-finite quantities are rejected and are not stored. Arrays and item sequences keep the given order. Optional fields use the presence byte above, so a later encoder cannot choose another representation for "missing".

Changing any included semantic field, including request order, a displayed name, a truncation flag, `before`, or `proposed`, changes the canonical bytes and the fingerprint. Reordering two updates changes it even when the set of pairs is the same.

Creation rejects a snapshot that cannot be encoded under this schema. It stores nothing in that case.

## Concurrency

Store operations are thread-safe inside the process.

Intent creation is atomic. The expired purge, the live-count check, ref allocation, and insertion are one decision. A concurrent create cannot observe a half-inserted entry and cannot push the live count above 64.

A duplicate random ref is regenerated inside that same decision. It does not overwrite.

Document cleanup and expiry purge must not race a lookup into returning a removed or expired intent as valid. After a successful forget, purge, or clear, `TryGet` for that ref is absence.

Do not introduce distributed locking or cross-process coordination. One in-process mutual exclusion is enough.

## Timeout and orphans

Preserve CAP-0007 and EXEC-0001.

- Cancellation or timeout before the EXEC-0001 work item begins does not call creation. No intent is stored.
- Once execution has begun, do not interrupt the Revit thread.
- A preview that then completes as valid may call creation even though the Bridge caller already stopped waiting.
- That orphan is a normal stored intent. It is not listable or discoverable. It expires, forgets, and clears under the same rules as any other entry.
- A timeout response is not proof that no intent was created.
- This specification adds no acknowledgement, claim, or recovery protocol.

## Lifecycle integration

Reuse the existing two-phase `DocumentClosing` / `DocumentClosed` correlation. `DocumentClosing` `DocumentId` is only a temporary event-pair key. It is not a RevitMCP `document_id`, and cleanup must not treat it as document identity.

On successful `DocumentClosed` only:

1. call `OpenDocumentIdentityService.TryGet(document, out documentId)`, or the equivalent non-creating lookup;
2. do not call `GetId` or `GetOrAssign` during close cleanup;
3. if no mapping exists, skip intent forget;
4. if a mapping exists, capture that existing `document_id` before forgetting the identity mapping, then forget intents for that id;
5. forget the document's parameter-ref map;
6. forget the document identity last.

Cancelled and failed closes leave the pending correlation without calling forget. Intents, parameter refs, and `document_id` for that still-open document stay.

Shutdown clears intent state before process-owned bridge and dispatcher resources are fully released. Entering LIFECYCLE-0001 stopping closes the store and removes every entry. A later `TryCreate` in that process fails and stores nothing, including a preview that was already running. Restart does not reload the cleared entries.

## Internal API

Names below are conceptual. The implementation chooses the final type names.

The store supports only:

```text
TryCreate(snapshot) -> created(intent_ref, intent_fingerprint, expires_at) | capacity_reached | rejected
TryGet(intent_ref) -> entry | absent
ForgetDocument(document_id) -> removed
PurgeExpired() -> removed
Clear() -> removed
```

`TryGet` returns an entry only for an exact ref that is still unexpired and not forgotten. Absence covers unknown, expired, forgotten, and cleared refs. It does not reveal which of those occurred.

`ForgetDocument` removes matches and does not return their payloads.

Do not add listing, search, apply-state, approval-state, or transaction-state operations. ADR-0008 retry and terminal apply state belong to a future CAP-0008 design.

## Pure tests

A future implementation must cover these cases without launching Revit:

1. The ref comes from the injected 32-byte source, is 64 lowercase hex characters, and a second draw produces a different ref. A colliding draw is regenerated instead of overwriting.
2. The same ordered semantic snapshot produces the same SHA-256 fingerprint.
3. Swapping update order changes the fingerprint.
4. Changing a displayed element, category, or parameter name changes the fingerprint.
5. Changing `before` or `proposed` changes the fingerprint.
6. Quantity `-0.0` and `+0.0` produce the same canonical bytes and fingerprint.
7. An intent is valid before 10 monotonic minutes and absent at 10 monotonic minutes.
8. Moving the UTC clock backward or forward does not extend monotonic expiry.
9. Purging an expired intent frees a capacity slot.
10. 64 live intents are accepted and the 65th returns `INTENT_CAPACITY_REACHED` without storing it.
11. A live unexpired intent is never removed to admit another intent.
12. Successful close uses a non-creating `TryGet`, captures that `document_id` before forget, and forgets only that document's intents. It does not call `GetId` or `GetOrAssign`. A document with no existing mapping skips intent forget.
13. Cancelled and failed close preserve that document's intents.
14. Shutdown clear removes every intent, and a later create does not restore them.
15. No operation lists, searches by prefix, or returns recent intents.
16. Concurrent creates never leave more than 64 live intents.

## Explicitly excluded

- CAP-0007 production implementation;
- Bridge protocol v8;
- Server tool registration;
- CAP-0008 and every apply path;
- approval-provider implementation;
- MRTR;
- MCP Apps and Revit product UI;
- `Transaction`, `SubTransaction`, and `TransactionGroup`;
- `Parameter.Set` or any equivalent mutation;
- save and synchronize;
- worksharing checkout;
- an MCP SDK or other package upgrade;
- a new package for random tokens or SHA-256.

`tools/list` remains exactly the six accepted read tools.

## Relationship

CAP-0007 deferred the capacity number and the concrete digest. This specification sets that capacity at 64 and the digest at SHA-256 over the canonical bytes above. It does not change CAP-0007's preview contract, TTL, orphan rule, or approval-preview fields.

ADR-0008 already requires Addin-owned immutable snapshots and forbids retained Revit API wrappers. This specification does not change ADR-0008 and does not authorize a model write.

LIFECYCLE-0001 remains the bootstrap and shutdown specification. This document only adds intent clearance to that shutdown and to the existing successful-close forget path.

## References

- ADR-0006: Open-document identity
- ADR-0008: Controlled write safety model
- CAP-0007: `revit_preview_parameter_updates`
- LIFECYCLE-0001: Revit add-in bootstrap and shutdown
- EXEC-0001: Revit execution queue
- Existing Addin close correlation: `DocumentCloseIdentityCleanup`, `RevitDocumentCloseEventSource`, `OpenDocumentIdentityService`, `OpenDocumentParameterIdentityService`
