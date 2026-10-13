# RevitMCP

Vendor-neutral Model Context Protocol capability layer for Autodesk Revit.

## Building

The repository uses the .NET 10 SDK pinned in `global.json` and one multi-version add-in project.

```powershell
dotnet build src/BimIt.Revit.Contracts/BimIt.Revit.Contracts.csproj
dotnet build src/BimIt.Revit.Bridge/BimIt.Revit.Bridge.csproj
dotnet build src/BimIt.Revit.Server/BimIt.Revit.Server.csproj

dotnet build src/BimIt.Revit.Addin/BimIt.Revit.Addin.csproj -p:RevitVersion=2025
dotnet build src/BimIt.Revit.Addin/BimIt.Revit.Addin.csproj -p:RevitVersion=2026
dotnet build src/BimIt.Revit.Addin/BimIt.Revit.Addin.csproj -p:RevitVersion=2027
```

`RevitVersion` is required for `BimIt.Revit.Addin` and must be `2025`, `2026`, or `2027`. Solution-wide `dotnet build BimIt.Revit.sln` therefore needs `-p:RevitVersion=...` as well. Local IDE builds can set that property in an untracked `Directory.Build.user.props` file.

```powershell
dotnet test tests/BimIt.Revit.Contracts.Tests/BimIt.Revit.Contracts.Tests.csproj
dotnet test tests/BimIt.Revit.Bridge.Tests/BimIt.Revit.Bridge.Tests.csproj
dotnet test tests/BimIt.Revit.Server.Tests/BimIt.Revit.Server.Tests.csproj
dotnet test tests/BimIt.Revit.Addin.Tests/BimIt.Revit.Addin.Tests.csproj
```

`BimIt.Revit.Addin.Tests` compiles the add-in with `RevitVersion=2026` by default and does not require Autodesk Revit to be installed. The Nice3point Revit API packages are version-pinned compile-time references and must not appear as `RevitAPI.dll` / `RevitAPIUI.dll` in add-in output.
