# RevitMCP

Vendor-neutral Model Context Protocol capability layer for Autodesk Revit.

## Building

The repository uses the .NET 10 SDK pinned in `global.json` and one multi-version add-in project.

```powershell
dotnet build src/RevitMCP.Contracts/RevitMCP.Contracts.csproj
dotnet build src/RevitMCP.Bridge/RevitMCP.Bridge.csproj
dotnet build src/RevitMCP.Server/RevitMCP.Server.csproj

dotnet build src/RevitMCP.Addin/RevitMCP.Addin.csproj -p:RevitVersion=2025
dotnet build src/RevitMCP.Addin/RevitMCP.Addin.csproj -p:RevitVersion=2026
dotnet build src/RevitMCP.Addin/RevitMCP.Addin.csproj -p:RevitVersion=2027
```

`RevitVersion` is required for `RevitMCP.Addin` and must be `2025`, `2026`, or `2027`. Solution-wide `dotnet build RevitMCP.sln` therefore needs `-p:RevitVersion=...` as well. Local IDE builds can set that property in an untracked `Directory.Build.user.props` file.

```powershell
dotnet test tests/RevitMCP.Contracts.Tests/RevitMCP.Contracts.Tests.csproj
dotnet test tests/RevitMCP.Bridge.Tests/RevitMCP.Bridge.Tests.csproj
dotnet test tests/RevitMCP.Server.Tests/RevitMCP.Server.Tests.csproj
```
