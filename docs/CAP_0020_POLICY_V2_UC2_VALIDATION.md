# CAP-0020 policy version 2 technical gate

- Responsibility: the policy version 2 live technical gate. Snowdon Towers Sample Electrical is the host because it could stay unmodified.
- Technical status: **PASS**
- Business comparison: agreement on absence of circuit number `UC2` in this sample. This is not the electrical-local correlation. That comparison is [CAP_0020_POLICY_V2_UC2_ELECTRICAL_VALIDATION.md](CAP_0020_POLICY_V2_UC2_ELECTRICAL_VALIDATION.md).
- Capability: [CAP-0020](capabilities/CAP-0020-bounded-generated-csharp-read.md)
- Policy: version 2 for loaded `RevitAPI` 26.5.0.0
- Date: 2026-11-26

The 2026-11-25 Bridge and MCP records stay **PASS** under policy version 1, with their recorded cache keys. This record does not change them.

The tested binaries were built on 2026-11-26 13:30 from the uncommitted working tree whose base is `f452a3e6d6758ff83df23722906af3215122dd4a`. They are not a committed SHA.

| File | SHA-256 |
| --- | --- |
| `BimIt.Revit.Addin.dll` | `50d820a30c521ba2e4dfe7a3469f4668ac1e7bf6e0dfafec7a841b1981052eed` |
| `BimIt.Revit.CompilerHost.exe` | `5d6e1c7b9ef759171bb823da254c4d930fd2170ded564b22e0aa7de3d237c047` |
| `BimIt.Revit.Server.exe` | `3af4f96b917d7a2c09209f2c22a9c2fd606d707d942663b2d1cfece0dbb6d425` |

## Environment

| Item | Observed |
| --- | --- |
| Revit | 2026.5, build `26.5.0.55` |
| Loaded Revit API | `RevitAPI` `26.5.0.0`, returned by the query |
| Active document | `Snowdon Towers Sample Electrical`, `C:\Program Files\Autodesk\Revit 2026\Samples\Snowdon Towers Sample Electrical.rvt`. `document_id` `aa41d140-9109-4f39-98c6-132d94a52995`. Active view `COVER SHEET`. Selection count 0. |
| Host | Instance `a2d18e60-c8af-4772-927e-ae0c37229896`. The query completed, and the server admits `revit_run_csharp_query` only when the negotiated bridge is protocol 26. |
| `tools/list` | 23. `revit_run_csharp_query` was present. No Bridge RPC name appeared. |
| Local opt-in | `%LOCALAPPDATA%\RevitMCP\capabilities.json` was absent before the call. It was written as schema version 1 with `csharp_query_enabled` true only for the query, then deleted. |
| `Document.IsModified` | false before the Assistant question, false before the query, and false after the query. Same `document_id`. The model was not saved. |

The Montmagny local cannot host this proof. Opening it reloads `PLAFOND CHAMBRE TYPE.dwg` and Revit shows `String_Revit_FilterDataOutOfRange`, which leaves `Document.IsModified` true. Snowdon was opened instead. The only user action before the dogfood was opening the Assistant.

## Technical gate

The method body is the exact source compiled by `Policy_v2_compiles_the_uc2_comparison_query`. It was called through a separate Release `BimIt.Revit.Server.exe` session, not through the Assistant.

| Check | Result |
| --- | --- |
| Opt-in absent | **PASS.** `CSHARP_QUERY_DISABLED`. No CompilerHost before or after. |
| Enabled query | **PASS.** `compile_outcome` `success`. `policy_version` 2. `revit_api` `RevitAPI` `26.5.0.0`. `source_sha256` `237d796cc1a4b7dcd42e32fc285b0d8f3674b37d8612fd961513642137a5ec57`. `cache_key` `1b3339f7aa3f6246797e8be1b7d02579858ca58ead9e23be21728f6830c87bc1`. `duration_ms` 35. `result_characters` 167. |
| Shape | Closed JSON. `target_circuit_number` `UC2`. `scanned_circuit_count` 448. `scan_truncated` false. `matched_circuit_count` 0. `returned_circuit_count` 0. `truncated` false. `circuits` empty. No live Revit object. |
| Restore | **PASS.** File deleted. The same call returned `CSHARP_QUERY_DISABLED`. No `BimIt.Revit.CompilerHost` process remained. |

`scan_truncated` false means the collector was exhausted. `matched_circuit_count` 0 is the count of `UC2` in this project. The scan visited 448 systems, under the 7500 bound, and returned no circuits, so the member loops did not run.

## Assistant baseline

Question, with no tool named:

> Pour le circuit UC2, quel est le panneau et quels éléments lui sont associés? Ne modifie rien.

Answer:

> Je n’ai trouvé aucune correspondance pour le circuit UC2 dans le modèle : aucun élément ne porte le numéro de circuit UC2, et la combinaison panneau contenant UC + circuit 2 ne renvoie rien. Je ne peux donc pas identifier son panneau ni les éléments associés à partir des données consultables.
>
> Aucune modification n’a été effectuée.

Tools used: `revit_get_context`, `revit_lookup`, `revit_query_elements`, `revit_query_parameter_bindings`, `revit_query_family_types`, `revit_get_elements`, `revit_describe_parameters`, `revit_get_parameter_values`.

`revit_run_csharp_query` was not used. The answer states that the relation was not observed. It does not claim a panel or a membership. The `UC` plus `2` attempt is a parameter search, not a structured circuit.

## Comparison

| Measure | Assistant / parameters | CAP-0020 / Revit API |
| --- | --- | --- |
| Circuit | No element carries circuit number `UC2` | `matched_circuit_count` 0, `scan_truncated` false |
| Panel | Not identified | No `panel_ref` |
| Correlated count / `Elements.Size` | No correlated elements | No members. `Elements.Size` was not read because no circuit matched |
| Comparable members | None | `member_refs` empty |
| Intersection | Empty | Empty |
| Assistant only | The extra `UC` + `2` parameter search, which also returned nothing | — |
| API only | — | The 448 scanned `ElectricalSystem` elements are a scan count, not a membership |

No primitive enrichment was run. There was no `panel_ref` and no `member_refs`.

## Limits

- This model is Snowdon Towers Sample Electrical. It does not contain the Montmagny circuit whose parameter correlation named panel `A305-PSUC-001` and 24 luminaires. That comparison was not rerun.
- The population result is conclusive for Snowdon only. `scan_truncated` false is the required condition, and it held.
- Serialized size for a non-empty 32-member result remains an estimate. This result was 167 characters. The runtime cap stays 16 000 characters.
- Policy version 2 is still a deny list, not a sandbox.
