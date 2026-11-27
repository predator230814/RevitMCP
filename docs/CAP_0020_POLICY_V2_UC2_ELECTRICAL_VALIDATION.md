# CAP-0020 policy version 2 electrical comparison

- Responsibility: the business dogfood on one electrical local. It compares an Assistant parameter correlation with `MEPSystem.Elements`. The policy version 2 technical gate is [CAP_0020_POLICY_V2_UC2_VALIDATION.md](CAP_0020_POLICY_V2_UC2_VALIDATION.md). Safety evidence for this document is included because the model and the result are different.
- Technical status of this run: **PASS**
- Capability: [CAP-0020](capabilities/CAP-0020-bounded-generated-csharp-read.md)
- Policy: version 2 for loaded `RevitAPI` 26.5.0.0
- Date: 2026-11-27
- No new capability is required.

This record keeps four observations apart.

1. Policy version 2 executed on this project. The population query and the later focused query both compiled and returned closed JSON.
2. In this model, distinct `ElectricalSystem` elements share `CircuitNumber` `UC2`. That describes this model only.
3. One panel's parameter correlation was compared with `MEPSystem.Elements`. The sets of element references matched.
4. Circuit Name and Circuit Number are different fields. This record does not say that either field is the normal or the abnormal way to number a circuit.

The executions in this file negotiated protocol 26. A later transport revalidation, on a reopen of this same local, is recorded in the protocol 27 section of [CAP_0020_POLICY_V2_UC2_VALIDATION.md](CAP_0020_POLICY_V2_UC2_VALIDATION.md). That revalidation does not repeat the panel comparison.

The 2026-11-25 records stay **PASS** under policy version 1. The 2026-11-26 Snowdon record stays the first policy version 2 gate. The population source hash and cache key match that Snowdon run because the source, the policy version, and the `RevitAPI` 26.5.0.0 identity are the same. `cache_key` identifies that compilation artifact. It does not identify the document, the model state, or the business result.

## Environment

| Item | Observed |
| --- | --- |
| Revit | 2026.5, build `26.5.0.55` |
| Loaded Revit API | `RevitAPI` `26.5.0.0`, returned by the query |
| Active document | `525413_E_EG_dave.otis`, `C:\REVIT USAGER\525413_E_EG_dave.otis.rvt`. Workshared local. `document_id` `c6f06172-a037-4df0-959c-50477a7adccc`. Active view `SYNCHRONISATION`. Selection count 0. |
| Host | Instance `b6b304b2-ded7-47d9-8b42-98dd699b1e43`. The completed calls used negotiated protocol 26. That fact belongs to these executions. Current binaries no longer advertise 26. |
| `tools/list` | 23 |
| Local opt-in | Absent before the call. Written as schema version 1 with `csharp_query_enabled` true only for the query, then deleted. |
| `Document.IsModified` | false before the Assistant question, false before the query, and false after the query. Same `document_id`. This run did not save the model. |
| CAD and links | No `ImportInstance`. Eleven Revit links were unloaded. The earlier truncated-number dialog did not appear on this open. |

## Technical gate

The method body is the exact source compiled by `Policy_v2_compiles_the_uc2_comparison_query`. It was called through Release `BimIt.Revit.Server.exe`, not through the Assistant.

| Check | Result |
| --- | --- |
| Opt-in absent | **PASS.** `CSHARP_QUERY_DISABLED`. No CompilerHost. |
| Enabled query | **PASS.** `compile_outcome` `success`. `policy_version` 2. `revit_api` `RevitAPI` `26.5.0.0`. `source_sha256` `237d796cc1a4b7dcd42e32fc285b0d8f3674b37d8612fd961513642137a5ec57`. `cache_key` `1b3339f7aa3f6246797e8be1b7d02579858ca58ead9e23be21728f6830c87bc1`. `duration_ms` 53. `result_characters` 788. |
| Shape | Closed JSON. `scanned_circuit_count` 3620. `scan_truncated` false. `matched_circuit_count` 12. `returned_circuit_count` 3. `truncated` true. Each returned circuit has `member_count` 69, `members_truncated` true, and an empty `member_refs` array, because 69 is above the enumerate bound of 64. No live Revit object. |
| Restore | **PASS.** File deleted. The same call returned `CSHARP_QUERY_DISABLED`. No CompilerHost remained. |

`scan_truncated` false means 12 is the count of `ElectricalSystem` elements whose `CircuitNumber` is `UC2` in this project. `truncated` true means nine of those circuits are absent from `circuits`.

The three returned panels, read afterwards with `revit_get_elements`:

| `panel_ref` | Name | Category | Family | Type |
| --- | --- | --- | --- | --- |
| `f9709d3e-04eb-4cd6-95d8-30db458572c9-003b0648` | `B211-PSUC-001` | Tableaux/Baies | `Tt_EQE_Panneau_Services_120_208V(P)` | `100A` |
| `f9709d3e-04eb-4cd6-95d8-30db458572c9-003b071f` | `D211-PSUC-001` | Tableaux/Baies | `Tt_EQE_Panneau_Services_120_208V(P)` | `100A` |
| `f9709d3e-04eb-4cd6-95d8-30db458572c9-003b0251` | `B111-PSUC-001` | Tableaux/Baies | `Tt_EQE_Panneau_Services_120_208V(P)` | `100A` |

## Assistant baseline

Question, with no tool named:

> Pour le circuit UC2, quel est le panneau et quels éléments lui sont associés? Ne modifie rien.

Answer:

> J’ai trouvé quatre circuits nommés UC2, chacun associé à un panneau différent :
>
> | Panneau | Nombre d’éléments indiqué |
> | --- | --- |
> | B211-PSUC-001 | 69 |
> | D211-PSUC-001 | 69 |
> | B111-PSUC-001 | 69 |
> | D311-PSUC-001 | 69 |
>
> Les lectures disponibles indiquent le nombre d’éléments, mais ne permettent pas d’en obtenir la liste ni les noms. La topologie électrique n’a pas fourni de connexions pour ces circuits.
>
> Aucune modification n’a été effectuée.

Tools used: `revit_get_context`, `revit_lookup`, `revit_query_elements`, `revit_query_parameter_bindings`, `revit_get_elements`, `revit_describe_parameters`, `revit_get_parameter_values`, `revit_get_mep_topology`.

`revit_run_csharp_query` was not used. The answer gives indicated counts. It does not claim a member list.

A later read on the focused circuit separates the fields. The parameter `Nom du circuit` is `ÉCLAIRAGE`. The parameter `Numero de circuit` is `UC2`. The circuit element's own name is `UC2`. The first Assistant answer used the shared number as though it were the circuit name, and it reported four panels. The population query counts every `ElectricalSystem` whose `CircuitNumber` is `UC2`. Those are different readings of this model. Neither reading is a numbering convention for the product.

## Population comparison

| Measure | Assistant / parameters | CAP-0020 / Revit API |
| --- | --- | --- |
| Circuit | 4 circuits named `UC2` | `matched_circuit_count` 12, `scan_truncated` false |
| Panel | `B211-PSUC-001`, `D211-PSUC-001`, `B111-PSUC-001`, `D311-PSUC-001` | The 3 returned panels are `B211-PSUC-001`, `D211-PSUC-001`, and `B111-PSUC-001`, category Tableaux/Baies, family `Tt_EQE_Panneau_Services_120_208V(P)`, type `100A` |
| Correlated count / `Elements.Size` | 69 indicated on each of the 4 | `member_count` 69 on each of the 3 returned circuits |
| Comparable members | No element list and no names | `member_refs` empty and `members_truncated` true, because 69 is above 64 |
| Intersection | The 3 returned panel names and the count 69 | Same 3 names and the same count |
| Assistant only | `D311-PSUC-001` is named. It is not among the 3 returned circuits. The result does not say whether it is one of the 9 circuits left out by the return cap. | |
| API only | | 9 `UC2` circuits beyond the Assistant's 4. Their panel names were not returned. |

## Limits

- The return cap is 3 circuits. Nine matched circuits have no panel name in this result.
- The member enumerate cap is 64. A circuit of 69 members returns the count and an empty `member_refs` array. Element-by-element comparison was not possible.
- The focused comparison of panel `A305-PSUC-001` is the next section. It is not this population scan.
- Serialized size for a full 32-member payload remains an estimate. This result was 788 characters.

## Focused comparison for one panel

The name `A305-PSUC-001` found 11 elements. `revit_get_elements` separated them. Ten are schedules or schedule graphics. One is the equipment used as the panel identity:

| Field | Observed |
| --- | --- |
| `element_ref` | `917d21bb-7869-40a1-8551-b690b707e249-0040d912` |
| Name | `A305-PSUC-001` |
| Category | Tableaux/Baies |
| Family | `Tt_EQE_Panneau_Services_120_208V(P)` |
| Type | `100A` |

The parameter correlation used two built-in instance parameters on category `Luminaires`, and it did not use Circuit Name:

| Role | Parameter | Built-in id | Observed value |
| --- | --- | --- | --- |
| Circuit Number | `Numero de circuit` | `rbsElecCircuitNumber` | `UC2` |
| Panel | `Panneau` | `rbsElecCircuitPanelParam` | `A305-PSUC-001` |

`revit_query_elements` returned `matched_count` 24 and `truncated` false. All 24 references were read back. Each one has `Numero de circuit` `UC2` and `Panneau` `A305-PSUC-001`.

| Category | Family | Type | Count |
| --- | --- | --- | --- |
| Luminaires | `Tt_LUM_Rond_Encastre(P)` | `L1 (GÉNÉRAL)` | 20 |
| Luminaires | `Tt_LUM_Rectangle_Encastre(P)` | `L3A-HALL ASCENSEUR` | 4 |

The focused policy version 2 body kept the scan bound of 7500. It admitted an `ElectricalSystem` only when `CircuitNumber` was `UC2`, `BaseEquipment` was not null, and `elementRef(BaseEquipment)` was the panel ref above. For a circuit whose `Elements.Size` was at most 64, it returned every `member_refs` entry and did not trim to 32. This source is a validation input. It is not a product fixture and it is not a Skill.

| Check | Result |
| --- | --- |
| Document | Same local. `document_id` `7d81e406-ae0b-47f9-8f6e-e12c81d5725a` after the reopen. `Document.IsModified` false before the query and false after it. pyRevit read the same flag as false after the call. The model was not saved. |
| Opt-in | Absent before the call. Schema version 1 with `csharp_query_enabled` true only for the query, then deleted. |
| Disabled calls | `CSHARP_QUERY_DISABLED` before the opt-in and after the file was deleted. No CompilerHost before, after the query, or after the restore. |
| Enabled query | `compile_outcome` `success`. `policy_version` 2. `revit_api` `RevitAPI` `26.5.0.0`. `source_sha256` `68ec2cde101769ac37a19fc669517a0ecff962b608c84a66f5087ae0407bf9b9`. `cache_key` `d0acc07be95e6c9fbf449857272ebbee027e0ba005f55b6917c4ffc2b70be3aa`. `duration_ms` 48. `result_characters` 1593. `tools/list` 23. |
| Scan | `scanned_circuit_count` 3620. `scan_truncated` false. `matched_circuit_count` 1. `returned_circuit_count` 1. `truncated` false. |
| Circuit | `circuit_ref` `dfb87f7b-1da6-4606-9ab2-11df74fa4739-004bb94e`. `panel_ref` equals the equipment ref above. `member_count` 24. `member_refs` count 24. `members_truncated` false. |

On that circuit element, the parameters are distinct from each other and from the element name:

| Field | Observed |
| --- | --- |
| Element name | `UC2` |
| Circuit Name, parameter `Nom du circuit` (`rbsElecCircuitName`) | `ÉCLAIRAGE` |
| Circuit Number, parameter `Numero de circuit` (`rbsElecCircuitNumber`) | `UC2` |
| Category | Circuit électrique |

The element name and the circuit number happen to be the same text here. The circuit name is a different value. The comparison uses the element sets, not a choice between those fields.

| Measure | Correlation / primitives | CAP-0020 / Revit API |
| --- | --- | --- |
| Panel | `A305-PSUC-001`, equipment ref `917d21bb-7869-40a1-8551-b690b707e249-0040d912` | `panel_ref` is that same ref |
| Circuit Name | `ÉCLAIRAGE`, read from `Nom du circuit` on the circuit element | The focused body did not project this field. The same parameter read is `ÉCLAIRAGE`. |
| Circuit Number | `UC2`, parameter `Numero de circuit` on the 24 luminaires | `circuit_number` `UC2`, and `Numero de circuit` on the circuit element is `UC2` |
| Element count | `matched_count` 24, `truncated` false | `Elements.Size` 24, `member_refs` 24, `members_truncated` false |
| Intersection | 24 references | 24 references |
| Correlation only | none | |
| API only | | none |

No reference was present on only one side, so no divergent category, family, or type was left to classify. The 24 shared references are the luminaires in the table above.

This source has a different `source_sha256` and `cache_key` from the population query because the source is different. The algorithm is unchanged. The matching keys across Snowdon and the population query remain the expected result for one source, one policy, and one Revit API identity.
