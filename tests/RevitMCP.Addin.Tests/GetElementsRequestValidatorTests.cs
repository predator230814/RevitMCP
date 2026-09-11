using RevitMCP.Addin.Inspection;
using RevitMCP.Bridge;
using RevitMCP.Contracts;
using Xunit;

namespace RevitMCP.Addin.Tests;

public sealed class GetElementsRequestValidatorTests
{
    [Fact]
    public void Null_document_id_is_invalid()
    {
        AssertInvalid(Request(documentId: null!, ["ref-1"], FieldsOnly()));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Empty_or_whitespace_document_id_is_explicit_and_valid(string documentId)
    {
        GetElementsRequestValidator.Validate(Request(documentId, ["ref-1"], FieldsOnly()));
    }

    [Fact]
    public void Null_or_empty_or_too_many_refs_are_invalid()
    {
        AssertInvalid(Request("doc", null!, FieldsOnly()));
        AssertInvalid(Request("doc", [], FieldsOnly()));
        AssertInvalid(Request("doc", Enumerable.Range(0, 11).Select(index => $"ref-{index}").ToArray(), FieldsOnly()));
    }

    [Fact]
    public void Ten_refs_are_accepted()
    {
        GetElementsRequestValidator.Validate(
            Request("doc", Enumerable.Range(0, 10).Select(index => $"ref-{index}").ToArray(), FieldsOnly()));
    }

    [Fact]
    public void Null_ref_is_invalid()
    {
        AssertInvalid(Request("doc", [null!], FieldsOnly()));
    }

    [Fact]
    public void Ordinal_duplicate_ref_is_invalid()
    {
        AssertInvalid(Request("doc", ["ref-a", "ref-a"], FieldsOnly()));
    }

    [Fact]
    public void Refs_differing_only_by_case_remain_distinct()
    {
        GetElementsRequestValidator.Validate(Request("doc", ["Ref-A", "ref-a"], FieldsOnly()));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Empty_or_whitespace_ref_is_accepted(string elementRef)
    {
        GetElementsRequestValidator.Validate(Request("doc", [elementRef], FieldsOnly()));
    }

    [Fact]
    public void Null_projection_is_invalid()
    {
        AssertInvalid(Request("doc", ["ref-1"], null!));
    }

    [Fact]
    public void Projection_with_no_fields_and_no_parameter_names_is_invalid()
    {
        AssertInvalid(Request("doc", ["ref-1"], new GetElementsProjection()));
    }

    [Fact]
    public void Supplied_empty_fields_are_invalid_even_with_parameter_names()
    {
        AssertInvalid(Request(
            "doc",
            ["ref-1"],
            new GetElementsProjection
            {
                Fields = [],
                ParameterNames = ["Mark"]
            }));
    }

    [Fact]
    public void Supplied_empty_parameter_names_are_invalid_even_with_fields()
    {
        AssertInvalid(Request(
            "doc",
            ["ref-1"],
            new GetElementsProjection
            {
                Fields = [GetElementField.Name],
                ParameterNames = []
            }));
    }

    [Fact]
    public void Fields_only_projection_is_valid()
    {
        GetElementsRequestValidator.Validate(Request("doc", ["ref-1"], FieldsOnly()));
    }

    [Fact]
    public void Parameters_only_projection_is_valid()
    {
        GetElementsRequestValidator.Validate(
            Request("doc", ["ref-1"], new GetElementsProjection { ParameterNames = ["Mark"] }));
    }

    [Fact]
    public void Mixed_projection_is_valid()
    {
        GetElementsRequestValidator.Validate(
            Request(
                "doc",
                ["ref-1"],
                new GetElementsProjection
                {
                    Fields = [GetElementField.Name, GetElementField.LevelName],
                    ParameterNames = ["Mark", "Flow"]
                }));
    }

    [Fact]
    public void Undefined_field_enum_is_invalid()
    {
        AssertInvalid(Request(
            "doc",
            ["ref-1"],
            new GetElementsProjection { Fields = [(GetElementField)99] }));
    }

    [Fact]
    public void Duplicate_field_is_invalid()
    {
        AssertInvalid(Request(
            "doc",
            ["ref-1"],
            new GetElementsProjection { Fields = [GetElementField.Name, GetElementField.Name] }));
    }

    [Fact]
    public void More_than_ten_parameter_names_is_invalid()
    {
        var names = Enumerable.Range(1, 11).Select(index => $"Name{index}").ToArray();
        AssertInvalid(Request("doc", ["ref-1"], new GetElementsProjection { ParameterNames = names }));
    }

    [Fact]
    public void Empty_parameter_name_is_invalid()
    {
        AssertInvalid(Request("doc", ["ref-1"], new GetElementsProjection { ParameterNames = [""] }));
    }

    [Fact]
    public void Parameter_name_over_256_is_invalid()
    {
        AssertInvalid(Request(
            "doc",
            ["ref-1"],
            new GetElementsProjection { ParameterNames = [new string('A', 257)] }));
    }

    [Fact]
    public void Case_insensitive_duplicate_parameter_names_are_invalid()
    {
        AssertInvalid(Request(
            "doc",
            ["ref-1"],
            new GetElementsProjection { ParameterNames = ["Flow", "flow"] }));
    }

    [Fact]
    public void Whitespace_parameter_name_is_not_trimmed()
    {
        GetElementsRequestValidator.Validate(
            Request("doc", ["ref-1"], new GetElementsProjection { ParameterNames = [" "] }));
        GetElementsRequestValidator.Validate(
            Request("doc", ["ref-1"], new GetElementsProjection { ParameterNames = [" Mark "] }));
    }

    private static void AssertInvalid(GetElementsRequest request)
    {
        var exception = Assert.Throws<BridgeException>(() => GetElementsRequestValidator.Validate(request));
        Assert.Equal(CapabilityErrorCodes.InvalidInspection, exception.ErrorCode);
        Assert.Equal("The element inspection request is invalid.", exception.Message);
    }

    private static GetElementsRequest Request(
        string documentId,
        IReadOnlyList<string> elementRefs,
        GetElementsProjection projection)
    {
        return new GetElementsRequest
        {
            DocumentId = documentId,
            ElementRefs = elementRefs,
            Projection = projection
        };
    }

    private static GetElementsProjection FieldsOnly()
    {
        return new GetElementsProjection { Fields = [GetElementField.Name] };
    }
}
