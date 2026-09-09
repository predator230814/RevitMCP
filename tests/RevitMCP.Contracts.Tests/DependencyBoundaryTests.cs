using System.Reflection;
using Xunit;

namespace RevitMCP.Contracts.Tests;

public sealed class DependencyBoundaryTests
{
    private static readonly string[] ForbiddenNames =
    [
        "RevitAPI",
        "RevitAPIUI",
        "ModelContextProtocol",
        "StreamJsonRpc"
    ];

    [Fact]
    public void Contracts_has_no_revit_mcp_or_streamjsonrpc_references()
    {
        AssertNoForbiddenReferences(typeof(ContractsInfo).Assembly, ForbiddenNames);
        AssertNoForbiddenReferences(typeof(RevitInstanceRegistration).Assembly, ForbiddenNames);
        AssertNoForbiddenReferences(typeof(BridgeHandshakeRequest).Assembly, ForbiddenNames);
    }

    internal static void AssertNoForbiddenReferences(Assembly assembly, IReadOnlyList<string> forbiddenNames)
    {
        var referenced = assembly
            .GetReferencedAssemblies()
            .Select(name => name.Name ?? string.Empty)
            .ToArray();

        foreach (var forbidden in forbiddenNames)
        {
            var match = referenced.FirstOrDefault(name =>
                name.Equals(forbidden, StringComparison.OrdinalIgnoreCase)
                || name.StartsWith(forbidden + ".", StringComparison.OrdinalIgnoreCase));

            Assert.True(match is null, $"Assembly '{assembly.GetName().Name}' unexpectedly references '{match}'.");
        }
    }
}
