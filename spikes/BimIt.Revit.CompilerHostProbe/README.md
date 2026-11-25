# CompilerHost compile-only probe

This command is not registered by the production add-in. It compiles one fixed level-count body through the packaged production CompilerHost. It does not load the PE, execute the query, call EXEC-0001, or change the model.

Build the 2026 add-in first so `compiler\BimIt.Revit.CompilerHost.exe` sits beside `BimIt.Revit.Addin.dll`:

```text
dotnet build src/BimIt.Revit.Addin/BimIt.Revit.Addin.csproj -c Release -p:RevitVersion=2026
dotnet build spikes/BimIt.Revit.CompilerHostProbe/BimIt.Revit.CompilerHostProbe.csproj -c Release
```

In Revit 2026.5, with the production add-in already loaded, invoke `BimIt.Revit.CompilerHostProbe.CompilerHostProbeCommand` through Add-In Manager or an external command you add locally. Do not commit that registration.

The report is `%LOCALAPPDATA%\RevitMCP\spikes\compiler-host-probe.json`. A successful run has `loadedMatchesFile` true, `apiVersion` `26.5.0.0`, `outcome` `success`, a source hash, a cache key, and `peBytes` greater than zero. The report does not contain the Revit API path or the method body.
