# RevitMCP WebView2 spike

Isolated feasibility check for this topology:

```text
Revit DockablePane
-> WPF host
-> Microsoft WebView2
-> local HTML/CSS/JavaScript
```

This is not the production RevitMCP add-in, not an MCP tool, and not an accepted UI architecture. ADR-0009 does not accept WebView2. Live Revit validation and Tech Lead review are still required before any product decision.

The spike does not create a Revit transaction, call `Parameter.Set`, save, or sync. JavaScript can send only a whitelisted `ping` message. Unknown messages are rejected. The page is local and cannot navigate away from `https://revitmcp-spike.local/`.

## Package

The managed SDK reference follows the WebView2 assemblies already loaded by that Revit year. The installed Evergreen browser runtime stays unchanged and is discovered at runtime. This spike does not download that runtime and does not add an assembly resolver.

- Revit 2025: `Microsoft.Web.WebView2` `1.0.2045.28`
- Revit 2026: `Microsoft.Web.WebView2` `1.0.2478.35`
- Revit 2027: `Microsoft.Web.WebView2` `1.0.2478.35`

Revit API packages follow the repository matrix:

- Revit 2025: `net8.0-windows`, Nice3point `2025.4.60`
- Revit 2026: `net8.0-windows`, Nice3point `2026.4.10`
- Revit 2027: `net10.0-windows`, Nice3point `2027.2.0`

## Build

From the repository root:

```text
dotnet build spikes/RevitMCP.WebView2Spike/RevitMCP.WebView2Spike.csproj -p:RevitVersion=2025 --configuration Release
dotnet build spikes/RevitMCP.WebView2Spike/RevitMCP.WebView2Spike.csproj -p:RevitVersion=2026 --configuration Release
dotnet build spikes/RevitMCP.WebView2Spike/RevitMCP.WebView2Spike.csproj -p:RevitVersion=2027 --configuration Release
dotnet test spikes/RevitMCP.WebView2Spike.Tests/RevitMCP.WebView2Spike.Tests.csproj --configuration Release
```

The spike is not part of `RevitMCP.sln`. `RevitAPI.dll` and `RevitAPIUI.dll` must not appear in the output. `Microsoft.Web.WebView2.Core.dll`, `Microsoft.Web.WebView2.Wpf.dll`, `WebView2Loader.dll`, and `ui/` must appear.

Browser profile data is created under `%LOCALAPPDATA%\RevitMCP\WebView2Spike\<RevitYear>\`, not in the repository or the Revit install directory.

## Load in Revit

Build the matching year, then run:

```text
powershell -File spikes/RevitMCP.WebView2Spike/Register-WebView2SpikeAddin.ps1 -RevitVersion 2026
```

Use `2025` or `2027` for the other years. Restart that Revit version. The manifest is `RevitMCP.WebView2Spike.addin`, separate from the production RevitMCP development add-in.

Show and hide the pane from Add-Ins > External Tools:

- RevitMCP UI Spike - Show
- RevitMCP UI Spike - Hide

If the Evergreen WebView2 Runtime is missing, the pane shows that failure and Revit keeps running.

## Manual validation

Do not mark a year PASS until these are observed in that Revit process. Current status: **PENDING** for Revit 2025, Revit 2026.5, and Revit 2027.

Use a disposable project only.

1. Revit starts with the spike loaded.
2. Dockable pane registration succeeds. A failed registration leaves the show command reporting that the pane is not registered.
3. The pane can be shown.
4. The local HTML page renders.
5. Host to UI shows PASS after `hostReady`.
6. Send ping. UI to Host shows PASS.
7. The same action shows Host to UI PASS for `pong`.
8. Hide the pane, show it again, and send ping again.
9. Open or switch a disposable document. The document stays unmodified. The spike does not read the document.
10. No transaction, save, or sync runs.
11. The page shows the Revit version/build, .NET runtime, WebView2 SDK version, Evergreen runtime version, local origin, and every loaded `Microsoft.Web.WebView2.*` assembly version and redacted location.
12. Revit closes without a crash or hang.
13. Start and close Revit at least three times. Record any pane, WebView2, or shutdown instability.

Record the on-screen assembly list. More than one version of the same `Microsoft.Web.WebView2.*` assembly is a compatibility concern. Do not add an `AssemblyResolve` hook to hide it. Do not copy usernames, model paths, or cloud identifiers into the evidence.

## Limitations

- WebView2 is not an accepted RevitMCP UI architecture.
- The Evergreen runtime is a machine prerequisite.
- Airspace, DPI, and dock/undock behavior are live questions, not answered by the build.
- DevTools are enabled so a reviewer can inspect the local page. The page still has no host-object bridge and no generic native execution message.
- The spike does not implement approval, MCP Apps, writes, or authentication.
