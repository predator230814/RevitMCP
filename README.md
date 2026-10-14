# BIM-IT Revit MCP

Vendor-neutral Model Context Protocol capability layer for Autodesk Revit. The product name is BIM-IT Revit MCP. The repository and historical ADRs still use the RevitMCP name. Current code uses the `BimIt.Revit` namespaces.

BIM-IT Assistant is a separate local process and DockablePane. It is a client of this MCP service. Natural language is answered inside that process by a provider adapter. The first adapter is GitHub Copilot. External MCP clients do not need the Assistant or Copilot.

A dogfood machine needs GitHub CLI authentication for the Windows user (`gh auth login`). The Assistant runs Copilot in empty mode, which disables keytar, so a Copilot CLI keychain login is not a guaranteed credential. BIM-IT does not collect or store a GitHub token. The live checklist is [docs/BIM_IT_ASSISTANT_COPILOT_VALIDATION.md](docs/BIM_IT_ASSISTANT_COPILOT_VALIDATION.md).

## Building

The repository uses the .NET 10 SDK pinned in `global.json` and one multi-version add-in project.

```powershell
dotnet build src/BimIt.Revit.Contracts/BimIt.Revit.Contracts.csproj
dotnet build src/BimIt.Revit.Bridge/BimIt.Revit.Bridge.csproj
dotnet build src/BimIt.Revit.Server/BimIt.Revit.Server.csproj
dotnet build src/BimIt.Revit.Assistant.Ipc/BimIt.Revit.Assistant.Ipc.csproj
dotnet build src/BimIt.Revit.Assistant/BimIt.Revit.Assistant.csproj

dotnet build src/BimIt.Revit.Addin/BimIt.Revit.Addin.csproj -p:RevitVersion=2025
dotnet build src/BimIt.Revit.Addin/BimIt.Revit.Addin.csproj -p:RevitVersion=2026
dotnet build src/BimIt.Revit.Addin/BimIt.Revit.Addin.csproj -p:RevitVersion=2027
```

`RevitVersion` is required for `BimIt.Revit.Addin` and must be `2025`, `2026`, or `2027`. Solution-wide `dotnet build BimIt.Revit.sln` therefore needs `-p:RevitVersion=...` as well. Local IDE builds can set that property in an untracked `Directory.Build.user.props` file.

```powershell
dotnet test tests/BimIt.Revit.Contracts.Tests/BimIt.Revit.Contracts.Tests.csproj
dotnet test tests/BimIt.Revit.Bridge.Tests/BimIt.Revit.Bridge.Tests.csproj
dotnet test tests/BimIt.Revit.Server.Tests/BimIt.Revit.Server.Tests.csproj
dotnet test tests/BimIt.Revit.Assistant.Tests/BimIt.Revit.Assistant.Tests.csproj
dotnet test tests/BimIt.Revit.Addin.Tests/BimIt.Revit.Addin.Tests.csproj
```

`BimIt.Revit.Addin.Tests` compiles the add-in with `RevitVersion=2026` by default and does not require Autodesk Revit to be installed. The Nice3point Revit API packages are version-pinned compile-time references and must not appear as `RevitAPI.dll` / `RevitAPIUI.dll` in add-in output.

The first live check for the Assistant pane is [docs/BIM_IT_ASSISTANT_VALIDATION.md](docs/BIM_IT_ASSISTANT_VALIDATION.md). The Copilot natural-language check is [docs/BIM_IT_ASSISTANT_COPILOT_VALIDATION.md](docs/BIM_IT_ASSISTANT_COPILOT_VALIDATION.md). Primary target: Revit 2026.5 build 26.5.0.55. `GitHub.Copilot.SDK` 1.0.21 is the Assistant-only provider package.
