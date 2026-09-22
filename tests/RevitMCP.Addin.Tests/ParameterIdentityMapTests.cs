using RevitMCP.Addin.Identity;
using RevitMCP.Addin.Inspection;
using RevitMCP.Contracts;
using Xunit;

namespace RevitMCP.Addin.Tests;

public sealed class ParameterIdentityMapTests
{
    [Fact]
    public void Same_source_and_identity_reuse_one_stable_ref()
    {
        var map = new ParameterIdentityMap();
        var identity = BuiltIn("autodesk.revit.parameter:allModelMark");

        var first = map.GetRef(GetElementParameterSource.Instance, identity);
        var second = map.GetRef(GetElementParameterSource.Instance, identity);

        Assert.Equal(first, second);
        Assert.True(map.TryResolve(first, out var binding));
        Assert.Equal(GetElementParameterSource.Instance, binding.Source);
        Assert.Equal(identity, binding.Identity);
    }

    [Fact]
    public void Source_is_part_of_the_forward_key()
    {
        var map = new ParameterIdentityMap();
        var identity = BuiltIn("autodesk.revit.parameter:allModelMark");

        var instance = map.GetRef(GetElementParameterSource.Instance, identity);
        var type = map.GetRef(GetElementParameterSource.Type, identity);

        Assert.NotEqual(instance, type);
        Assert.True(map.TryResolve(type, out var binding));
        Assert.Equal(GetElementParameterSource.Type, binding.Source);
    }

    [Fact]
    public void Distinct_identities_receive_distinct_refs()
    {
        var map = new ParameterIdentityMap();
        var first = map.GetRef(GetElementParameterSource.Instance, BuiltIn("autodesk.revit.parameter:a"));
        var second = map.GetRef(GetElementParameterSource.Instance, BuiltIn("autodesk.revit.parameter:b"));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Reverse_lookup_is_ordinal_and_unknown_strings_have_no_binding()
    {
        var map = new ParameterIdentityMap();
        var assigned = map.GetRef(GetElementParameterSource.Instance, BuiltIn("autodesk.revit.parameter:allModelMark"));

        Assert.False(map.TryResolve(assigned.ToUpperInvariant(), out _));
        Assert.False(map.TryResolve("not-a-minted-ref", out _));
        Assert.False(map.TryResolve(string.Empty, out _));
        Assert.False(map.TryResolve(" ", out _));
        Assert.True(map.TryResolve(assigned, out _));
    }

    [Fact]
    public void Local_and_shared_identities_round_trip_source_and_classified_data()
    {
        var map = new ParameterIdentityMap();
        var shared = ParameterIdentityClassifier.Classify(
            isBuiltIn: false,
            builtInTypeId: null,
            isShared: true,
            sharedGuid: Guid.Parse("AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE"),
            localStableKey: "unused");
        var local = ParameterIdentityClassifier.Classify(
            isBuiltIn: false,
            builtInTypeId: null,
            isShared: false,
            sharedGuid: Guid.Empty,
            localStableKey: "local:12");

        var sharedRef = map.GetRef(GetElementParameterSource.Type, shared);
        var localRef = map.GetRef(GetElementParameterSource.Instance, local);

        Assert.True(map.TryResolve(sharedRef, out var sharedBinding));
        Assert.Equal(DescribeParameterIdentityKind.Shared, sharedBinding.Identity.Kind);
        Assert.Equal("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", sharedBinding.Identity.SharedGuid);
        Assert.True(map.TryResolve(localRef, out var localBinding));
        Assert.Equal(DescribeParameterIdentityKind.Local, localBinding.Identity.Kind);
        Assert.Equal("local:12", localBinding.Identity.StableKey);
    }

    private static ClassifiedParameterIdentity BuiltIn(string typeId)
    {
        return ParameterIdentityClassifier.Classify(
            isBuiltIn: true,
            builtInTypeId: typeId,
            isShared: false,
            sharedGuid: Guid.Empty,
            localStableKey: "unused");
    }
}
