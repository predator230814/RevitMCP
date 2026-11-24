# Query runtime spike findings

Date: 2026-11-24. Base: `main` at `e0a7b14` (PR #130). This is evidence for a future ADR-0027. It does not change production behavior, and it does not supersede ADR-0023.

## Recommendation

**Viable with constraints.**

The closed read-only method-body design can be compiled with Roslyn, rejected by semantic policy, instrumented with a cooperative loop budget, diagnosed in a small stable form, and keyed for a later cache. Generated assemblies unload through a collectible `AssemblyLoadContext`. A real `Document` can be compiled against, and the host is shaped to run only when that document is a non-modifiable project document.

ADR-0023 §1 still forbids executing model-supplied C#, Python, Dynamo, expressions, or Revit API source in the production capability layer. ADR-0025 keeps generated or flexible C# Research Only. This spike does not lift either decision. It does not add `revit_api.symbols`, ADR-0027, a production executor, an MCP tool, a Bridge RPC, or a protocol change.

## Smallest production slice

Ship a descriptive `revit_api.symbols` read before any executor.

That slice uses the reflection index below. It returns bounded C# symbol descriptions. It does not accept source, does not emit IL, and does not replace `revit_api.metadata` or `revit_api.element_properties` (CAP-0017 / ADR-0025). It needs its own capability contract. It does not need a Bridge protocol bump if it is reached through the existing read-operation gate.

An executor is a later slice, and only after an accepted ADR explicitly supersedes the ADR-0023 prohibition. The smallest executor slice, when that decision exists, is still internal:

- one read-only method body;
- Roslyn referenced only by the component that compiles, which the production Addin build does not reference today;
- compile against the RevitAPI already loaded in that Revit process;
- policy version, cooperative budget, and the cache key proven here;
- invocation only from the EXEC-0001 handler, after `Document.IsModifiable` is false;
- no MCP tool, no write, no transaction, and no protocol bump until the contract itself is accepted.

## Answers

1. **Roslyn can target the running Revit API without living in the production Addin.** The spike project references Roslyn and the Nice3point RevitAPI 2026.4.10 compile-time assembly (`RevitAPI` 26.4.10.0). `src/BimIt.Revit.Addin` does not reference the spike or `Microsoft.CodeAnalysis`. The opt-in live command resolves the metadata path from `typeof(Document).Assembly.Location`, so a Revit 2026.5 process would compile against the assembly that process already loaded. Roslyn is loaded only if that separate spike command is loaded.

2. **Collectible unload works. A real `Document` was not executed in this session.** `RevitAPI.dll` in the NuGet package is a mixed-mode x64 assembly. Loading it outside Revit fails because its native dependencies are absent. A generated assembly whose signature mentions `Document` and `Element` therefore also fails to load here, and the executor reports `BIMQ140` without treating that as a successful query. The same rewriter, policy, budget, JSON serializer, and collectible context were executed with a host probe whose parameters are `object` and `Func<object, string>`. After `return 1;`, the load context is collectible. Inside Revit the generated assembly must resolve RevitAPI from the default load context, so unloading the query does not unload RevitAPI. The production `RevitExecutionDispatcher` was not modified. The live command now creates a spike-owned dispatcher and enqueues through it. The Revit 2026.5 result is [EXECUTION_GATE.md](EXECUTION_GATE.md).

3. **Semantic analysis rejects the closed list before emit.** Rejected samples include `System.IO`, `System.Net`, `System.Diagnostics.Process`, `System.Reflection`, `Transaction`, `SubTransaction`, `TransactionGroup`, `Autodesk.Revit.UI`, `Document.Application`, `Document.Save`, `Document.Close`, `Task.Run`, `unsafe`, and `DllImport`. `try`, `goto`, `lock`, `yield`, `async`, and preprocessor directives are also rejected. This is a closed list, not an operating-system sandbox. A Revit method that mutates without using a name on the list can still be compiled. `IsModifiable == false` is required before execution. `IsModified` can show that a call changed the document; it cannot prevent the call.

4. **`for`, `foreach`, `while`, and `do` bodies call a host-owned budget check.** A `while` loop with limit 5 returns `BIMQ120`. A script that uses all four forms and stays inside the budget returns `5`. `QueryBudget.BlockingCallLimitation` records the limit: a blocking Revit API call cannot be safely interrupted. The check runs when an instrumented loop body is entered.

5. **Diagnostics are a short record:** severity, code, line, column, message. At most 8 are returned. Compiler ids such as `CS1061` are preserved. Policy ids are `BIMQ100` (source size), `BIMQ101` (result size), `BIMQ110` (policy), `BIMQ120` (budget), `BIMQ130` (document readiness), and `BIMQ140` (execution or JSON failure). Messages are flattened and capped.

6. **The same source produces a stable cache key.** The key is the SHA-256 of the contract name, policy version, RevitAPI identity (name, version, public-key token, file length), and the exact source. A second compile of the same source is a cache hit. A different source, policy version, or file length changes the key. No persistent cache is implemented. The NuGet reference assembly reports no public-key token and a file length that will not match the Revit 2026.5 runtime assembly, so a cache built from the package must not be reused for the running Revit process.

## Script shape

The model writes statements, not a project. The host wraps them as `GeneratedQuery.Execute`. On the Revit contract the parameters are `Document doc`, `Func<Element, string> elementRef`, and a host-owned `QueryBudget`. The probe used by automated execution has the same budget and `elementRef` call shape with `object` in place of the Revit types. Ordinary Revit calls stay direct (`FilteredElementCollector`, `OfClass`, `element.Name`). The result is serialized by the host. Revit objects are refused. Source is capped at 8,000 characters and JSON at 16,000.

## Symbol index

`RevitApiSymbolIndex` reflects over a loaded RevitAPI assembly. Tests open the 2026.4.10 reference through `MetadataLoadContext`. The index describes `XYZ` (class, not an `Element` subclass, with `X`/`Y`/`Z`), `FilteredElementCollector.OfClass`, `Line.CreateBound` as a static factory, `ElementTransformUtils.CopyElements` with at least two overloads, `BuiltInCategory` including `OST_Walls` inside a 32-value page, and the first obsolete `Autodesk.Revit.DB` type in ordinal order. Method groups are capped at 16 and overloaded groups are kept ahead of that cap. Nothing in the description is marked callable. CAP-0017 is unchanged.

## Live command

`spikes/BimIt.Revit.QueryRuntimeSpike.Live` builds a read-only external command. It compiles the fixed samples against `typeof(Document).Assembly.Location`, rejects `new Transaction(...)` before enqueue, creates a spike-owned `RevitExecutionDispatcher`, and returns without waiting. The generated query runs only inside the queued work item. The command attribute is `TransactionMode.ReadOnly`. The command does not construct a `Transaction`. The `.addin` manifest is copied to the spike output only. It is not installed. The Revit 2026.5 gate is [EXECUTION_GATE.md](EXECUTION_GATE.md).

## Tests

`dotnet test spikes/BimIt.Revit.QueryRuntimeSpike.Tests/BimIt.Revit.QueryRuntimeSpike.Tests.csproj --configuration Release` with the x64 .NET 8 host: 29 passed. The machine's default `dotnet` is .NET 10 and cannot run this `net8.0-windows` suite. Production `tools/list` remains 22. Bridge protocol remains 25. Controlled-write code was not modified.
