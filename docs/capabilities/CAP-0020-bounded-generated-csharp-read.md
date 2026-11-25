# CAP-0020: Bounded generated-C# read

- Status: Accepted on 2026-11-24. CompilerHost infrastructure is implemented, and the out-of-process compile-only gate passed on Revit 2026.5 build 26.5.0.55 against loaded `RevitAPI` 26.5.0.0. The production execution kernel is implemented. Its live execution gate passed in PR #136 on the same Revit build: host-owned JSON, unchanged document, budget failure, recovery, invalid result, recovery, and all six collectible contexts collected. Local enablement and Bridge protocol 26 `revit.run_csharp_query` are implemented. The MCP tool is not implemented. Production `tools/list` stays 22. Bridge current version is 26. CAP-0020 is not fully complete until the Server tool and the official MCP-client gate exist.
- Operation class: Read
- Date: 2026-11-24
- Architecture: Accepted [ADR-0028](../adr/ADR-0028-bounded-generated-csharp-read-executor.md). This is the only exception to [ADR-0023](../adr/ADR-0023-declarative-revit-read-query-architecture.md) §1. [ADR-0027](../adr/ADR-0027-layered-revit-capability-admission.md) remains the admission order.
- Bridge: `revit.run_csharp_query` on protocol 26 only. Implemented by [BRIDGE-0022](../bridge/BRIDGE-0022-bounded-csharp-query-rpc.md). A full host advertises 26 when the service is composed. Advertisement does not mean the local opt-in is on.
- MCP: planned tool `revit_run_csharp_query`. Not implemented. Production `tools/list` stays 22 until that tool exists, when it becomes 23.

## Purpose

Run one bounded C# method body against the active project document and return a closed JSON value. The body is for a question that ordinary primitives and Skills do not express. It is not a substitute for those primitives.

## Trusted surface

The model supplies source text only. The Addin is the execution authority. The Server cannot compile, cannot supply IL, and cannot skip the compiler policy. Roslyn runs only in the CompilerHost child process. The Addin loads compilation bytes only from the child it launched for that request.

The Addin launches that process from a fixed absolute path under the product directory of the loaded Addin assembly. It does not search `PATH`. `UseShellExecute` is false. Standard input and standard output are redirected. The process serves one request and then exits. The request is at most 32,768 bytes and the response is at most 262,144 bytes. The CompilerHost opens no named pipe, network socket, or other listener. A missing executable, a malformed response, an oversized response, a nonzero exit, or no exit within 30 seconds returns `CSHARP_QUERY_COMPILER_FAILED`. The Addin loads nothing from that process.

The CompilerHost receives the loaded `RevitAPI` path and identity from the Addin. The path is not returned to the client. Policy version 1 runs only when that identity is `RevitAPI` 26.5.0.0. Any other loaded identity returns `CSHARP_QUERY_API_UNSUPPORTED` before the process starts. Policy v1 compiles against the pinned .NET 10 reference assemblies shipped at `compiler/refs/net10.0` beside the CompilerHost. It does not use a targeting pack installed on the machine.

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

The host-visible names inside `source` are `doc` and `elementRef`. `elementRef(element)` returns an opaque ADR-0006 `element_ref`, or a deterministic host string when the element is null. The current implementation is based on `Element.UniqueId`. Clients must not parse that value or depend on its format. The spike gate's separate `UniqueId` delegate is not this contract.

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
3. If the loaded assembly is not `RevitAPI` 26.5.0.0, return `CSHARP_QUERY_API_UNSUPPORTED`. Do not start the CompilerHost. Do not reuse the 26.5.0.0 deny inventory for another identity.
4. The Addin launches the CompilerHost child described above and passes the loaded Revit API identity. A process failure returns `CSHARP_QUERY_COMPILER_FAILED` and loads nothing. Policy failure returns `CSHARP_QUERY_POLICY_REJECTED` and does not emit. Compiler errors return `CSHARP_QUERY_COMPILE_FAILED` and do not emit.
5. Enqueue one EXEC-0001 work item. The Addin does not overlap a second generated execution. It does not create a second dispatcher.
6. Inside that work item, require the exact active project document, reject a family document, and require `Document.IsModifiable == false`. Open no transaction.
7. Load the compilation in a collectible `AssemblyLoadContext`. Resolve `RevitAPI` from the default context. Invoke the host entry with `doc` and `elementRef`.
8. Copy the allowed result graph into host objects. Unload the query context. Do not unload `RevitAPI`.

The loop budget is 10,000 charges. Each instrumented loop-body entry charges one. A blocking Revit API call is not interrupted. The in-process executor cannot guarantee protection against all CPU, memory, or blocking-call denial of service.

## Result graph

Allowed: null, boolean, bounded string, numeric primitives (`byte`, `sbyte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `float`, `double`, `decimal`, `char`), a single-dimensional array of allowed values, exactly `List<T>`, exactly `Dictionary<string, T>`, and compiler-generated anonymous projections composed only of allowed values.

`IEnumerable`, `IList`, `IDictionary`, any other collection, a subclass of `List<T>` or `Dictionary<string, T>`, and a deferred LINQ iterator are `CSHARP_QUERY_INVALID_RESULT`. The copier does not call `GetEnumerator` on an arbitrary enumerable. It copies an array by length and index, a `List<T>` by `Count` and indexer, and a `Dictionary<string, T>` through that concrete type.

Depth limit 8. Node limit 1,024. Each scalar, array element, list element, dictionary entry, and anonymous property counts as one node. Serialized `result` limit 16,000 characters. The node count is applied while copying, before serialization. There is no partial result.

The copier calls property getters only on compiler-generated anonymous types in the generated assembly. It does not walk an arbitrary object and does not call getters on a Revit object, an `ElementId`, or an enum. Return those through an allowed projection or through `elementRef`.

## Policy version 1

The reviewed identity is loaded `RevitAPI` 26.5.0.0. Reject before emit. The closed categories are in ADR-0028: transactions, Revit UI, application services, filesystem, network, process creation, reflection, dynamic assembly load, `System.Type` member calls, `System.Environment`, `System.Console`, `System.GC`, `AppDomain`, local functions, self-referential delegate locals, Roslyn, P/Invoke, unsafe code, threads, preprocessor directives, the listed C# escape statements, event subscription, and the effect-bearing Revit API save, sync, export, print, document-lifecycle, external-resource, worksharing, and file-affecting members named in that ADR. `typeof` of an allowed type remains available as a type token.

## Local enablement

Default off. The Addin reads `%LOCALAPPDATA%\RevitMCP\capabilities.json` on every call. Schema version 1 is an object with exactly `schema_version` equal to `1` and `csharp_query_enabled` equal to a boolean. Any missing file, unreadable file, malformed JSON, wrong schema, missing or wrong-type property, or unknown property is disabled. The product does not create or enable the file. No MCP or Bridge argument changes the setting. A future local Revit UI may replace the same file atomically. This is not the write-approval pipeline. Enabling the query grants no write authority.

## Deterministic errors

| Code | When |
| --- | --- |
| `CSHARP_QUERY_DISABLED` | Local opt-in is off or unreadable. |
| `INVALID_CSHARP_QUERY` | Source missing, empty, over 8,000 characters, or the envelope has an unknown or forbidden field. |
| `CSHARP_QUERY_API_UNSUPPORTED` | The loaded assembly is not `RevitAPI` 26.5.0.0. |
| `CSHARP_QUERY_POLICY_REJECTED` | Policy version 1 rejected the body before emit. |
| `CSHARP_QUERY_COMPILE_FAILED` | Roslyn reported errors. |
| `CSHARP_QUERY_COMPILER_FAILED` | The CompilerHost child was missing, exceeded a payload bound, returned a malformed response, exited nonzero, or did not exit within 30 seconds. No bytes are loaded. |
| `CSHARP_QUERY_DOCUMENT_NOT_READY` | No active document, family document, `IsModifiable` is true, or `document_id` does not match. `reason` is `no_active_document`, `project_document_required`, `document_modifiable`, or `document_mismatch`. |
| `CSHARP_QUERY_BUDGET_EXCEEDED` | The cooperative loop budget was exhausted. |
| `CSHARP_QUERY_EXECUTION_FAILED` | The method threw for another reason. No exception text. |
| `CSHARP_QUERY_INVALID_RESULT` | The returned graph is outside the closed set. |
| `CSHARP_QUERY_RESULT_TOO_LARGE` | The result exceeds 1,024 nodes, depth 8, or 16,000 serialized characters. |
| `BRIDGE_PROTOCOL_INCOMPATIBLE` | The connection does not advertise protocol 26. |

Policy and compile failures include at most 8 diagnostics. Each diagnostic has `code`, `line`, `column`, and `message`. Compiler `code` is the C# diagnostic id. Policy `code` is `CSHARP_QUERY_POLICY_REJECTED`. `message` is flattened to 180 characters. The spike ids `BIMQ100`, `BIMQ101`, `BIMQ110`, `BIMQ120`, `BIMQ130`, and `BIMQ140` are research evidence only. Production clients match the codes in this table.

`CSHARP_QUERY_DOCUMENT_NOT_READY` is returned by the Addin and does not include a Revit exception. `document_modifiable` means the host refused to run because the document was open for modification. It does not mean the query changed the document.

## Security

ADR-0028 is the threat model. This capability is not an OS sandbox. No transaction prevents ordinary transactional edits. It does not prove every `RevitAPI` member is free of external effects. Policy version 1 is the deny list reviewed for `RevitAPI` 26.5.0.0 only. The in-process executor cannot guarantee protection against all CPU, memory, or blocking-call denial of service. A newly discovered external effect on that identity is a reviewed policy update, not a new wrapper tool. Another Revit API identity needs its own reviewed manifest.

## Performance

One CompilerHost child and one execution per call in v1. A compiler daemon and a persistent cache are deferred. The cache key is still returned so a later cache can use it. The result budget is 1,024 nodes, depth 8, and 16,000 characters. The character cap stays tighter than the 120,000-character compiled-read budget because the result shape is model-defined and the spike validated 16,000 characters. The node cap stops the copier before serialization.

## Observability

Log `source_sha256`, `cache_key`, `policy_version`, Revit API name and version, `compile_outcome`, `duration_ms`, `result_characters`, and the error code. Do not log `source` or `result` by default. Do not log the Revit API path.

## Composition with CAP-0019

`revit_api.symbols` is optional. Call it when a signature is uncertain. Then call this tool with a focused body. Do not call it as a mandatory preface. Recurring meaning is promoted under ADR-0027 to a Skill or a compiled capability. It is not left forever as generated source.

## Acceptance

This document is the contract. The compiler and execution kernel are implemented. The execution-kernel live gate passed in PR #136 on Revit 2026.5 build 26.5.0.55. Local enablement and Bridge protocol 26 are implemented. `revit_run_csharp_query` is not implemented. Live validation of that MCP tool has not been run. The Bridge live gate is prepared and has not been recorded as passed. The accepted feasibility evidence is PR #133.

An implementation is acceptable when:

- the tool exists, `tools/list` is 23, and protocol 26 is advertised only by a host that implements this contract
- Roslyn is absent from the Addin, Server, Bridge, and Contracts projects
- a disabled setting returns `CSHARP_QUERY_DISABLED` without starting the compiler
- a loaded assembly other than `RevitAPI` 26.5.0.0 returns `CSHARP_QUERY_API_UNSUPPORTED` without starting the CompilerHost
- the CompilerHost is launched from the fixed absolute product path, with `UseShellExecute` false, redirected stdio, one request, and no listener; a malformed response or nonzero exit returns `CSHARP_QUERY_COMPILER_FAILED` and loads nothing
- a transaction body, a local function, and a `System.Environment` use are policy-rejected and are not enqueued
- a read body runs only through EXEC-0001 against the exact project document with `IsModifiable == false`
- `elementRef` returns an ADR-0006 `element_ref`
- the result is the closed graph of arrays, `List<T>`, `Dictionary<string, T>`, and anonymous projections; another enumerable is `CSHARP_QUERY_INVALID_RESULT`
- a result over 1,024 nodes is `CSHARP_QUERY_RESULT_TOO_LARGE`
- the query context can be collected, and `RevitAPI` stays loaded
- a tight loop returns `CSHARP_QUERY_BUDGET_EXCEEDED`, and a later read still succeeds
- no MCP response contains the Revit API path, the source echo, a stack trace, or raw exception text

## Deferred

Generated writes. Per-call approval. Arbitrary references or IL. Python and Dynamo. A remote compiler. A named pipe or network compiler listener. A persistent compiler process. A persistent compilation cache. The ribbon control that edits the local opt-in. Reviewed policy manifests for Revit 2025, Revit 2027, and any loaded identity other than `RevitAPI` 26.5.0.0. Any claim that policy version 1 names every externally effectful Revit API member, or that the executor prevents every CPU, memory, or blocking-call denial of service.
