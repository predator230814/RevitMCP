# CAP-0020 Bridge live gate

Non-MCP client. It speaks `revit.run_csharp_query` through `NamedPipeBridgeClient`. It does not register an MCP tool and it does not write `%LOCALAPPDATA%\RevitMCP\capabilities.json`.

Use Revit 2026.5 build 26.5.0.55, one disposable project document, and `Document.IsModified == false`. Restart Revit after this slice is built and loaded so the Addin advertises protocol 26. Do not restart Revit between A, B, C, and D.

Build:

```text
dotnet build spikes/BimIt.Revit.CSharpQueryBridgeHarness/BimIt.Revit.CSharpQueryBridgeHarness.csproj
```

Run the built executable with `A`, `B`, `C`, or `D`.

## A — default off

Delete `%LOCALAPPDATA%\RevitMCP\capabilities.json` if it exists. Run `A`.

Expected: negotiated protocol 26, `CSHARP_QUERY_DISABLED`, no CompilerHost process, and the document stays unmodified.

## B — enable locally

Create the file with exactly:

```json
{
  "schema_version": 1,
  "csharp_query_enabled": true
}
```

Do not restart Revit. Run `B`.

Expected: negotiated protocol 26, success, real Level `element_ref` / `name` / `elevation`, `source_sha256`, `cache_key`, Revit API name `RevitAPI` and version `26.5.0.0`, closed JSON, and `Document.IsModified` still false.

## C — policy failure

Leave the file enabled. Run `C`. The body is `return Environment.TickCount;`.

Expected: `CSHARP_QUERY_POLICY_REJECTED` with at most 8 diagnostics (`code`, `line`, `column`, `message`). No result and no model change.

## D — disable again

Set `csharp_query_enabled` to false or delete the file. Do not restart Revit. Run `D`.

Expected: `CSHARP_QUERY_DISABLED`.
