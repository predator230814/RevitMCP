using RevitMCP.Server;
using Xunit;

namespace RevitMCP.Server.Tests;

public sealed class DependencyBoundaryTests
{
    private static readonly string[] ForbiddenNames =
    [
        "RevitAPI",
        "RevitAPIUI",
        "RevitMCP.Addin"
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
    }
}
