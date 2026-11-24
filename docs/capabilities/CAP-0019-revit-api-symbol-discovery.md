# CAP-0019: Revit API symbol discovery

- Status: Accepted on 2026-11-24. Implemented. Live Revit validation has not been run.
- Operation class: Read
- Date: 2026-11-24
- Architecture: Accepted [ADR-0027](../adr/ADR-0027-layered-revit-capability-admission.md). [ADR-0023](../adr/ADR-0023-declarative-revit-read-query-architecture.md) §1 stays in force. [ADR-0025](../adr/ADR-0025-bounded-revit-api-property-access.md) flexible C# stays Research Only.
- Bridge: no new RPC and no protocol bump. Reuses [CAP-0016](CAP-0016-revit-read-operations.md) `revit_lookup` / `revit_read`. Bridge protocol stays 25.
- MCP: no new tool. Production `tools/list` stays 22.

This operation describes the Revit API assembly already loaded by the connected Revit process. It does not execute generated code.

## Purpose

Give a model compact C#-oriented descriptions of public API symbols so it can check signatures instead of guessing them.

| Id | Version | Title |
|---|---:|---|
| `revit_api.symbols` | 1 | Revit API symbols |

`revit_lookup` finds the operation. `revit_read` runs it. The description is progressive:

1. Search returns type summaries.
2. Exact `type_name` returns one bounded type description.
3. Exact `type_name` plus `member` returns that member group and the type context, including the relevant overloads.

`revit_api.metadata` and `revit_api.element_properties` are unchanged. This operation does not read elements and does not project property values.

## Trusted surface

Reflection uses the Revit API assembly already loaded in the process (`typeof(Element).Assembly` through the existing resolver). v1 includes public non-generic types in that assembly, including types that are not `Element` subclasses.

v1 does not reflect:

- RevitAPIUI
- a caller-supplied assembly
- a plugin assembly
- a dynamically loaded assembly
- open generic types, because the trusted resolver rejects backtick names and v1 does not add a second naming rule

Types the runtime cannot load are not part of the population. That is a load failure, not a result cap.

The operation never:

- accepts C# source
- compiles source
- emits or loads IL
- invokes a Revit API method
- invokes a getter on a model element
- mutates Revit

Enum constant values are read from metadata literals. They are not obtained by executing code. There is no preferred-value list.

## Inputs

Closed object. `additionalProperties` is false.

| Field | Rule |
|---|---|
| `query` | string, 2..128. Search mode. |
| `type_name` | string, 1..256. Exact public full type name. |
| `member` | string, 1..128. Exact C# member name. Legal only with `type_name`. |

Exactly one of `query` or `type_name`. The JSON Schema subset cannot express that alternative, so the operation rejects any other combination with `INVALID_READ_OPERATION_INPUT` and no partial result.

Exact names use ordinal C# comparison. A simple name is not a full name, so it does not select a type. An unknown type or member uses the same error and returns nothing.

Search is ordinal, case-insensitive containment against public type names and, only when no type name matches, public declared member names. It does not use plural equivalence, a BIM alias table, or a benchmark table. Type-name hits have an empty `matched_members` list. Member hits include the matched member names.

Results are ordered by full type name, ordinal. Matched member names are ordered ordinal.

If the selected search population is larger than `MaxSearchMatches`, the operation fails closed. It does not return a truncated page of that population.

## Exact description

An exact type includes:

- full type name, simple name, and namespace
- kind: `class`, `abstract class`, `static class`, `struct`, `interface`, `enum`, or `delegate`
- base type when it is more specific than `Object`, `ValueType`, `Enum`, or a delegate base
- assembly name and assembly version
- obsolete marker and message, bounded
- public constructors
- public properties
- public fields, including enum constants
- public method groups and overload signatures
- static factory methods discovered from the type itself

Declared public members only. Inherited members stay on the base type, which is named when present. Implemented interfaces are not listed in v1.

A factory is a public static declared method whose name starts with `Create` or `New` (ordinal), whose return type is not `void`, and whose return type is in the same assembly. Factories are listed in `factories` and are not repeated in `methods`. There is no handwritten creation map.

`member` equal to the simple type name selects public constructors. Any other exact name selects one of methods, properties, or fields. If one name matches more than one of those kinds, the operation rejects it as ambiguous. The member path returns that group plus type context and leaves the other detail arrays empty. Overloads of that member are preserved up to `MaxMemberOverloads`.

Signatures are compact C#. They keep static or instance, return type, generic type and method arguments, parameter types and names, and `ref`, `out`, or `in`. They do not include assembly paths, IL, metadata tokens, reflection handles, or stack traces. Default parameter values and generic constraints are omitted in v1. Primitive types use C# keywords.

## Bounds

These constants are part of the contract. Counts and flags are always present. A list is never shortened without its omitted count. A signature or obsolete message is never shortened without its truncated flag. When an omitted count is greater than zero, the returned list length is the maximum.

| Constant | Value | Behavior |
|---|---:|---|
| `MaxSearchMatches` | 40 | Fail closed. No partial type list. |
| `MaxMatchedMembers` | 8 | Explicit `matched_members_omitted_count`. |
| `MaxConstructors` | 8 | Explicit `constructors_omitted_count`. |
| `MaxProperties` | 24 | Explicit `properties_omitted_count`. |
| `MaxFields` | 24 | Explicit `fields_omitted_count`. Enum constants use the same ordinal cap. |
| `MaxMethodGroups` | 12 | Explicit `methods_omitted_count`. Groups are ordered by name, then instance before static. |
| `MaxOverloadsPerGroup` | 4 | Explicit `signatures_omitted_count` on the group. |
| `MaxMemberOverloads` | 12 | Member path. Explicit `signatures_omitted_count`. |
| `MaxFactories` | 8 | Explicit `factories_omitted_count`. |
| `MaxObsoleteMessage` | 160 | Explicit `obsolete_message_truncated`. |
| `MaxSignatureLength` | 400 | Explicit `text_truncated`. |

Search rows and exact rows use the CAP-0016 cursor envelope. `page_size` remains 1..200 and defaults to 100. The serialized envelope remains inside the 120,000 character result budget. `summary.matched_type_count` is the selected population. `returned_count` is the page. A page shortened by the cursor or the budget is not a silent drop: the cursor says whether more rows remain, and a single row that cannot fit fails as too large.

## Errors

| Condition | Code |
|---|---|
| Missing or combined `query` and `type_name`, `member` without `type_name`, or a length violation | `INVALID_READ_OPERATION_INPUT` |
| Unknown type, unknown member, or ambiguous member | `INVALID_READ_OPERATION_INPUT` |
| Search population above `MaxSearchMatches` | `INVALID_READ_OPERATION_INPUT` |
| Symbol metadata cannot be read | `REVIT_EXECUTION_FAILED` |
| Result shape or budget failure | existing CAP-0016 result codes, including `RESULT_TOO_LARGE` |

Unknown-name errors do not include a partial description.

## Out of scope

- executing C#, IL, or a Revit API member
- changing `revit_api.metadata` or `revit_api.element_properties`
- a new MCP tool, Bridge RPC, or protocol version
- handwritten per-type behavior
