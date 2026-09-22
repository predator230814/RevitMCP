using RevitMCP.Addin.Inspection;
using RevitMCP.Contracts;
using Xunit;

namespace RevitMCP.Addin.Tests;

public sealed class ParameterIdentityClassifierTests
{
    [Fact]
    public void Built_in_identity_uses_api_type_id_and_does_not_parse_prefixes()
    {
        var identity = ParameterIdentityClassifier.Classify(
            isBuiltIn: true,
            builtInTypeId: "autodesk.revit.parameter:allModelMark",
            isShared: true,
            sharedGuid: Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            localStableKey: "unused");

        Assert.Equal(DescribeParameterIdentityKind.BuiltIn, identity.Kind);
        Assert.Equal("autodesk.revit.parameter:allModelMark", identity.ParameterTypeId);
        Assert.Null(identity.SharedGuid);
        Assert.Equal("autodesk.revit.parameter:allModelMark", identity.StableKey);
    }

    [Fact]
    public void Shared_guid_is_canonical_lowercase()
    {
        var identity = ParameterIdentityClassifier.Classify(
            isBuiltIn: false,
            builtInTypeId: null,
            isShared: true,
            sharedGuid: Guid.Parse("AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE"),
            localStableKey: "unused");

        Assert.Equal(DescribeParameterIdentityKind.Shared, identity.Kind);
        Assert.Equal("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", identity.SharedGuid);
        Assert.Null(identity.ParameterTypeId);
    }

    [Fact]
    public void Local_identity_has_no_portable_fields()
    {
        var identity = ParameterIdentityClassifier.Classify(
            isBuiltIn: false,
            builtInTypeId: null,
            isShared: false,
            sharedGuid: Guid.Empty,
            localStableKey: "document-local-key");

        var contract = ParameterIdentityClassifier.ToContract(identity);
        Assert.Equal(DescribeParameterIdentityKind.Local, contract.Kind);
        Assert.Null(contract.ParameterTypeId);
        Assert.Null(contract.Guid);
        Assert.Equal("document-local-key", identity.StableKey);
    }

    [Fact]
    public void Describe_and_get_parameter_values_share_the_same_identity_helper()
    {
        var describe = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "RevitMCP.Addin", "Capabilities", "RevitDescribeParametersService.cs"));
        var values = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "RevitMCP.Addin", "Capabilities", "RevitGetParameterValuesService.cs"));
        var helper = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "RevitMCP.Addin", "Inspection", "RevitParameterIdentity.cs"));

        Assert.Contains("RevitParameterIdentity.Classify(", describe, StringComparison.Ordinal);
        Assert.Contains("RevitParameterIdentity.Classify(", values, StringComparison.Ordinal);
        Assert.Contains("ParameterUtils.IsBuiltInParameter", helper, StringComparison.Ordinal);
        Assert.DoesNotContain("LookupParameter", helper, StringComparison.Ordinal);
        Assert.DoesNotContain("ClassifyIdentity", describe, StringComparison.Ordinal);
        Assert.DoesNotContain("ParameterUtils.IsBuiltInParameter", describe, StringComparison.Ordinal);
        Assert.DoesNotContain("ParameterUtils.IsBuiltInParameter", values, StringComparison.Ordinal);
        Assert.DoesNotContain("LookupParameter", describe, StringComparison.Ordinal);
        Assert.DoesNotContain("LookupParameter", values, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
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
