# ADR-0026: Assistant-local proposal registry

- Status: Accepted
- Date: 2026-11-24
- Product Owner: Dave

Accepted for the conversation-coordination slice described here. Parameter-update proposals are not part of this decision.

## Context

The Assistant process already stores two untrusted proposal families:

- MEP device placement, in `MepDevicePlacementProposalStore` ([ADR-0016](ADR-0016-assistant-structured-proposals-and-controlled-write-orchestration.md));
- room pattern, in `RoomPatternStore` ([ADR-0017](ADR-0017-pattern-intelligence-and-spatial-grounding.md) through [ADR-0021](ADR-0021-controlled-pattern-application-against-linked-architecture.md)).

Each store owns its typed payload, its preview receipt, and its apply receipt. Each store can clear itself when its own source attachment changes, when the conversation resets, or when the Assistant shuts down. Nothing in the process says which of those proposals is the one current proposal for the conversation. A later MEP preview, review, or apply can still run after a room pattern has replaced the conversation's proposal, and the reverse is also true.

ADR-0016 already says one conversation holds one active proposal. That rule was written for the MEP proposal. It now has to cover every proposal kind the Assistant stores, without turning those stores into one payload or moving controlled-write authority out of Revit.

Preview, review, and apply stay on the existing Assistant orchestration. Approval, stale intent checks, audit, and exactly-once apply stay on ADR-0008, ADR-0010, and ADR-0011. `EphemeralWriteIntentStore` stays the Revit-local intent store. This decision does not add `bimit_propose_parameter_updates`.

## Decision drivers

- At most one proposal is current in a conversation, across proposal kinds.
- A new proposal, including a same-kind replacement, invalidates the previous generation.
- Domain preview, review, and apply fail closed when their generation is no longer current.
- Reset, New Chat, and Assistant shutdown invalidate proposal authority.
- Replacing or removing an attachment invalidates a current proposal that depends on that artifact, and leaves a proposal that does not.
- The model does not receive the coordination token and cannot use it as authority.
- MEP and room-pattern payloads, preview receipts, and apply receipts stay in their existing stores.
- The registry does not become an intent store, an approval provider, an audit log, an MCP tool, a Bridge service, or a Revit identity authority.
- Production `tools/list` stays 22. Bridge protocol stays 25. No new model-facing schema.

## Options considered

### Option A: One universal proposal payload

Merge both stores into a single document that can represent an MEP placement, a room pattern, and later a parameter update.

Advantages:

- one object is obviously the current proposal;
- invalidation has one implementation.

Disadvantages:

- the families do not share a payload, a preview receipt, or an apply receipt;
- a universal document would move domain rules into the coordinator;
- parameter updates would be pulled into the model before they are specified.

### Option B: A thin Assistant-local registry beside the existing stores

The Assistant process keeps one volatile registry. The registry records the current kind, a monotonic in-process generation, a bounded presentation state, and an optional source-artifact id. The existing stores keep their typed state. They register when they accept a proposal and they refuse preview, review, and apply when their generation is no longer current.

Advantages:

- ADR-0016's one-active-proposal rule extends across kinds without a shared payload;
- controlled-write state stays in the stores and in the Revit intent path;
- a later kind can register without changing MEP or room-pattern contracts.

Disadvantages:

- every store that accepts a proposal must take the same registry;
- a stale domain payload can still exist after it loses authority, so operations must check the registry instead of trusting a non-null payload.

### Option C: Coordinate currentness in the Revit intent store

Use `EphemeralWriteIntentStore`, the approval provider, or the audit log as the conversation's proposal owner.

Advantages:

- the write path already has a strong identity for an intent.

Disadvantages:

- a proposal exists before any intent, and a `needs_calibration` proposal has no intent;
- it would move untrusted conversation state into the trusted write boundary;
- the Assistant would become able to treat intent or approval state as its own.

### Chosen direction

**Option B is accepted.** Option A is rejected for this slice. Option C is rejected. Parameter-update proposals remain future work and are not registered by this decision.

## Decision

### 1. One Assistant-process registry

`AssistantProposalRegistry` lives in the Assistant process. The Assistant host creates one registry for the process and passes it to `MepDevicePlacementProposalStore` and `RoomPatternStore`. The registry is memory only. It is not serialized, not restored, and not shared with another process.

The registry holds either nothing or one current registration:

| Field | Meaning |
|---|---|
| Kind | `MepDevicePlacement` or `RoomPattern` |
| Generation | A `long` that increases when a proposal becomes current and when that current registration is released. It is never reused. |
| Presentation | A closed set: `needs_calibration`, `ready`, or `proposed` |
| Source artifact | Optional id of the attachment the proposal depends on. Empty means unbound. |

The generation is an internal coordination token. It is not an `intent_ref`, not a document id, not an element reference, and not model authority. Model results, IPC messages, and MCP schemas do not gain this token.

### 2. Current and stale

Activating a proposal replaces the current registration and returns a new generation. The previous generation, including a previous proposal of the same kind, is stale. Domain stores still replace their own payloads on same-kind replacement. They do not delete the other family's payload merely because it became stale.

A domain operation that previews, reviews, applies, or otherwise commits the proposal as current must fail closed when its generation is not the registry's current generation. A stale payload is still owned by its store. It cannot open a preview, a Revit review, or an apply. An in-flight operation that finishes after its generation was replaced does not record a new receipt.

Reset and New Chat clear both stores. Assistant shutdown clears both stores. The store that holds the current generation releases it. A second clear finds nothing current and does not create a new current registration. After either path, no generation is current.

### 3. Attachment binding

The registry drops the current registration when all of these are true:

- the registration has a source artifact id;
- that id is the attachment being replaced or removed;
- the id differs from the attachment that remains.

A current proposal with no source artifact stays current. A current proposal bound to a different artifact stays current. The domain store for a proposal bound to the changed artifact still clears its own payload, whether or not that proposal is the current one. That keeps the existing per-store attachment rule.

### 4. What the registry does not own

The registry does not store placement offsets, pattern geometry, preview receipts, apply receipts, `intent_ref`, approval state, audit events, document identity, element identity, or Revit API state. Those remain in the domain stores and on the existing controlled-write path.

The registry is not `EphemeralWriteIntentStore`, not an approval provider, not an audit store, not an MCP capability, not a Bridge service, and not a Revit identity authority. This decision adds no MCP tool, no Bridge RPC, no protocol version, and no Revit transaction.

### 5. Presentation

MEP registration uses the proposal's existing `needs_calibration` or `ready` state. Promoting a current MEP proposal to `ready` updates that presentation without a new generation. Room-pattern registration uses `proposed`. Any other presentation is rejected. The registry does not invent preview, review, or apply states.

### 6. Deferred

This slice does not add parameter-update proposals, a model-facing registry schema, or a universal payload. A future proposal family must take the same registry and the same fail-closed rule before it can become current. Live Revit validation of this coordination slice is not part of the decision.

## Consequences

- ADR-0016's one-active-proposal rule now applies across MEP device placement and room pattern. ADR-0016's MEP contract is otherwise unchanged. This ADR does not supersede ADR-0016, ADR-0008, ADR-0010, or ADR-0011.
- A room pattern accepted while an MEP proposal is current makes that MEP proposal unable to preview, review, or apply. The reverse is also true. The displaced payload remains in its store until that store's own clear rule runs.
- The placement presentation reported to the pane follows the MEP store only while that MEP proposal is current.
- Production `tools/list` stays 22. Bridge protocol stays 25.
- Tests that construct one store keep a private registry, so a single-family test stays current until that store replaces, clears, or loses its attachment. The Assistant host is what shares one registry between the two families.

## References

- [ADR-0008](ADR-0008-controlled-write-safety-model.md)
- [ADR-0010](ADR-0010-revit-local-trusted-approval-authority.md)
- [ADR-0011](ADR-0011-v1-controlled-write-audit.md)
- [ADR-0013](ADR-0013-bim-it-assistant-local-architecture.md)
- [ADR-0016](ADR-0016-assistant-structured-proposals-and-controlled-write-orchestration.md)
- [ADR-0017](ADR-0017-pattern-intelligence-and-spatial-grounding.md)
- [LIFECYCLE-0002](../lifecycle/LIFECYCLE-0002-ephemeral-write-intent-store.md)
