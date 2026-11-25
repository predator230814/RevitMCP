# ADR-0028: Bounded generated-C# Revit read executor

- Status: Accepted
- Date: 2026-11-24
- Accepted: 2026-11-24 by Dave, Product Owner
- Product Owner: Dave
- Contract: [CAP-0020](../capabilities/CAP-0020-bounded-generated-csharp-read.md)
- Evidence: [EXECUTION_GATE.md](../../spikes/BimIt.Revit.QueryRuntimeSpike/EXECUTION_GATE.md) (PR #133)

Accepted architecture. This ADR authorizes one production capability. It does not implement it. Production `tools/list` stays 22 and Bridge protocol stays 25 until that implementation.

## Context

[ADR-0027](ADR-0027-layered-revit-capability-admission.md) admits Revit behavior as an existing primitive, then a Skill or composition, then bounded C# for the long tail, then a compiled specialized capability. It did not authorize an executor. It said a later revision could narrowly supersede [ADR-0023](ADR-0023-declarative-revit-read-query-architecture.md) §1 only after a real Revit/EXEC-0001 gate and Product Owner acceptance.

PR #133 is that gate. A fixed generated read compiled against the Revit API loaded by Revit 2026.5 (`RevitAPI` 26.5.0.0, not the Nice3point 26.4.10.0 reference), ran through a spike-owned `RevitExecutionDispatcher`, returned structured JSON, left `Document.IsModified` false, stopped a tight loop with the cooperative budget, recovered with another read, and released the collectible load contexts. The gate is feasibility evidence. It is not a production authorization by itself.

[ADR-0023](ADR-0023-declarative-revit-read-query-architecture.md) §1 prohibits model-supplied Python, C#, Dynamo, macro, expression, or Revit API source in the production layer. [ADR-0024](ADR-0024-registry-driven-compiled-revit-read-operations.md) keeps trusted compiled reads behind `revit_lookup` and `revit_read`. [ADR-0025](ADR-0025-bounded-revit-api-property-access.md) keeps generated C# Research Only as a substitute for bounded property reads. [CAP-0019](../capabilities/CAP-0019-revit-api-symbol-discovery.md) describes the loaded API and does not execute it.

The spike compiler runs Roslyn inside the Revit process. That placement is not accepted for production. The gate also returned `UniqueId` from its host delegate. Production identity remains the ADR-0006 `element_ref`.

## Decision

Authorize one narrowly bounded, read-only, generated-C# query surface: [CAP-0020](../capabilities/CAP-0020-bounded-generated-csharp-read.md), MCP tool `revit_run_csharp_query`.

This is the deliberate exception to ADR-0023 §1.

Model-supplied C# remains prohibited except when supplied to CAP-0020 and processed by the exact bounded compiler and execution policy defined by this ADR.

ADR-0023 remains in force for everything else. This ADR does not authorize Python, Dynamo, macros, expressions, arbitrary assemblies, full C# projects, caller-supplied IL, write transactions, or generic generated-code execution.

ADR-0027's admission order is unchanged. CAP-0020 is step 3. It is not a reason to skip a primitive or a Skill, and it is not a compiled specialized capability.

ADR-0025 is unchanged. Generated C# is still not the mechanism for ordinary Element property reads.

## What is superseded

Only this sentence of ADR-0023 §1, and only inside the CAP-0020 boundary:

> The production layer runs only code that BIM-IT compiled, reviewed, and tested.

Inside CAP-0020, the production layer may execute a C# method body supplied by a model or client after the trusted compiler host accepts it and the Addin runs it under this ADR. Outside CAP-0020, that sentence stands. ADR-0023 §8's statement that no model-supplied code runs is superseded in the same narrow way and nowhere else.

## Why this is its own MCP tool

Generated execution has a different trust, security, and governance profile from a CAP-0016 compiled read. `revit_read` runs BIM-IT code selected from a closed registry. `revit_run_csharp_query` runs model-supplied source. Clients must be able to allow-list it separately. Hiding it inside `revit_read` would make that separation impossible.

The tool is vendor-neutral. Any compatible MCP client can call it. BIM-IT Assistant is not required. After implementation, production `tools/list` is 23. This PR does not add the tool.

MCP annotations may describe the tool. They do not enable it, and they are not the safety policy. `readOnlyHint` does not create the read-only boundary.

## Where compilation happens, and who executes

```text
MCP client
→ Server
→ Bridge protocol 26
→ Addin
→ trusted local CompilerHost process
→ bounded semantic analysis and Roslyn emit
→ Addin
→ EXEC-0001
→ collectible AssemblyLoadContext
→ RevitAPI already loaded by that Revit process
→ structured result
```

Roslyn stays outside the Revit process and outside the production Addin dependency graph. It lives only in the trusted BIM-IT CompilerHost executable.

The Addin is the execution authority. It supplies the CompilerHost with the path and assembly identity of `typeof(Document).Assembly` for the running process. That path is internal. It never appears in MCP output, logs intended for agents, or Bridge error text.

The CompilerHost does not execute generated code. It returns diagnostics or compilation bytes to the Addin that started it.

The Addin accepts compilation bytes only from the CompilerHost child process it launched for that request. It rejects arbitrary caller-provided IL. The Bridge payload carries the method body, not an assembly.

CompilerHost v1 is that child process:

- The Addin launches it from a fixed absolute path under the product directory that contains the loaded Addin assembly. The path is product-relative and resolved to an absolute path before launch.
- The Addin does not resolve the executable through `PATH`, the working directory, an MCP argument, or the Bridge payload.
- `UseShellExecute` is false.
- Standard input and standard output are redirected. Standard error is not returned to the MCP client.
- The process handles one request. The Addin writes one request, reads one response, and waits for that process to exit. It does not reuse the process and does not keep a compiler daemon.
- The request is at most 32,768 bytes. The response is at most 262,144 bytes. A larger payload is refused.
- The CompilerHost opens no named pipe, network socket, or other listener.
- A missing executable, a launch failure, a response that exceeds the bound, a malformed response, a nonzero exit, or no exit within 30 seconds returns `CSHARP_QUERY_COMPILER_FAILED`. The Addin loads no bytes from that process.

The Server routes the call and enforces transport rules. It does not compile, does not hold a compiler policy bypass, and does not execute.

v1 still computes the cache key. A persistent compiler process and a persistent compilation cache are later optimizations.

The out-of-process host was not part of the PR #133 gate. The gate proved in-process compilation, EXEC-0001 execution, the budget, and collectible unload. Moving Roslyn out of process is a production constraint, not a claim the gate already demonstrated.

## Source shape

The model supplies a C# method body, not a class, file, or project.

The host wraps that body. The model-facing names are:

- `doc`, the active project `Document`
- `elementRef`, a host delegate `Element -> string` that returns the production ADR-0006 opaque `element_ref`

The model cannot define references, assemblies, usings, compiler options, an entry point, a path, or IL. The host adds the fixed usings (`System`, `System.Collections.Generic`, `System.Linq`, `Autodesk.Revit.DB`). A using directive in the body is rejected.

The cooperative budget is host-owned. The body cannot name it. Loop bodies are instrumented so each iteration charges that budget.

Bounds, carried from the spike unless noted:

| Bound | v1 |
| --- | --- |
| Source | 8,000 characters |
| Diagnostics returned | 8 |
| Diagnostic message | 180 characters, flattened |
| Serialized result | 16,000 characters |
| JSON depth | 8 |
| Result nodes | 1,024 |
| Loop budget | 10,000 charges |
| Concurrent generated executions | one work item at a time on the existing EXEC-0001 queue |

A second query waits on that queue. This tool does not create another dispatcher. The spike's `element_ref` difference is intentional: the gate used `UniqueId`; production uses ADR-0006.

## Document gate

Before any generated method runs, on EXEC-0001:

- the requested `document_id` matches the active document
- the document is a project document
- a family document is rejected
- `Document.IsModifiable` is false
- the host opens no `Transaction`, `SubTransaction`, or `TransactionGroup`

## Static policy

Policy version 1. Rejection happens before emit. The spike analyzer in `ForbiddenApiAnalyzer` is the floor, not the whole policy. v1 also rejects the effect-bearing Revit API surfaces below. The policy is a deny rule inside the compiler. It is not a wrapper per API method, and it does not describe the loaded Revit API as safe.

The v1 deny inventory was reviewed against loaded `RevitAPI` 26.5.0.0. Production support for policy version 1 is that identity only: assembly name `RevitAPI`, version `26.5.0.0`. The Addin compares the assembly loaded by the running Revit process before it launches the CompilerHost. Any other name or version returns `CSHARP_QUERY_API_UNSUPPORTED` and does not compile. The 26.5.0.0 inventory is not reused on an unreviewed identity. Revit 2025, Revit 2027, and any other build are admitted later by a reviewed policy manifest for that exact loaded identity. That admission does not require a new architecture.

Reject before emit:

- `Transaction`, `SubTransaction`, `TransactionGroup`
- `Autodesk.Revit.UI` and `Autodesk.Revit.ApplicationServices`
- filesystem, network, and process APIs (`System.IO`, `System.Net`, `System.Diagnostics`)
- reflection and dynamic assembly load (`System.Reflection`, `System.Reflection.Emit`, `System.Runtime.Loader`, `System.Linq.Expressions`, `dynamic`, `Activator`, `Assembly.Load`, `AppDomain`, and the related spike reflection names)
- reflection through `System.Type`: `typeof` of an allowed type may be passed as a type token, and calling members on a `System.Type` value, including `GetType`, is rejected
- process-wide BCL escape and availability surfaces: `System.Environment`, `System.Console`, and `System.GC`
- local functions
- a lambda or anonymous method that references the local variable it is assigned to, including a delegate local initialized from such a lambda
- Roslyn (`Microsoft.CodeAnalysis`, `System.CodeDom`)
- P/Invoke and unsafe code, including `DllImport`, `LibraryImport`, and `UnmanagedCallersOnly`
- threads, `Parallel`, and `Task.Run` (`System.Threading`)
- preprocessor directives
- `try`, `catch`, `lock`, `goto`, `yield`, `async`, and `await` as compiler or runtime escape mechanisms
- event subscription and removal on Revit types (`add_` / `remove_` methods and event assignment)

Reject these Revit API effect categories even though the members live in `RevitAPI.dll`. The names were read from the Revit 2026.5 assembly `RevitAPI` 26.5.0.0:

- document save and close: `Save`, `SaveAs`, `SaveAsCloudModel`, `SaveCloudModel`, `SaveToProjectAsImage`, `Close`
- export, import, print: `Export`, `ExportImage`, `Import`, `Link`, `Print`, and `PrintManager`
- worksharing and central-model mutation: `SynchronizeWithCentral`, `ReloadLatest`, `EnableWorksharing`, `EnableCloudWorksharing`
- file-affecting family and document lifecycle: `LoadFamily`, `LoadFamilySymbol`, `EditFamily`, `OpenDocumentFile`, `OpenIFCDocument`, `OpenSharedParameterFile`, `ExtractPartAtomFromFamilyFile`
- non-transaction document effects: `Regenerate`, `PostFailure`, `PurgeReleasedAPIObjects`
- external resources and transmission: `TransmissionData`, `ExternalResourceUtils`, `ExternalResourceServerUtils`, `ExternalResourceServiceUtils`, `IExternalResourceServer`
- worksharing utilities that act on a central or a file: `WorksharingUtils`

Reading `Document.IsWorkshared` or a central GUID is not in that effect list. A later reviewed manifest for this same identity may add names. A different loaded identity needs its own reviewed manifest. Neither change turns this list into one MCP operation per method.

## What read-only means

This is not an operating-system sandbox. Generated code runs in the Revit process with the Windows user's permissions once the Addin loads it.

A blocking Revit API call cannot be forcibly interrupted. The budget runs when an instrumented loop body is entered. The in-process executor cannot guarantee protection against all CPU, memory, or blocking-call denial of service. The loop budget, the result bounds, and the rejected BCL surfaces reduce obvious escapes. They do not bound every allocation, every blocking call, or every amount of CPU the generated method can consume.

No open transaction stops ordinary model mutation that Revit requires a transaction to perform. It does not prove that every `RevitAPI` member has no external side effect. The static policy exists because that proof is unavailable.

Threat model for v1:

- A hostile or mistaken method body tries to escape the host, touch the machine, open a transaction, or call a file, network, sync, export, print, or document-lifecycle API.
- The compiler rejects those shapes before emit. The document gate refuses a modifiable or family document before execution. The result copier refuses a live Revit object.
- What remains possible is a Revit API member that changes something outside a transaction and is not yet named by the reviewed policy for that loaded identity. That is a policy gap to close by a reviewed manifest, not a claim of purity.
- A generated method can still exhaust CPU, memory, or block inside an uninterruptible Revit API call. That residual denial of service is outside what this executor can guarantee.
- The local opt-in limits who can reach the tool on that machine. It does not inspect the body.

## Enablement

CAP-0020 has no per-query human approval. Do not reuse the ADR-0008 approval pipeline. That pipeline authorizes a specific write intent. This capability is a read, and approval-per-call would mix two different decisions.

The repository has no existing local capability-enable store. v1 therefore adds one, owned by the Addin:

- default is disabled
- the value is persistent local configuration for the Windows user under the existing `%LOCALAPPDATA%\RevitMCP` root
- it is not an MCP argument, not a client annotation, and not an approval record
- a disabled runtime returns `CSHARP_QUERY_DISABLED` before the compiler process starts and before EXEC-0001
- if the setting cannot be read, the runtime stays disabled
- enabling execution grants no write authority
- the control that edits the setting is local to Revit. It is not an MCP tool. The exact ribbon control is an implementation detail

## Results crossing the collectible context

Generated code must not return a live Revit object.

The v1 result graph is closed:

- null
- boolean
- string
- numeric primitives: `byte`, `sbyte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `float`, `double`, `decimal`, and `char`
- a single-dimensional array whose element type is an allowed value
- exactly `System.Collections.Generic.List<T>`, where `T` is an allowed value
- exactly `System.Collections.Generic.Dictionary<string, T>`, where `T` is an allowed value
- a compiler-generated anonymous type in the generated assembly whose properties are allowed values

`IEnumerable`, `IList`, `IDictionary`, a subclass, another collection type, and a deferred LINQ iterator are rejected. The host does not call `GetEnumerator` on an arbitrary enumerable. Copying an array uses its length and indexer. Copying a `List<T>` uses `Count` and the indexer. Copying a `Dictionary<string, T>` uses that concrete type. A LINQ operator that has not been materialized into one of those shapes is not a result.

Each scalar, array element, list element, dictionary entry, and anonymous property counts as one node. v1 allows 1,024 nodes, depth 8, and 16,000 serialized characters. The node count is checked while copying, before serialization.

Enums and `ElementId` are not allowed nodes. A number that happens to equal an element id is not an `element_ref` and must not be sent to another tool as one. Cross-call element identity is only the string returned by `elementRef`.

Before the generated `AssemblyLoadContext` is unloaded, the Addin copies that graph into ordinary host objects. The only property getters it may call are the getters of those compiler-generated anonymous types, and only to copy an allowed value. It does not reflectively serialize an arbitrary object, and it does not call getters on any other returned object. The PR #133 failure mode is binding: handing the anonymous instance to a serializer that caches its type keeps the collectible context alive.

Revit API stays in the default load context. Unloading the query does not unload it.

## Companion discovery

[CAP-0019](../capabilities/CAP-0019-revit-api-symbol-discovery.md) is the companion for signature checks. The intended agent order is:

1. Use an ordinary primitive when it expresses the question.
2. Use `revit_api.symbols` only when a signature is uncertain.
3. Generate one focused method body.
4. Call `revit_run_csharp_query`.
5. Promote a recurring engineering meaning to a Skill or a compiled capability when ADR-0027 step 2 or step 4 is justified.

Do not require a symbol lookup before every query.

## Observability

Record source hash, cache key, policy version, Revit API name and version, compile outcome, execution duration, result size, and error code. Do not record the method body or the result payload by default. Do not record the Revit API filesystem path.

## Out of scope

Generated writes, a `run_modify` analogue, a transaction rollback sandbox, per-call approval, arbitrary DLL references, arbitrary IL, Python, Dynamo, a remote compiler, a named pipe or network compiler listener, a persistent compiler daemon, and a wrapper catalogue of the Revit API. A generated write needs its own ADR. Reviewed manifests for identities other than `RevitAPI` 26.5.0.0, including Revit 2025 and Revit 2027, are later admissions under this same architecture. This ADR does not claim the executor can stop every CPU, memory, or blocking-call denial of service.

## Consequences

- Implementation adds `revit_run_csharp_query`, Bridge protocol 26, and the CompilerHost. Until that implementation, protocol 26 is unsupported and `tools/list` stays 22.
- Production projects still must not reference Roslyn or the query-runtime spike.
- ADR-0023 §1 and §8 carry a pointer to this exception. Their other rules stay.
- ADR-0027's admission order stays. Its earlier "no executor" sentences are superseded by this ADR for the read executor only.
