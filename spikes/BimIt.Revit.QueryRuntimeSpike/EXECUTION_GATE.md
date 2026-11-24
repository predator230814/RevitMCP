# Query runtime EXEC-0001 live gate

- Status: **PASS**
- Tested spike sources: `d6307450e05aaebac95cccc9c2b6c7714bcebf27`. This sentence does not change those binaries.
- Conclusion: **PASS — ready for an ADR decision on a narrowly authorized production read executor.**
- This does not authorize that executor. ADR-0023 §1 and ADR-0027 are unchanged. ADR-0025 flexible C# stays Research Only.
- This is bounded read execution through the existing Revit dispatcher. It is not an operating-system sandbox.

The gate used a disposable saved project, not a family document. `Document.IsModified` was already `false` before the command. Nothing was saved to make it false. No MCP client, Bridge RPC, Server tool, or model-supplied source was involved.

## Environment

| Item | Observed |
| --- | --- |
| Revit | 2026.5, build `26.5.0.55` |
| Host runtime | .NET `10.0.15` |
| Loaded Revit API | `RevitAPI` `26.5.0.0` |
| Compiler identity | `RevitAPI` `26.5.0.0`, same name and version as the loaded assembly |
| Production add-in | One loaded `BimIt.Revit.Addin`, version `1.0.0.0`, from the 2026 Release output. The spike output does not contain that DLL. |
| Dispatcher | Spike-owned `RevitExecutionDispatcher`. The production dispatcher type was not modified. |
| Query | Fixed `LevelProjection`. Host `elementRef` returns `UniqueId` for this spike. That is not an ADR-0006 `element_ref`. |

Revit 2026.5 hosts .NET 10. The spike project still targets `net8.0-windows`. Compilation metadata references follow the hosting runtime, so the loaded `RevitAPI` is not paired with the net8 reference pack.

## Order of proof

Run `2b1cccaba33c4429b522139df3771d66`. `completed_before_return` is `false`: `Schedule` returned before the queued work ran.

| Step | Evidence |
| --- | --- |
| Compile against the loaded Revit API | `loaded_revit_api_name` `RevitAPI`, `loaded_revit_api_version` `26.5.0.0`, equal to the compiler identity. |
| `new Transaction(...)` before enqueue | `BIMQ110`. `transaction_success` false. No assembly. `TransactionExecutionEnqueued` is false. The rejection is in the preamble, written before `EnqueueAsync`. |
| `LevelProjection` through the dispatcher | Step `level` succeeded after `Schedule` had returned. The work item is the `EnqueueAsync` callback, which runs on the production `ExternalEvent` handler. |
| Structured JSON | `{"count":6,"sample":[{"id":"e3e052f9-0156-11d5-9301-0000863f27ad-00000137","name":"Level 1"},{"id":"458c0e49-01bb-11d5-9302-0000863f27ad-000002b6","name":"Ceiling"},{"id":"ef57b02a-5e81-49e7-93bb-ae5f002d921c-00030015","name":"Roof Line"}]}` |
| `IsModified` | `false` -> `false` on every step. |
| Second normal query | Step `level-repeat` succeeded. Same cache key and same JSON. |
| Tight loop | Step `tight-loop` failed with `BIMQ120`. |
| Normal query after `BIMQ120` | Step `level-after-budget` succeeded. **PASS.** |
| Collectible contexts | `context_collected` true for `level`, `level-repeat`, `tight-loop`, and `level-after-budget` after at most 10 GC cycles. Revit API itself stayed in the default load context. |
| Revit stayed responsive | A following pyRevit read returned in 22 ms, `IsModified` still `false`, and the same document still had 6 levels. |

## Cache keys

| Query | Key |
| --- | --- |
| `LevelProjection` | `3b3c53994b07c63a43b43a8cb9a925f3205ac055a9049e3fae182859c2356a34` |
| Tight loop | `439a7b1df542b4c70c2f21c307a21340516b3ac5e8b5c1fb75b808b37eb6da3d` |

The three normal runs used the level key.

## Constraint found and kept inside the spike

An earlier dispatch of the same command, before the serializer change, executed the query and returned `BIMQ120`, but the normal-run collectible contexts stayed alive. `System.Text.Json` was caching the generated anonymous types. The host now copies the result into ordinary dictionaries and lists before serialization. The recorded run is the one after that change. A blocking Revit API call is still not interruptible; the budget only stops instrumented loops.

## Harness

Build with the .NET 8 SDK:

`dotnet build spikes/BimIt.Revit.QueryRuntimeSpike.Live/BimIt.Revit.QueryRuntimeSpike.Live.csproj -c Release`

Start Revit 2026 with the production BIM-IT add-in already loaded. Open a saved project document that is not a family and whose `Document.IsModified` is already false. From a valid Revit API context, call `QueryRuntimeLiveCommand.Schedule` once. It returns `scheduled` and does not wait. The report is written to `%LOCALAPPDATA%\RevitMCP\spikes\query-runtime-exec-gate.json`. The `.addin` file is not installed.

## Production surface

No MCP tool, Bridge RPC, protocol version, or Server registration was added. Production `tools/list` stays 22. Bridge protocol stays 25. Production projects do not reference this spike, `Microsoft.CodeAnalysis`, or a generated-code executor. Roslyn stays in this spike.
