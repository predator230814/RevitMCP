using Xunit;

namespace RevitMCP.Bridge.Tests;

public sealed class DependencyBoundaryTests
{
    private static readonly string[] ForbiddenNames =
    [
        "RevitAPI",
        "RevitAPIUI",
        "ModelContextProtocol"
    ];

    [Fact]
    public void Bridge_has_no_revit_api_or_mcp_references()
    {
        var referenced = typeof(BridgeInfo).Assembly
            .GetReferencedAssemblies()
            .Select(name => name.Name ?? string.Empty)
            .ToArray();

        foreach (var forbidden in ForbiddenNames)
        {
            var match = referenced.FirstOrDefault(name =>
                name.Equals(forbidden, StringComparison.OrdinalIgnoreCase)
                || name.StartsWith(forbidden + ".", StringComparison.OrdinalIgnoreCase));

            Assert.True(match is null, $"Assembly '{typeof(BridgeInfo).Assembly.GetName().Name}' unexpectedly references '{match}'.");
        }
    }
}
