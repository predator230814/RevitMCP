using RevitMCP.Addin.Inspection;
using RevitMCP.Bridge;
using RevitMCP.Contracts;
using Xunit;

namespace RevitMCP.Addin.Tests;

public sealed class GetMepTopologyRequestValidatorTests
{
    [Fact]
    public void Omitted_bounds_and_domain_use_accepted_defaults()
    {
        var validated = GetMepTopologyRequestValidator.Validate(new GetMepTopologyRequest
        {
            DocumentId = "doc",
            SeedElementRefs = ["seed"]
        });

        Assert.Null(validated.Domain);
        Assert.Equal(3, validated.MaxDepth);
        Assert.Equal(100, validated.MaxElements);
        Assert.Equal(200, validated.MaxEdges);
    }

    [Theory]
    [InlineData(1, 10, 10)]
    [InlineData(10, 250, 500)]
    public void Closed_bound_ranges_are_accepted(int depth, int elements, int edges)
    {
        var validated = GetMepTopologyRequestValidator.Validate(new GetMepTopologyRequest
        {
            DocumentId = " ",
            SeedElementRefs = ["", "\t"],
            Domain = MepTopologyDomain.CableTrayConduit,
            MaxDepth = depth,
            MaxElements = elements,
            MaxEdges = edges
        });

        Assert.Equal(" ", validated.DocumentId);
        Assert.Equal(["", "\t"], validated.SeedElementRefs);
        Assert.Equal(MepTopologyDomain.CableTrayConduit, validated.Domain);
    }

    [Theory]
    [InlineData(0, 100, 200)]
    [InlineData(11, 100, 200)]
    [InlineData(3, 9, 200)]
    [InlineData(3, 251, 200)]
    [InlineData(3, 100, 9)]
    [InlineData(3, 100, 501)]
    public void Out_of_range_bounds_are_invalid(int depth, int elements, int edges)
    {
        var exception = Assert.Throws<BridgeException>(() => GetMepTopologyRequestValidator.Validate(new GetMepTopologyRequest
        {
            DocumentId = "doc",
            SeedElementRefs = ["seed"],
            MaxDepth = depth,
            MaxElements = elements,
            MaxEdges = edges
        }));

        Assert.Equal(CapabilityErrorCodes.InvalidMepTopology, exception.ErrorCode);
    }

    [Fact]
    public void Raw_seed_strings_are_not_trimmed_and_duplicates_are_invalid()
    {
        var distinct = GetMepTopologyRequestValidator.Validate(new GetMepTopologyRequest
        {
            DocumentId = "doc",
            SeedElementRefs = [" a", "a "]
        });
        Assert.Equal([" a", "a "], distinct.SeedElementRefs);

        Assert.Equal(
            CapabilityErrorCodes.InvalidMepTopology,
            Assert.Throws<BridgeException>(() => GetMepTopologyRequestValidator.Validate(new GetMepTopologyRequest
            {
                DocumentId = "doc",
                SeedElementRefs = ["a", "a"]
            })).ErrorCode);
        Assert.Equal(
            CapabilityErrorCodes.InvalidMepTopology,
            Assert.Throws<BridgeException>(() => GetMepTopologyRequestValidator.Validate(new GetMepTopologyRequest
            {
                DocumentId = null!,
                SeedElementRefs = ["seed"]
            })).ErrorCode);
    }
}
