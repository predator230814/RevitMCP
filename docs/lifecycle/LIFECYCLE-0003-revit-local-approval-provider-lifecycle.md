# LIFECYCLE-0003: Revit-local trusted approval provider lifecycle

- Status: Accepted
- Date: 2026-10-05

## Purpose

Define the v1 lifecycle and state contract for the Revit-local, in-process trusted approval provider required by accepted ADR-0010.

The provider owns approval state separately from the immutable LIFECYCLE-0002 intent store. This specification is the checkpoint before any provider implementation and before CAP-0008/apply design.

It does not define CAP-0008 public behavior. It does not implement a provider, a UI, a Bridge method, or a Server tool.

## Relationship

ADR-0010 is Accepted. This specification refines that decision. It does not change ADR-0008, ADR-0009, ADR-0010, LIFECYCLE-0001, LIFECYCLE-0002, or CAP-0007.

`EphemeralWriteIntentStore` remains the Addin-owned immutable intent store. `IntentEntry` remains the stored snapshot: `intent_ref`, fingerprint, schema version, creation and expiry timestamps, `instance_id`, `document_id`, and the ordered preview items. `IntentStoreCreateStatus.Rejected` remains a failed insertion. It is not a human rejection.

The current successful-close path looks up an existing `document_id` without creating one, then forgets intents, parameter refs, and document identity. This specification inserts provider cleanup ahead of that intent forget. It does not move intent ownership.

The merged WebView2 spike is feasibility evidence for a lazy DockablePane presentation. It is not production UI and it is not this lifecycle contract.

## 1. Ownership

There is one trusted approval provider per RevitMCP Addin process lifetime.

It lives inside `RevitMCP.Addin`.

It is not owned by:

- `RevitMCP.Server`;
- the Bridge;
- the MCP client;
- the model;
- MCP Apps;
- an orchestrator.

The provider receives access to the existing Addin-owned `EphemeralWriteIntentStore`. Intent ownership stays in that store.

## 2. Intent store remains immutable

Do not add approval or apply state to `IntentEntry` or `EphemeralWriteIntentStore`.

Do not add any of the following to the intent entry:

- `Approved`;
- `Rejected`;
- `Consumed`;
- transaction state;
- UI session state.

Approval-provider state is separate.

## 3. Provider state is ephemeral

Provider state exists only for the Revit process lifetime.

It is not persisted:

- into the Revit model;
- into registration files;
- into Server state;
- to disk;
- into MCP `requestState`;
- into a database.

Restarting Revit restores no approval state.

## 4. Provider state is subordinate to the live intent

No provider record is sufficient by itself.

Every operation that relies on approval resolves the exact current intent through `EphemeralWriteIntentStore`.

If the intent is unknown, expired, forgotten after document close, or unavailable because the store is stopped, the provider state is unusable and the operation fails closed.

Provider state may be purged lazily. An orphaned provider record never authorizes anything after its intent is no longer live.

`TryGet` absence covers unknown, expired, forgotten, and cleared refs, as LIFECYCLE-0002 already requires. The provider does not invent a distinction the store does not return.

## 5. Decision record

For a live intent, provider-owned state may record:

- `intent_ref`;
- `intent_fingerprint`;
- `instance_id`;
- `document_id`;
- decision: `approved` or `rejected`;
- decision timestamp;
- provider/method identity;
- effective expiry;
- whether an approved decision has been consumed.

These words are the lifecycle contract. They are not a required implementation enum and they are not MCP fields.

Do not copy the full preview, before, or proposed BIM payload into the provider decision record. The authoritative payload stays in the intent store.

An active UI render model may hold that preview transiently in memory for presentation. This specification does not write raw BIM values to durable logs.

## 6. Effective lifetime

Approval never refreshes or extends the intent.

The effective approval expiry is the underlying intent expiry. A decision made near intent expiry has only the remaining intent lifetime.

v1 has no second user-configurable approval TTL.

Validity stays contingent on the intent store's monotonic expiry. Comparing wall-clock UTC with `expires_at` must not decide whether an approval is still valid. The provider treats the intent as live only when the store still returns it.

## 7. One active human review session in v1

At most one approval review session may be active per Revit process at a time.

This is a v1 simplicity and safety constraint. It is not a permanent product limit.

Multiple live intents may exist. The provider does not queue them. It exposes no list, search, or recent-intents UI contract.

A pending review occupies the single session. A recorded terminal decision ends that review session and keeps the decision record. It does not keep the session slot busy.

Conceptual `BeginReview(intent_ref)`:

- the intent is unknown, expired, forgotten, or the store is stopped: `unavailable`;
- the live intent already has a rejected, approved, or consumed decision: `terminal`;
- the same intent already has the active pending session: `already_active`, and the provider brings that session forward;
- a different intent currently has the active pending session: `busy`;
- otherwise: `started`, and the provider creates one new active review session.

A new review starts only when the current active Revit document resolves to the same existing RevitMCP `document_id` bound to the intent. The lookup is non-creating. The provider does not mint a `document_id` in order to start a review.

If another document is active, or no active document can be resolved, the review does not start and the outcome is `unavailable`. The provider does not activate or switch to another Revit document.

`already_active` is not a new start. It brings forward the pending session that was already bound to that document.

Do not define a public MCP error code for these outcomes.

## 8. Approval session binding

Each active review session has a provider-generated opaque local session correlation value.

That value correlates the rendered page with the exact currently active native session. A delayed or stale WebView2 message from an older render is rejected.

The session value is not:

- an MCP token;
- an authorization token;
- an approval attestation;
- a replacement for `intent_ref`.

It need not be secret from the bundled local UI. It must be unique enough to distinguish sessions.

Do not expose it through Server, Bridge, or MCP.

The page is never authoritative for `intent_ref`, `intent_fingerprint`, document identity, or proposed values. The native provider resolves those from the intent store and the current session.

Conceptual bounded UI messages may resemble `approveCurrent(session_ref)` and `rejectCurrent(session_ref)`. This specification does not define a final JSON schema.

## 9. Explicit human action

ADR-0010 remains in force.

Approve or Reject may be recorded only from an explicit human action in the currently rendered trusted local approval UI, for that current native-bound session.

Forbidden:

- approval on page load;
- timer-driven approval;
- model-triggered approval;
- automatic approval after an MCP request;
- a generic programmatic command that approves an arbitrary intent.

Origin checks, content security policy, and session validation constrain the channel. They do not by themselves prove a physical human gesture.

The bundled local UI is part of the v1 trusted computing base described by ADR-0010. v1 still does not claim protection against a compromised Revit process, injected in-process code, tampered binaries or UI assets, or a compromised OS or user session.

## 10. Session termination without a decision

Closing, dismissing, or destroying the active review surface without Approve or Reject:

- records no decision;
- does not reject the intent;
- leaves that still-live intent eligible for a later new review.

Hiding and showing the same still-live surface may retain the same native-bound session.

If the WebView or UI control is disposed or recreated, the prior session correlation is invalid. A new review session must be established. No stale message from the old UI instance may decide the new session.

## 11. Active document changes

While a review is still pending, if the active Revit document changes away from the session's bound document, that pending review becomes non-actionable and ends without a decision.

The person must explicitly begin the review again after returning to the intended document.

Changing views inside the same document does not by itself invalidate the review.

An already-recorded approval is not invalidated merely because the person later activates another document. A future CAP-0008 use of that approval remains subject to that capability's document-context checks and stale-state revalidation. This specification does not define those checks.

Do not define the exact Revit event that observes the active-document change.

## 12. Terminal decisions

The first valid terminal human decision wins.

`ApproveCurrent` and `RejectCurrent` revalidate before recording that decision:

- the session correlation is still current;
- the exact intent is still live in `EphemeralWriteIntentStore`;
- the stored fingerprint, `instance_id`, and `document_id` still match that intent;
- the current active document still matches the intent document, using a non-creating identity lookup.

If the intent has expired or disappeared, the active document has changed, or the document has been successfully closed before the click is processed, record no decision. End the session as non-actionable. Fail closed. The conceptual result is `unavailable`, or `invalid_session` when the correlation itself is no longer current.

Do not define the exact Revit event or implementation API that observes the active document.

`approved` cannot later become `rejected`. `rejected` cannot later become `approved`.

A duplicate or delayed message does not change a terminal decision.

A rejected intent cannot be reopened for approval in v1. If the person wants the operation later, that is a new preview and a new intent.

## 13. Approval consumption

The internal one-way operation is conceptual:

```text
TryConsumeApproved(intent_ref) -> consumed(approval_snapshot) | not_approved
```

It succeeds only when all of the following are true:

- the exact intent is still live in `EphemeralWriteIntentStore`;
- the stored provider binding matches that intent's fingerprint, `instance_id`, and `document_id`;
- the decision is `approved`;
- the approval has not already been consumed.

On success, mark the approval consumed atomically before returning success.

The returned snapshot is the decision binding. It is not a copy of the BIM preview payload.

After consumption:

- that approval can never authorize another mutation attempt;
- the same intent cannot be re-approved;
- a replay fails closed.

The provider may retain a bounded consumed tombstone until the underlying intent expires, the document closes successfully, or the process shuts down.

This is approval lifecycle state only. Do not define when CAP-0008 invokes consumption, transaction outcome, rollback, commit, verification, or apply public errors. Those remain CAP-0008 work.

## 14. Concurrency

Provider operations are thread-safe inside the process.

Invariants:

- at most one active review session;
- one intent has at most one terminal human decision;
- the first valid Approve or Reject wins;
- only one concurrent approval-consumption attempt can succeed;
- document forget and shutdown make provider state unusable;
- no approval record can authorize an absent or expired intent.

Do not introduce distributed locking. One in-process synchronization strategy is enough.

Do not hold a provider-state lock while executing arbitrary Revit API work.

## 15. Document-close lifecycle

Reuse the existing successful `DocumentClosing` / `DocumentClosed` correlation. `DocumentClosing` `DocumentId` is only an event-pair key. It is not a RevitMCP `document_id`.

Cancelled or failed close preserves provider state and preserves intent state.

Successful close with an existing `document_id` invalidates, in this logical order:

1. the active approval session for that document;
2. provider approval, terminal, and consumed state for that document;
3. intent state for that document;
4. parameter-ref state;
5. document identity.

The implementation may combine those steps under one bounded in-process synchronization, provided that order stays observable: provider state for that document is unusable before the intent forget returns, and document identity is forgotten last.

If no existing RevitMCP `document_id` mapping exists, do not create one during cleanup. Do not invent approval or intent cleanup for a missing id. Parameter-ref and document-identity forget stay as LIFECYCLE-0002 already requires.

## 16. Expiry cleanup

An expired intent means expired approval and session authority.

Provider operations fail closed immediately when the intent is expired, even if provider cleanup has not run.

A provider `PurgeExpired` operation may remove pending records, terminal records, consumed tombstones, and inactive sessions whose corresponding intents are no longer live.

No background timer is required for correctness.

## 17. Provider shutdown

Provider shutdown is idempotent.

Once stopping begins:

- no new review session may start;
- no new Approve or Reject may be recorded;
- no approval may be consumed;
- the active UI session is invalidated;
- decision and consumed state is cleared;
- UI and WebView event handlers are detached;
- the provider is unavailable.

For a future integration, provider shutdown occurs before the intent store is cleared and before Revit execution infrastructure is finally disposed. Shutdown must not wait for a person.

## 18. Provider or UI failure leaves the read core available

Failure to initialize or display the local approval UI must not make the existing RevitMCP read and preview stack unavailable.

The six read capabilities and CAP-0007 preview remain usable.

A missing or faulted approval UI or provider means a future apply fails closed.

WebView2 startup is not part of required RevitMCP bootstrap readiness. WebView2 creation remains lazy.

A failed explicit UI initialization may be retried on a later explicit review request after the failed control and handlers are cleaned up. Do not run an automatic retry loop.

## 19. Presentation remains separate

The preferred v1 presentation remains:

```text
Revit DockablePane
-> WPF
-> bundled local WebView2
```

That surface is presentation and input. The native provider is the authority.

This specification defines provider, session, and decision state. It does not define production HTML, CSS, a final JavaScript protocol, layout, branding, a broader conversational UI, or MCP Apps.

## 20. No public approval enumeration

Do not add:

- `ListApprovals`;
- `ListPendingIntents`;
- recent-intents search;
- prefix search;
- a public approval-history lookup.

The provider operates only on an exact known `intent_ref` and the single current local review session.

Audit design remains separate.

## 21. No Revit API wrappers in provider state

Provider decision and session state must not retain `Document`, `Element`, `Parameter`, `Connector`, `UIApplication`, or any other Revit API wrapper as durable provider state.

Store only RevitMCP-owned immutable identifiers and bounded UI and session metadata.

Transient UI integration may touch Revit UI objects only inside a valid Revit UI lifecycle context.

## 22. Internal conceptual API

Names are conceptual. The implementation chooses the final type names. These operations are not MCP tools and not Bridge RPC methods.

```text
BeginReview(intent_ref)
  -> started | already_active | busy | unavailable | terminal

ApproveCurrent(session_ref)
  -> recorded | invalid_session | unavailable

RejectCurrent(session_ref)
  -> recorded | invalid_session | unavailable

DismissCurrent(session_ref)
  -> dismissed | invalid_session

TryConsumeApproved(intent_ref)
  -> consumed(approval_snapshot) | not_approved

ForgetDocument(document_id)
  -> removed

PurgeExpired()
  -> removed

Stop()
  -> cleared
```

`ApproveCurrent` and `RejectCurrent` record a decision only for the current session correlation, only as an explicit human action under section 9, and only after the revalidation in section 12. `invalid_session` covers a missing, stale, or superseded correlation. `unavailable` covers a stopped provider, an intent the store no longer returns as live, or an active document that no longer matches the intent.

`DismissCurrent` records no decision.

`not_approved` covers every consume failure: missing intent, mismatch, rejected, pending, or already consumed. It does not reveal which of those occurred.

## 23. Pure tests for a future implementation

A future implementation must cover these cases without launching Revit:

1. An unknown intent cannot create a review.
2. An expired intent cannot create a review.
3. At most one review session is active.
4. A repeated `BeginReview` for the same pending intent reuses the current session.
5. `BeginReview` for a different intent while one session is active returns `busy`.
6. A stale or wrong session correlation cannot approve.
7. An explicit approve records one terminal decision.
8. An explicit reject records one terminal decision.
9. Approve followed by reject does not flip the decision.
10. Reject followed by approve does not flip the decision.
11. Dismissal records no decision and permits a later review of that still-live intent.
12. Disposing or recreating the UI invalidates the old session correlation.
13. An active-document change ends a pending review without a decision.
14. A view change in the same document does not by itself invalidate the review.
15. Approval does not extend intent expiry.
16. An expired intent makes an existing approval unusable.
17. A successful consume is single-use.
18. Concurrent consume attempts allow at most one success.
19. A rejected intent cannot be reviewed again.
20. A consumed approval cannot be reviewed or consumed again.
21. Successful document close clears provider state before intent, parameter-ref, and document-identity state.
22. Cancelled and failed close preserve provider state and intent state.
23. Shutdown clears provider state and later operations fail closed.
24. Provider or UI initialization failure does not fault the read and preview core.
25. An explicit later UI retry is allowed after a cleaned-up initialization failure.
26. No operation lists or searches approvals or intents.
27. Decision state does not duplicate the full BIM preview payload.
28. Provider state retains no Revit API wrappers.
29. `BeginReview` while a different document is active does not start.
30. `BeginReview` with no resolvable active document does not start.
31. An intent that expires after `BeginReview` and before Approve or Reject receives no terminal decision.
32. A delayed Approve or Reject after an active-document change records no decision.
33. A delayed Approve or Reject after successful document-close invalidation records no decision.

## Explicitly excluded

This specification does not authorize:

- production approval-provider code;
- production WebView2 approval UI;
- CAP-0008;
- `revit_apply_parameter_updates`;
- Bridge protocol v9;
- a Server write tool;
- `Parameter.Set`;
- a Revit `Transaction`, `SubTransaction`, or `TransactionGroup`;
- save or sync;
- MRTR;
- MCP Apps;
- an MCP SDK upgrade;
- authentication architecture;
- an external approval provider;
- attestation or cryptographic implementation;
- production Revit writes.

## References

- ADR-0008: controlled write safety model
- ADR-0009: agent and human interaction and trusted approval boundary
- ADR-0010: Revit-local trusted approval authority and provider contract
- LIFECYCLE-0001: Revit add-in bootstrap and shutdown
- LIFECYCLE-0002: ephemeral write-intent store
- CAP-0007: `revit_preview_parameter_updates`
- Merged WebView2 spike, PR #60, `spikes/RevitMCP.WebView2Spike/README.md`. Feasibility evidence only.
