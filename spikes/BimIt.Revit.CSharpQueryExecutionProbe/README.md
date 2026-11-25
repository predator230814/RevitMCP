# Generated-query execution probe

This command is not registered by the production add-in. It compiles three fixed method bodies with the packaged CompilerHost, then runs them through the production execution kernel on a `RevitExecutionDispatcher` queue. `IExternalCommand.Execute` returns before that queue runs. The command does not load an MCP tool, does not open a transaction, and does not save the model.

Build the 2026 add-in, then the probe:

```text
dotnet build src/BimIt.Revit.Addin/BimIt.Revit.Addin.csproj -c Release -p:RevitVersion=2026
dotnet build spikes/BimIt.Revit.CSharpQueryExecutionProbe/BimIt.Revit.CSharpQueryExecutionProbe.csproj -c Release
```

In Revit 2026.5, load the production add-in. Open one disposable project document whose `Document.IsModified` is false. Invoke `BimIt.Revit.CSharpQueryExecutionProbe.CSharpQueryExecutionProbeCommand` through Add-In Manager or a local command you do not commit.

The report is `%LOCALAPPDATA%\RevitMCP\spikes\csharp-query-execution-probe.json`. A passing run has `passed` true, `contexts_collected` true, and six steps:

1. `normal` success, `api_version` `26.5.0.0`, host-owned JSON, `modified_before` and `modified_after` false.
2. `repeat` success with the same source hash and cache key.
3. `budget` code `CSHARP_QUERY_BUDGET_EXCEEDED` and no result JSON.
4. `after_budget` success.
5. `invalid` code `CSHARP_QUERY_INVALID_RESULT` and no result JSON.
6. `after_invalid` success.

`csharp-query-execution-probe.enqueue.json` has `completed_before_return` false. The report does not contain the method body or the Revit API path.
