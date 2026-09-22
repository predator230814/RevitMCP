using RevitMCP.Addin.Inspection;
using RevitMCP.Bridge;
using RevitMCP.Contracts;
using Xunit;

namespace RevitMCP.Addin.Tests;

public sealed class GetParameterValuesRequestValidatorTests
{
    [Fact]
    public void Null_document_id_is_invalid()
    {
        AssertInvalid(Request(documentId: null!, [Read("el", "pr")]));
    }

    [Fact]
    public void Present_empty_or_whitespace_document_id_is_accepted()
    {
        GetParameterValuesRequestValidator.Validate(Request("", [Read("el", "pr")]));
        GetParameterValuesRequestValidator.Validate(Request(" ", [Read("el", "pr")]));
        GetParameterValuesRequestValidator.Validate(Request("\t", [Read("el", "pr")]));
    }

    [Fact]
    public void Null_or_empty_or_too_many_reads_are_invalid()
    {
        AssertInvalid(Request("doc", null!));
        AssertInvalid(Request("doc", []));
        AssertInvalid(Request("doc", Enumerable.Range(0, 51).Select(index => Read($"el-{index}", "pr")).ToArray()));
    }

    [Fact]
    public void Fifty_reads_are_accepted()
    {
        GetParameterValuesRequestValidator.Validate(
            Request("doc", Enumerable.Range(0, 50).Select(index => Read($"el-{index}", "pr")).ToArray()));
    }

    [Fact]
    public void Null_pair_or_null_refs_are_invalid()
    {
        AssertInvalid(Request("doc", [null!]));
        AssertInvalid(Request("doc", [new GetParameterValueRead { ElementRef = null!, ParameterRef = "pr" }]));
        AssertInvalid(Request("doc", [new GetParameterValueRead { ElementRef = "el", ParameterRef = null! }]));
    }

    [Fact]
    public void Empty_and_whitespace_refs_are_accepted()
    {
        GetParameterValuesRequestValidator.Validate(Request("doc", [Read("", "")]));
        GetParameterValuesRequestValidator.Validate(Request("doc", [Read(" ", "\t")]));
    }

    [Fact]
    public void Ordinal_duplicate_pair_is_invalid()
    {
        AssertInvalid(Request("doc", [Read("el", "pr"), Read("el", "pr")]));
    }

    [Fact]
    public void Pairs_differing_only_by_case_remain_distinct()
    {
        GetParameterValuesRequestValidator.Validate(Request("doc", [Read("El", "Pr"), Read("el", "pr")]));
    }

    [Fact]
    public void Same_element_with_different_parameter_is_accepted()
    {
        GetParameterValuesRequestValidator.Validate(Request("doc", [Read("el", "pr-a"), Read("el", "pr-b")]));
    }

    [Fact]
    public void Whitespace_is_not_trimmed_for_uniqueness()
    {
        GetParameterValuesRequestValidator.Validate(Request("doc", [Read("el", "pr"), Read("el ", "pr")]));
        GetParameterValuesRequestValidator.Validate(Request("doc", [Read("el", "pr"), Read("el", " pr")]));
    }

    private static void AssertInvalid(GetParameterValuesRequest request)
    {
        var exception = Assert.Throws<BridgeException>(() => GetParameterValuesRequestValidator.Validate(request));
        Assert.Equal(CapabilityErrorCodes.InvalidParameterRead, exception.ErrorCode);
        Assert.Equal("The parameter value request is invalid.", exception.Message);
    }

    private static GetParameterValuesRequest Request(string documentId, IReadOnlyList<GetParameterValueRead> reads)
    {
        return new GetParameterValuesRequest
        {
            DocumentId = documentId,
            Reads = reads
        };
    }

    private static GetParameterValueRead Read(string elementRef, string parameterRef)
    {
        return new GetParameterValueRead
        {
            ElementRef = elementRef,
            ParameterRef = parameterRef
        };
    }
}
