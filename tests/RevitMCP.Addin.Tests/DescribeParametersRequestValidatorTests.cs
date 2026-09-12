using RevitMCP.Addin.Inspection;
using RevitMCP.Bridge;
using RevitMCP.Contracts;
using Xunit;

namespace RevitMCP.Addin.Tests;

public sealed class DescribeParametersRequestValidatorTests
{
    [Fact]
    public void Null_document_id_is_invalid()
    {
        AssertInvalid(Request(documentId: null!, ["ref-1"]));
    }

    [Fact]
    public void Null_or_empty_or_too_many_refs_are_invalid()
    {
        AssertInvalid(Request("doc", null!));
        AssertInvalid(Request("doc", []));
        AssertInvalid(Request("doc", Enumerable.Range(0, 11).Select(index => $"ref-{index}").ToArray()));
    }

    [Fact]
    public void Ten_refs_are_accepted()
    {
        DescribeParametersRequestValidator.Validate(
            Request("doc", Enumerable.Range(0, 10).Select(index => $"ref-{index}").ToArray()));
    }

    [Fact]
    public void Ordinal_duplicate_ref_is_invalid()
    {
        AssertInvalid(Request("doc", ["ref-a", "ref-a"]));
    }

    [Fact]
    public void Refs_differing_only_by_case_remain_distinct()
    {
        DescribeParametersRequestValidator.Validate(Request("doc", ["Ref-A", "ref-a"]));
    }

    [Fact]
    public void Undefined_source_is_invalid()
    {
        AssertInvalid(Request("doc", ["ref-1"], source: (DescribeParameterSource)99));
    }

    [Fact]
    public void Empty_name_contains_is_invalid_and_whitespace_is_not_trimmed()
    {
        AssertInvalid(Request("doc", ["ref-1"], nameContains: string.Empty));
        DescribeParametersRequestValidator.Validate(Request("doc", ["ref-1"], nameContains: " "));
        DescribeParametersRequestValidator.Validate(Request("doc", ["ref-1"], nameContains: " Flow "));
    }

    [Fact]
    public void Name_contains_longer_than_256_is_invalid()
    {
        AssertInvalid(Request("doc", ["ref-1"], nameContains: new string('F', 257)));
        DescribeParametersRequestValidator.Validate(Request("doc", ["ref-1"], nameContains: new string('F', 256)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Limit_outside_1_to_100_is_invalid(int limit)
    {
        AssertInvalid(Request("doc", ["ref-1"], limit: limit));
    }

    [Fact]
    public void Default_constructed_request_uses_accepted_defaults()
    {
        var request = new DescribeParametersRequest
        {
            DocumentId = "doc",
            ElementRefs = ["ref-1"]
        };

        Assert.Equal(DescribeParameterSource.Both, request.Source);
        Assert.Equal(50, request.Limit);
        DescribeParametersRequestValidator.Validate(request);
    }

    private static void AssertInvalid(DescribeParametersRequest request)
    {
        var exception = Assert.Throws<BridgeException>(() => DescribeParametersRequestValidator.Validate(request));
        Assert.Equal(CapabilityErrorCodes.InvalidParameterDiscovery, exception.ErrorCode);
        Assert.Equal("The parameter discovery request is invalid.", exception.Message);
    }

    private static DescribeParametersRequest Request(
        string documentId,
        IReadOnlyList<string> elementRefs,
        DescribeParameterSource source = DescribeParameterSource.Both,
        string? nameContains = null,
        int limit = 50)
    {
        return new DescribeParametersRequest
        {
            DocumentId = documentId,
            ElementRefs = elementRefs,
            Source = source,
            NameContains = nameContains,
            Limit = limit
        };
    }
}
