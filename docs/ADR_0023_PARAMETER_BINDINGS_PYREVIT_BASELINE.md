# ADR-0023 parameter bindings: independent pyRevit baseline

- Date: 2026-11-05
- Purpose: evidence for the proposed second ADR-0023 source, [CAP-0015](capabilities/CAP-0015-revit-query-parameter-bindings.md)
- Status: frozen reference baseline. Research only. No BIM-IT code was changed.

This record keeps aggregate and representative evidence only. The raw 144-row inventory of the reference project is kept outside the repository and is not committed. Its integrity is identified by the SHA-256 below.

## Why a baseline was needed

On the same day, an isolated natural-agent run asked the 19-tool BIM-IT surface for a complete inventory of project parameter definitions and category bindings. The client could reach only `BimIt.Revit.Server`, had no built-in tools, and received no tool or schema coaching.

- The agent called `revit_get_context` once, successfully, and stopped.
- It concluded from the tool descriptions that no tool reads project-level parameter definitions or bindings. `revit_describe_parameters` needs at most 10 known element references and reports neither binding kind nor bound categories.
- It generated no code and changed nothing.
- Two earlier attempts of that run did not reach a conclusion for setup reasons: a client permission rule used the wrong tool-name pattern, and then every call returned `REVIT_EXECUTION_TIMEOUT`. The third attempt is the one reported above. The timeout cause was not identified.

The gap was therefore stated from schemas, not from failing calls. The baseline below establishes the facts independently, so that a future BIM-IT capability can be checked against them.

## Conditions

| Item | Value |
|---|---|
| Revit | 2026.5, build `26.5.0.55` |
| Active host document | `525413_E_EG_DAVE.OTIS`, project, workshared, already modified before the run |
| Linked documents | 11 loaded, excluded |
| Tool | pyRevit MCP, read-only `run_query`. No BIM-IT, Nonica, Autodesk Revit MCP, or BIM-IT Assistant call was used for model facts |
| Session routing | three Revit processes were running (two 2026, one 2024). A `get_context` call without a target returned `ambiguous_revit`. Every later call named the process explicitly |

## API path that produced the inventory

Determined from the running Revit 2026 API (`lookup_revit_api`), not assumed:

1. `doc.ParameterBindings.ForwardIterator()`, `Reset()`, then `MoveNext()` until it returns false.
2. `iterator.Key` is the `Definition`. Every key was an `InternalDefinition`.
3. `iterator.Current` is the `Binding`: `InstanceBinding` or `TypeBinding`.
4. `doc.GetElement(definition.Id)` is either a `SharedParameterElement`, with its GUID in `GuidValue`, or a non-shared `ParameterElement`.
5. `Definition.GetDataType()` gives the data type `ForgeTypeId`.
6. `ElementBinding.Categories` gives the bound categories, each with `Category.BuiltInCategory` and the localized `Category.Name`.
7. `ElementId.Value` (Int64) is used. `IntegerValue` was not used.

`InternalDefinition.BuiltInParameter` was `INVALID` for every entry, so no binding-map entry was a built-in parameter.

## Results

| Fact | Value |
|---|---|
| Binding-map entries (iterator count) | 144 |
| `ParameterBindings.Size` | 144 |
| Unique definitions | 144 |
| Shared | 73, each with a GUID, no GUID repeated |
| Non-shared project parameters | 71 |
| Instance-bound | 138 |
| Type-bound | 6 (5 shared, 1 non-shared) |
| Distinct bound categories | 109 |
| Definition/category associations | 354 |
| Definitions with zero categories | 0 |
| Duplicate display names (exact or case-insensitive) | 0 |
| Names with leading or trailing whitespace | 0 |
| Unclassifiable definitions or bindings | 0 |
| Script errors on individual entries | 0 |
| Definitions with `Visible == false` | 2 |
| Names containing non-ASCII characters | 23 |

Data types: 132 text, 5 yes/no, 2 multiline text, and one each of electrical potential, electrical current, wattage, number of poles, and length.

Bound categories: the largest binding sets have 107 and 30 categories. The median definition is bound to 1 category, the 90th percentile to 2. Every association mapped to a defined `OST_` `BuiltInCategory` member. Category types observed in associations: Model 328, Internal 12, AnalyticalModel 12, Annotation 2. Category names were returned in the Revit UI language (English) although many definition names are French.

The document also held 1,713 `ParameterElement`s, of which 1,150 were `SharedParameterElement`s. Of these, 1,077 shared and 492 non-shared parameter elements are **not** in the binding map. Two unbound shared elements share the display name `Door Level` with different GUIDs. A source defined as "all parameter elements" would therefore give a very different answer from "all project bindings".

### Representative rows

GUIDs are shortened here.

| Name | Identity | Data type | Binding | Categories |
|---|---|---|---|---|
| `Tt_Tension` | shared, `b6a72840…` | `autodesk.spec.aec.electrical:potential-2.0.0` | instance | Electrical Fixtures |
| `Tt_Capacité_Sectionneur` | shared, `857e1417…` | `autodesk.spec.aec.electrical:current-2.0.0` | instance | Electrical Fixtures |
| `ALIMENTATION 120V` | shared, `6c0ec84f…` | `autodesk.spec:spec.string-2.0.0` | instance | Security Devices |
| `BOUTON DE FERMETURE` | non-shared | `autodesk.spec:spec.string-2.0.0` | instance | Security Devices |
| `CHARGE INTÉGRÉE` | non-shared | `autodesk.spec:spec.string-2.0.0` | type | Electrical Equipment |

### Discrepancy check: GUIDs of the two electrical rows

The first verbal summary of this run showed `Tt_Tension` and `Tt_Capacité_Sectionneur` as shared, with `—` in the GUID column. The same summary stated that all 73 shared definitions have a GUID.

The frozen data was re-read to resolve this. Both rows are `SharedParameterElement`s with a 36-character lowercase GUID (`b6a72840…` and `857e1417…`). Across the file, 73 of 73 shared rows have a well-formed GUID and no non-shared row has one. The data does not contradict the invariant. The `—` was an omission in the summary table, not missing data.

## Completeness evidence

- The whole binding map was traversed with its iterator. The iterator count, 144, equals `ParameterBindings.Size`.
- 144 distinct definition element ids and 144 distinct benchmark identities (shared GUID, else definition element id). The benchmark identity is for this check only and is not proposed as a BIM-IT contract.
- 73 + 71 = 144 and 138 + 6 = 144.
- The frozen file was re-parsed independently of the script: 144 rows, 109 distinct categories, 354 associations, and each row's category count equal to the length of its category list.

## Operational evidence

| Item | Value |
|---|---|
| pyRevit MCP calls | 12: 2 `get_context`, 1 `get_skill`, 6 `lookup_revit_api`, 3 `run_query` |
| `run_query` attempts | 3 |
| Attempt 1 | failed under IronPython 2.7: the .NET `Int64` returned by `ElementId.Value` is not JSON-serializable. Fixed with `int(...)` |
| Attempt 2 | failed under IronPython 2.7: serializing the result failed on an accented name (byte `0xE9`, `é`) after the script finished |
| Attempt 3 | succeeded under CPython 3.12 (pythonnet) |
| Generated Python | about 7 KiB per script, with result keys invented by the script |
| Side effect | the successful script wrote the JSON inventory file itself, outside the repository |
| Elapsed (attempt 3) | about 1.7 s of script execution, 8.5 s for the whole call |
| Unicode | all 23 non-ASCII names intact under CPython and in the UTF-8 file |
| Model mutation | none. `run_query` rolls back and reports a modified model as an error, and none was reported. No transaction was opened |

These are the same classes of cost that ADR-0023 recorded for the family-type benchmark: runtime-specific serialization failures, Unicode failures under IronPython, and a result shape that exists only in the generated script.

## Frozen artifact

- The raw inventory and a manifest are kept outside the repository.
- SHA-256 of the raw inventory file as written on disk: `c3d1c6d63dc2a200c60d4ca9ee2d2146125f0e7f5d798b646403d2e27ef8b306`.
- The script printed a different hash, `1d8b03b0…`, computed before Windows line-ending translation. The on-disk hash is the reference.

## Use

This baseline is the comparison target for the CAP-0015 natural-agent gate. Its numbers describe one document state, not product constants. If the reference model changes, the baseline is re-measured first by a method independent of BIM-IT.
