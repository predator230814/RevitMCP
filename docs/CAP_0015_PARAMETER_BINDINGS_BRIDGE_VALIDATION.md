# CAP-0015 parameter-binding inventory: typed Bridge live validation

- Status: PASS
- Capability: [CAP-0015](capabilities/CAP-0015-revit-query-parameter-bindings.md)
- Bridge: [BRIDGE-0019](bridge/BRIDGE-0019-query-parameter-bindings-capability-rpc.md)
- Architecture: [ADR-0023](adr/ADR-0023-declarative-revit-read-query-architecture.md)
- Independent baseline: [ADR_0023_PARAMETER_BINDINGS_PYREVIT_BASELINE.md](ADR_0023_PARAMETER_BINDINGS_PYREVIT_BASELINE.md)
- Date: 2026-11-06

This is a **typed Bridge live validation**: `NamedPipeBridgeClient` → Bridge protocol 23 → `revit.query_parameter_bindings` → Addin EXEC-0001 → `Document.ParameterBindings` → Revit 2026.5.

It is **not** the CAP-0015 MCP or natural-agent gate. The Server application service and the `revit_query_parameter_bindings` MCP tool do not exist yet, so production `tools/list` remains 19. The natural-agent gate in CAP-0015 has not been run.

Automated tests do not update the reference numbers below. Do not mark this document PASS from automated tests.

| Phase | Check | Result |
|---|---|---|
| A | Unfiltered inventory, every page, against the frozen baseline | PASS |
| B | Binding-first `parameter_ref` parity with CAP-0004, and CAP-0005 chaining | PASS |
| C | Describe-first parity in a fresh Revit process | PASS |
| D | Both hidden (`Visible == false`) definitions present | PASS |
| E | Manual binding edit makes an old cursor `STALE_CURSOR`; restart succeeds | PASS |
| F | A protocol-22 client is refused CAP-0015 and keeps CAP-0014 | PASS |

## Environment

| Item | Observed |
|---|---|
| Source SHA | `393b12f8a7d852f00ed16a173a5fea6efaec5cf7` (main after PR #108) |
| Build | `BimIt.Revit.Addin` Release, `RevitVersion=2026`, built from a clean worktree at that SHA with 0 warnings. No source file changed after the build. |
| Loaded modules | Verified in both Revit processes from the process module list. `BimIt.Revit.Addin.dll` SHA-256 `208c6845825eb7ccdb51bcaee18e7f294254dac8e3c2b4fb5426da3f007e16c4`; `BimIt.Revit.Bridge.dll` `74a1eeff05c09ff4…`; `BimIt.Revit.Contracts.dll` `1bbc40d8584b5a3b…`. |
| Revit | 2026.5, build `26.5.0.55`, executable product version `20260731_1210(x64)`. One Revit process at a time. |
| Add-in version | `1.0.0.0` (registration and handshake) |
| Reference document | `525413_E_EG_DAVE.OTIS`, the active project document |
| Disposable document (phase E only) | `525413_E_EG_detached`, a detached copy of the reference project |

| Revit process | Instance | `document_id` | Registration protocol | Handshake |
|---|---|---|---|---|
| 1 (phases A, B, D, F) | `a5efd11f-3b73-408a-81cb-b11bbdf15be9` | `8a780043-52a7-4de8-8321-eea97003567d` | 23 | offered `[23..1]`, selected 23, host `[23..1]` |
| 2 (phase C) | `90f6d21f-24b3-484a-9f01-f32809ea501c` | `36447105-1204-44c6-bdf3-7db76892fb15` | 23 | offered `[23..1]`, selected 23, host `[23..1]` |
| 2 (phase E, copy) | `90f6d21f-24b3-484a-9f01-f32809ea501c` | `abaa0d3c-21a9-4461-8f6b-516524ad8511` | 23 | offered `[23..1]`, selected 23, host `[23..1]` |

Instance ids and `document_id` values are process-lifetime ids, so they changed with the restart.

## Validation client

- A temporary console harness, kept outside the repository and not committed, referenced `BimIt.Revit.Bridge` from the tested source.
- It used `FileRegistrationStore`, `LocalInstanceDiscovery`, `WindowsProcessInspector` and `NamedPipeBridgeClient`, with the typed request and result contracts. It did not implement its own JSON-RPC.
- It did not use `BimIt.Revit.Server`, MCP, the Assistant, Autodesk MCP, pyRevit, Nonica, Dynamo, or Revit API code for any product-path fact.
- Elapsed time is measured by the harness around each typed call. Character counts are the length of the result serialized with `ContractJson.Options`.
- pyRevit was used only as reference evidence, through the frozen artifact recorded in the baseline document. Its SHA-256 was verified as `c3d1c6d63dc2a200c60d4ca9ee2d2146125f0e7f5d798b646403d2e27ef8b306` before comparison. The BIM-IT output was saved before the comparison ran.

## Phase A: unfiltered inventory, PASS

`QueryParameterBindings` with the active `document_id`, no filters, `page_size` 50, following `next_cursor` until `complete` was true.

| Page | `returned_count` | `complete` | `next_cursor` | Elapsed (ms), run 1 | Elapsed (ms), run 2 | Serialized chars |
|---:|---:|---|---|---:|---:|---:|
| 1 | 50 | false | present | 109 | 26 | 37,748 |
| 2 | 50 | false | present | 6 | 5 | 25,986 |
| 3 | 44 | true | `null` | 5 | 5 | 35,446 |

Run 1 page 1 includes the first parameter-binding traversal in that process. Run 2 was a second complete traversal in the same process; its rows were byte-identical to run 1. The same traversal in the fresh process of phase C took 66 / 6 / 5 ms with the same character counts.

Summary, identical on all three pages:

| Field | BIM-IT | Frozen baseline |
|---|---:|---:|
| `matched_definition_count` | 144 | 144 |
| `shared_count` | 73 | 73 |
| `local_count` | 71 | 71 |
| `built_in_count` | 0 | 0 |
| `instance_bound_count` | 138 | 138 |
| `type_bound_count` | 6 | 6 |
| `distinct_category_count` | 109 | 109 |
| Sum of `categories.length` | 354 | 354 |

| Check | Result |
|---|---|
| Summary and context identical on every page | yes, 3 of 3 |
| Sum of `returned_count` / rows / distinct `parameter_ref` / repeated | 144 / 144 / 144 / 0 |
| Final page | `complete: true`, `next_cursor: null`; earlier pages `complete: false` with a cursor |
| Identity kinds | shared 73, local 71, built-in 0 |
| Binding kinds | instance 138, type 6 |
| Category associations / distinct tokens / rows with no category / unknown tokens | 354 / 109 / 0 / 0 |
| Rows with a GUID | 73, every shared row |
| Distinct names | 144 |
| Non-ASCII names | 23, intact. Examples: `CHARGE INTÉGRÉE`, `Tt_Capacité_Sectionneur`, `BOÎTIER D'ALIMENTATION`, `CÂBLAGE PROPOSÉ`, `DÉTECTEUR DE PRÉSENCE`. `ContractJson` escapes them as `É` on the wire; the decoded strings are exact. |
| Data types | `spec.string` 132, `spec.bool` 5, `multilineText` 2, and one each of `current`, `length`, `numberOfPoles`, `wattage`, `potential` |
| Raw `ElementId`, UniqueId-like value, file path, internal `id:` or `entry:` category key | none |
| `visible` field | absent |
| Row-by-row comparison with the frozen baseline (name, identity kind, GUID, Forge type id, binding kind, category token and name) | 144 matched, 0 mismatched, 0 missing, 0 extra |
| Failures | none |

The property names in every result were: `binding_kind`, `bindings`, `built_in_count`, `categories`, `category_name`, `category_token`, `complete`, `context`, `data_type`, `distinct_category_count`, `document_id`, `forge_type_id`, `guid`, `identity`, `instance_bound_count`, `instance_id`, `kind`, `local_count`, `matched_definition_count`, `name`, `next_cursor`, `parameter_ref`, `returned_count`, `shared_count`, `summary`, `type_bound_count`.

### Representative rows

`parameter_ref` values are from Revit process 1. They are opaque and process-scoped.

| Name | `parameter_ref` | Identity | Data type | Binding | Categories |
|---|---|---|---|---|---|
| `Tt_Tension` | `26dcbb07-fd10-4cb2-9f6f-0bd20ad60a08` | shared `b6a72840-960d-432b-9f8a-c4e8bb021c16` | `measurable_spec` `autodesk.spec.aec.electrical:potential-2.0.0` | instance | `OST_ElectricalFixtures` |
| `Tt_Capacité_Sectionneur` | `ac84c626-590f-450a-8099-d3e7465c0b5a` | shared `857e1417-5cec-423a-b077-34ce3b791d84` | `measurable_spec` `autodesk.spec.aec.electrical:current-2.0.0` | instance | Electrical Fixtures |
| `ALIMENTATION 120V` | `5d7f8ef4-5f77-46a2-9ed6-420d245e8ae2` | shared `6c0ec84f-7dd8-45a4-9b2e-7324183291a7` | `spec` string | instance | Security Devices |
| `BOUTON DE FERMETURE` | `fb555184-3dfd-4577-b841-ddebf4c209f0` | local | `spec` string | instance | Security Devices |
| `CHARGE INTÉGRÉE` | `0421fe2c-d86c-4403-89da-4915241d1b25` | local | `spec` string | type | `OST_ElectricalEquipment` (Electrical Equipment) |

All five match the frozen baseline.

## Phase B: binding-first parity and CAP-0005 chaining, PASS

In Revit process 1, after phase A: `QueryElements` on the category, then `DescribeParameters` on bounded elements with the source and `name_contains`, then `GetParameterValues`. No model change.

| Definition | Element | Describe source | `parameter_ref` | Identity | Data type | CAP-0005 |
|---|---|---|---|---|---|---|
| `Tt_Capacité_Sectionneur` (shared) | Electrical Fixtures `03a06b1c-…-004edfa0` | instance | `ac84c626…`, equal | shared `857e1417…`, equal | `current-2.0.0`, equal | `ok`, `has_value: false` |
| `CHARGE INTÉGRÉE` (local) | Electrical Equipment `052ed421-…-0049535e` | type | `0421fe2c…`, equal | local, equal | string, equal | `ok`, `has_value: false` |

`has_value: false` is a valid CAP-0005 result: the parameter exists on the element and has no value.

### Deviation: `Tt_Tension`

The task preferred `Tt_Tension` for shared parity. It could not be used:

- CAP-0015 and the frozen baseline both report `Tt_Tension` as an instance binding on Electrical Fixtures.
- `DescribeParameters` with source `instance` did not expose it on the first 25 Electrical Fixtures returned by `QueryElements` (2,319 matched).
- On fixture `03a06b1c-…-004edfa0`, CAP-0004 exposes `Tt_Tension` only with source `type`: ref `8544ec16-43c1-434a-8e2f-837deb94a8f2`, the same GUID `b6a72840…`, the same `potential` data type.
- CAP-0004 refs include the source, so this type ref differs from the CAP-0015 instance ref by design.

Hypothesis, not verified: the fixture family defines the same shared parameter as a type parameter, and that family definition is what the element exposes instead of the project instance binding. This is recorded for the Tech Lead. It is not judged a CAP-0015 defect: CAP-0015 reports the project binding map, and the baseline agrees with it.

Shared parity was therefore shown on another shared, instance-bound, Electrical Fixtures definition, `Tt_Capacité_Sectionneur`. CAP-0004's visible-parameter boundary was not changed or bypassed.

## Phase C: describe-first parity after a restart, PASS

Revit was closed without saving, restarted with the same build, and `525413_E_EG_DAVE.OTIS` was reopened (process 2). The loaded module hashes were verified again.

In that fresh process, CAP-0004 ran **before** any CAP-0015 call, then CAP-0015 ran in the same process:

| Definition | CAP-0004 first (`DescribeParameters`) | CAP-0015 afterwards | Identity | Data type | CAP-0005 |
|---|---|---|---|---|---|
| `Tt_Capacité_Sectionneur`, source instance | `2014732e-4bcc-439e-8d0a-0df2c306f08d` | `2014732e-4bcc-439e-8d0a-0df2c306f08d`, binding instance | shared `857e1417…`, equal | `current-2.0.0`, equal | `ok`, `has_value: false` |
| `CHARGE INTÉGRÉE`, source type | `1ac57695-59d5-4c1d-8a59-5c5c824fb6a2` | `1ac57695-59d5-4c1d-8a59-5c5c824fb6a2`, binding type | local, equal | string, equal | `ok`, `has_value: false` |

Refs were compared only within one process. Refs from process 1 and process 2 were not compared with each other.

The CAP-0015 traversal in process 2 again matched the frozen baseline row by row: 144 matched, 0 mismatched, 0 missing, 0 extra.

Together, phases B and C show both call orders in separate Revit process lifetimes.

## Phase D: hidden definitions, PASS

The frozen artifact, verified by SHA-256, contains exactly two definitions with `Visible == false`. Both are present as normal rows of the CAP-0015 traversal, with no `visible` field:

| Name | Identity | Binding | Data type | Category |
|---|---|---|---|---|
| `AreaCalculationInternationalBuildNumber` | shared `8ca3442a-9c02-424a-8231-6b07fa5d21ac` | instance | string | `OST_ProjectInformation` (Project Information) |
| `RoomBookInternationalBuildNumber` | shared `def84f6b-cf95-4eb0-826f-a2f9dcebd0a2` | instance | string | `OST_ProjectInformation` (Project Information) |

They were present in both Revit processes.

## Phase E: stale cursor, PASS

This ran only on the disposable detached copy `525413_E_EG_detached`, in Revit process 2, as the only active document. The reference project was not edited.

| Step | Expected | Observed |
|---|---|---|
| Page 1 with `page_size` 5; keep `next_cursor` | success | 5 rows, 39 ms, summary 144 / 73 / 71 / 0 / 138 / 6 / 109, cursor of 232 characters |
| Control before any edit: reuse that cursor | page 2 | page 2, 5 rows, 8 ms |
| Control before any edit: restart without a cursor | same cursor | new cursor byte-identical to the saved one |
| Dave edits a binding manually in Revit: Manage → Project Parameters, `BOUTON DE FERMETURE`, adds the Communication Devices category, OK | | committed |
| Reuse the saved cursor | `STALE_CURSOR` | `STALE_CURSOR`: "The parameter binding query cursor is stale. Restart the query without a cursor." |
| Restart without a cursor | success, different cursor | page 1, 5 rows, 50 ms; new cursor differs from the old one |

A full traversal of the edited copy then showed the edit and nothing else:

- 144 rows, 144 distinct refs, 0 repeated;
- category associations 355 instead of 354; distinct categories still 109 because Communication Devices was already bound elsewhere;
- compared with the frozen baseline, 143 rows matched and 1 differed: `BOUTON DE FERMETURE`, now bound to Communication Devices and Security Devices instead of Security Devices only.

The edit was made by hand in Revit. No BIM-IT, MCP or Revit API write was used. The copy was not saved and is disposable.

## Phase F: protocol-22 control, PASS

In Revit process 1, the harness offered only `BridgeProtocol.QueryFamilyTypesVersions`.

| Step | Expected | Observed |
|---|---|---|
| Handshake | selects 22 | offered `[22..1]`, selected 22, host `[23..1]` |
| `QueryFamilyTypesAsync` (`OST_ElectricalFixtures`, `page_size` 5) | success | 5 rows, 632 matched |
| `QueryParameterBindingsAsync` | `BRIDGE_PROTOCOL_INCOMPATIBLE` | `BRIDGE_PROTOCOL_INCOMPATIBLE`: "revit.query_parameter_bindings requires a negotiated bridge protocol version that explicitly supports it." No result returned. |
| `QueryFamilyTypesAsync` again | success | 5 rows |

## Failures

No Bridge, Addin, Revit API or runtime failure occurred on the product path.

Two harness mistakes produced errors and were corrected; neither is a product failure:

- one `DescribeParameters` request used `limit` 200, above the CAP-0004 maximum of 100, and was refused as an invalid request;
- one local comparison step read a file that had not been written yet.

## Safety observations

- No product code was changed.
- No Server, MCP tool, Assistant, pyRevit, Nonica, Dynamo or Revit API script was used for product facts.
- The reference project was not changed by the validation. It was closed without saving between process 1 and process 2.
- The only model change was Dave's manual binding edit on the disposable copy in phase E.
- No shared-parameter file was read and no linked document was traversed. Every row came from the active host document.
- The client cannot observe transactions directly. BRIDGE-0019 states that the read opens none. Two traversals in process 1 returned byte-identical rows, and the reference-project traversals matched the frozen baseline before and after the restart.
- The validation add-in manifest was pointed at the tested build for this run and restored afterwards.

## Checklist

- [x] Protocol 23 negotiated
- [x] Unfiltered traversal reaches an explicit final page
- [x] No Bridge, Addin or Revit runtime failure
- [x] Summary matches the frozen independent baseline for the current document state
- [x] Row count and unique `parameter_ref` count match the summary
- [x] Category association total matches the baseline
- [x] Unicode names intact
- [x] Shared CAP-0004 / CAP-0015 `parameter_ref` parity
- [x] Local parity
- [x] CAP-0005 chaining
- [x] Both call orders, in separate Revit process lifetimes
- [x] Both hidden definitions present
- [x] A real binding edit makes the old cursor `STALE_CURSOR`
- [x] Restart after the stale cursor succeeds
- [x] Protocol 22 refuses CAP-0015
- [x] Per-page elapsed time recorded
- [ ] Server application service and MCP tool: not implemented, out of scope
- [ ] CAP-0015 natural-agent gate: not run
