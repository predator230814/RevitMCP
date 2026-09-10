using RevitMCP.Server;
using Xunit;

namespace RevitMCP.Server.Tests;

public sealed class DependencyBoundaryTests
{
    private static readonly string[] ForbiddenNames =
    [
        "RevitAPI",
        "RevitAPIUI",
        "RevitMCP.Addin",
        "Autodesk.Revit",
        "ModelContextProtocol.AspNetCore"
    ];

    [Fact]
    public void Server_has_no_autodesk_revit_api_or_addin_references()
    {
        var referenced = typeof(Program).Assembly
            .GetReferencedAssemblies()
            .Select(name => name.Name ?? string.Empty)
            .ToArray();

        foreach (var forbidden in ForbiddenNames)
        {
            var match = referenced.FirstOrDefault(name =>
                name.Equals(forbidden, StringComparison.OrdinalIgnoreCase)
                || name.StartsWith(forbidden + ".", StringComparison.OrdinalIgnoreCase));

            Assert.True(match is null, $"Assembly '{typeof(Program).Assembly.GetName().Name}' unexpectedly references '{match}'.");
        }

        Assert.Contains(referenced, name => name.Equals("ModelContextProtocol", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(referenced, name => name.Equals("RevitMCP.Bridge", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Server_project_references_mcp_sdk_but_not_aspnetcore()
    {
        var project = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "RevitMCP.Server", "RevitMCP.Server.csproj"));
        Assert.Contains("ModelContextProtocol", project, StringComparison.Ordinal);
        Assert.DoesNotContain("ModelContextProtocol.AspNetCore", project, StringComparison.Ordinal);
        Assert.DoesNotContain("RevitAPI", project, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "RevitMCP.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate RevitMCP.sln from the test output directory.");
    }
}
