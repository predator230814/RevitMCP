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
| Host | Instance `a2d18e60-c8af-4772-927e-ae0c37229896`. The query completed on negotiated protocol 26. That is the protocol this execution used. Current binaries no longer advertise 26; see the protocol 27 section below. |
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

## Protocol 27 transport revalidation (2026-11-27)

This section does not replace the 2026-11-26 Snowdon gate above. That gate negotiated protocol 26. The revalidation checks only the transport split: current Server plus current Addin negotiate protocol 27, and the query is admitted only after that negotiation.

The open unmodified document was the electrical local `C:\REVIT USAGER\525413_E_EG_dave.otis.rvt`, not Snowdon. Reopening it assigned `document_id` `b6b115f2-9f04-4c7f-8e96-5e2884afc430`. The A305 24/24 comparison was not repeated. The source was `return 1;`.

The Release binaries were built on 2026-11-27 12:26 from this working tree, before the commit that records the revalidation.

| File | SHA-256 |
| --- | --- |
| `BimIt.Revit.Addin.dll` | `411abf581d22cbf918058f807c944c52d93efabe8afd73a3cac926415096efe1` |
| `BimIt.Revit.CompilerHost.exe` | `864ac7335dbd243905100692a69fab6bdf9c5612b17a3eb24dde0b3b339497a5` |
| `BimIt.Revit.Server.exe` | `6894d2555e7c1c91026e3d6bdcf5a3e166c7b98d0a7b7c8a440c0e41d3aaf2f7` |

| Check | Result |
| --- | --- |
| Registered Addin protocol | 27. Instance `621c6cf8-6cfa-49ac-bb9d-00d8b8beeb7d`. Revit 2026.5 build `26.5.0.55`. |
| Handshake before the query | **PASS.** The client offered `[27, 25, ..., 1]`. The host advertised the same set and did not advertise 26. Selected protocol was 27. The query had not been sent. |
| `tools/list` | 23 |
| `Document.IsModified` | false before the query and false after. Same `document_id`. pyRevit also read false after the query. The model was not saved. |
| Disabled before opt-in | **PASS.** `CSHARP_QUERY_DISABLED`. CompilerHost count 0. `capabilities.json` was absent. |
| Query | **PASS.** `policy_version` 2, `compile_outcome` `success`, result `1`, `RevitAPI` `26.5.0.0`. `source_sha256` `f58b7c3af621b52a2bb7dc67d4491f9ab6c6d16e3cfa1e46e670ff4f9a301fdc`. `cache_key` `7f6e86ad845d48c0bd6ee3504fb1f1453be63b012396efec27468687bbb6335e`. |
| Restore | **PASS.** The opt-in file was deleted. The next call returned `CSHARP_QUERY_DISABLED`. CompilerHost count 0. |

Status: **PASS**.
