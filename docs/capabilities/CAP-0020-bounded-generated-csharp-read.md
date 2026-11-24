# CAP-0020: Bounded generated-C# read

- Status: Accepted on 2026-11-24. Not implemented. Production `tools/list` stays 22. Bridge protocol stays 25.
- Operation class: Read
- Date: 2026-11-24
- Architecture: Accepted [ADR-0028](../adr/ADR-0028-bounded-generated-csharp-read-executor.md). This is the only exception to [ADR-0023](../adr/ADR-0023-declarative-revit-read-query-architecture.md) §1. [ADR-0027](../adr/ADR-0027-layered-revit-capability-admission.md) remains the admission order.
- Bridge: planned `revit.run_csharp_query` on protocol 26 only. Not implemented. A current host does not advertise 26.
- MCP: planned tool `revit_run_csharp_query`. After implementation, production `tools/list` is 23.

## Purpose

Run one bounded C# method body against the active project document and return a closed JSON value. The body is for a question that ordinary primitives and Skills do not express. It is not a substitute for those primitives.

## Trusted surface

The model supplies source text only. The Addin is the execution authority. The Server cannot compile, cannot supply IL, and cannot skip the compiler policy. Roslyn runs only in the trusted local CompilerHost process. The Addin loads compilation bytes only from the CompilerHost process it started.

The CompilerHost receives the loaded `RevitAPI` path and identity from the Addin. The path is not returned to the client.

## MCP tool

`revit_run_csharp_query`

Do not register this behavior on `revit_read` or `revit_lookup`. Clients must be able to allow-list it separately from CAP-0016.

Arguments:

| Name | Required | Rule |
| --- | --- | --- |
| `document_id` | yes | Exact active project document. ADR-0006 opaque id. |
| `source` | yes | C# method body. 1 to 8,000 characters. No class, no project, no using directives. |
| `instance_id` | no | Same routing rule as the other Revit tools. Omitted only when one eligible instance exists. |

No `references`, `usings`, `options`, `entry_point`, `assembly`, or `il` field exists. Unknown fields are rejected.

The host-visible names inside `source` are `doc` and `elementRef`. `elementRef(element)` returns the ADR-0006 `element_ref` for that element, or a deterministic host string when the element is null. It does not return `UniqueId`. The spike gate's `UniqueId` delegate is not this contract.

Fixed host usings, not supplied by the caller: `System`, `System.Collections.Generic`, `System.Linq`, `Autodesk.Revit.DB`.

## Output

One structured result:

| Field | Meaning |
| --- | --- |
| `result` | The closed JSON value. |
| `source_sha256` | SHA-256 of the exact UTF-8 source. The source itself is not echoed. |
| `cache_key` | SHA-256 of the contract name `revit.run_csharp_query`, policy version, loaded Revit API identity (name, version, public key token, file length), and exact source. |
| `policy_version` | `1` |
| `revit_api` | Assembly name and version only. |
| `compile_outcome` | `success` |
| `duration_ms` | Addin execution duration after the document gate. |
| `result_characters` | Serialized size of `result`. |

Failure responses use the error model below and omit `result`. They may include `source_sha256`, `policy_version`, `revit_api`, and bounded diagnostics. They never include the source, a stack trace, a filesystem path, or raw exception text.

## Transport-neutral contract

Protocol 26 adds `revit.run_csharp_query`. Support is `{26}` only. Protocol 26 preserves every protocol-25 capability by explicit membership. A full host advertises `[26..1]` only when this service is composed with the complete v25 prefix and the local enablement check is implemented. A complete v25 host still advertises `[25..1]` and does not advertise 26. A request on a connection below 26 is `BRIDGE_PROTOCOL_INCOMPATIBLE` before compile.

The Bridge request carries `document_id` and `source`. It does not carry IL or a Revit API path.

## Revit execution

1. If the local opt-in is missing or false, return `CSHARP_QUERY_DISABLED`. Do not start the CompilerHost. Do not enqueue.
2. Reject an invalid envelope before compile.
3. The Addin starts the trusted CompilerHost, passes the loaded Revit API identity, and applies policy version 1. Policy failure returns `CSHARP_QUERY_POLICY_REJECTED` and does not emit. Compiler errors return `CSHARP_QUERY_COMPILE_FAILED` and do not emit.
4. Enqueue one EXEC-0001 work item. The Addin does not overlap a second generated execution. It does not create a second dispatcher.
5. Inside that work item, require the exact active project document, reject a family document, and require `Document.IsModifiable == false`. Open no transaction.
6. Load the compilation in a collectible `AssemblyLoadContext`. Resolve `RevitAPI` from the default context. Invoke the host entry with `doc` and `elementRef`.
7. Copy the allowed result graph into host objects. Unload the query context. Do not unload `RevitAPI`.

The loop budget is 10,000 charges. Each instrumented loop-body entry charges one. A blocking Revit API call is not interrupted.

## Result graph

Allowed: null, boolean, bounded string, numeric primitives (`byte`, `sbyte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `float`, `double`, `decimal`, `char`), arrays or lists, string-keyed dictionaries, and compiler-generated anonymous types composed only of allowed values.

Depth limit 8. Serialized `result` limit 16,000 characters. Anything else is `CSHARP_QUERY_INVALID_RESULT` or `CSHARP_QUERY_RESULT_TOO_LARGE`. There is no partial result.

The copier calls property getters only on compiler-generated anonymous types in the generated assembly. It does not walk an arbitrary object and does not call getters on a Revit object, an `ElementId`, or an enum. Return those through an allowed projection or through `elementRef`.

## Policy version 1

Reject before emit. Details and the Revit API names inspected on `RevitAPI` 26.5.0.0 are in ADR-0028. The closed categories are transactions, Revit UI, application services, filesystem, network, process creation, reflection, dynamic assembly load, Roslyn, P/Invoke, unsafe code, threads, preprocessor directives, the listed C# escape statements, event subscription, and the effect-bearing Revit API save, sync, export, print, document-lifecycle, external-resource, worksharing, and file-affecting members named in that ADR.

## Local enablement

Default off. Persistent Addin setting for the Windows user under `%LOCALAPPDATA%\RevitMCP`. Not an MCP field. Not the write-approval pipeline. Enabling this tool grants no write authority. A missing or unreadable setting is disabled.

## Deterministic errors

| Code | When |
| --- | --- |
| `CSHARP_QUERY_DISABLED` | Local opt-in is off or unreadable. |
| `INVALID_CSHARP_QUERY` | Source missing, empty, over 8,000 characters, or the envelope has an unknown or forbidden field. |
| `CSHARP_QUERY_POLICY_REJECTED` | Policy version 1 rejected the body before emit. |
| `CSHARP_QUERY_COMPILE_FAILED` | Roslyn reported errors. |
| `CSHARP_QUERY_DOCUMENT_NOT_READY` | No active document, family document, `IsModifiable` is true, or `document_id` does not match. `reason` is `no_active_document`, `project_document_required`, `document_modifiable`, or `document_mismatch`. |
| `CSHARP_QUERY_BUDGET_EXCEEDED` | The cooperative loop budget was exhausted. |
| `CSHARP_QUERY_EXECUTION_FAILED` | The method threw for another reason. No exception text. |
| `CSHARP_QUERY_INVALID_RESULT` | The returned graph is outside the closed set. |
| `CSHARP_QUERY_RESULT_TOO_LARGE` | The serialized result exceeds 16,000 characters or depth 8. |
| `BRIDGE_PROTOCOL_INCOMPATIBLE` | The connection does not advertise protocol 26. |

Policy and compile failures include at most 8 diagnostics. Each diagnostic has `code`, `line`, `column`, and `message`. Compiler `code` is the C# diagnostic id. Policy `code` is `CSHARP_QUERY_POLICY_REJECTED`. `message` is flattened to 180 characters. The spike ids `BIMQ100`, `BIMQ101`, `BIMQ110`, `BIMQ120`, `BIMQ130`, and `BIMQ140` are research evidence only. Production clients match the codes in this table.

`CSHARP_QUERY_DOCUMENT_NOT_READY` is returned by the Addin and does not include a Revit exception. `document_modifiable` means the host refused to run because the document was open for modification. It does not mean the query changed the document.

## Security

ADR-0028 is the threat model. This capability is not an OS sandbox. No transaction prevents ordinary transactional edits. It does not prove every `RevitAPI` member is free of external effects. Policy version 1 is the deny list for the effects inspected on `RevitAPI` 26.5.0.0. A newly discovered external effect is a policy-version bump, not a new wrapper tool.

## Performance

One compile and one execution per call in v1. A compiler daemon and a persistent cache are deferred. The cache key is still returned so a later cache can use it. The result budget is 16,000 characters, tighter than the 120,000-character compiled-read budget, because the result shape is model-defined and the spike validated 16,000.

## Observability

Log `source_sha256`, `cache_key`, `policy_version`, Revit API name and version, `compile_outcome`, `duration_ms`, `result_characters`, and the error code. Do not log `source` or `result` by default. Do not log the Revit API path.

## Composition with CAP-0019

`revit_api.symbols` is optional. Call it when a signature is uncertain. Then call this tool with a focused body. Do not call it as a mandatory preface. Recurring meaning is promoted under ADR-0027 to a Skill or a compiled capability. It is not left forever as generated source.

## Acceptance

This document is the contract. Implementation is not done. Live validation of `revit_run_csharp_query` has not been run. The accepted feasibility evidence is PR #133.

An implementation is acceptable when:

- the tool exists, `tools/list` is 23, and protocol 26 is advertised only by a host that implements this contract
- Roslyn is absent from the Addin, Server, Bridge, and Contracts projects
- a disabled setting returns `CSHARP_QUERY_DISABLED` without starting the compiler
- a transaction body is policy-rejected and is not enqueued
- a read body runs only through EXEC-0001 against the exact project document with `IsModifiable == false`
- `elementRef` returns an ADR-0006 `element_ref`
- the result is the closed graph, the query context can be collected, and `RevitAPI` stays loaded
- a tight loop returns `CSHARP_QUERY_BUDGET_EXCEEDED`, and a later read still succeeds
- no MCP response contains the Revit API path, the source echo, a stack trace, or raw exception text

## Deferred

Generated writes. Per-call approval. Arbitrary references or IL. Python and Dynamo. A remote compiler. A persistent compiler process. A persistent compilation cache. The ribbon control that edits the local opt-in. Any claim that policy version 1 names every externally effectful Revit API member.
