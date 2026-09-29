using RevitMCP.Addin.Inspection;
using RevitMCP.Bridge;
using RevitMCP.Contracts;
using Xunit;

namespace RevitMCP.Addin.Tests;

public sealed class PreviewParameterUpdatesRequestValidatorTests
{
    [Fact]
    public void Null_document_id_is_invalid()
    {
        AssertInvalid(Request(documentId: null!, [Update("el", "pr", StringValue("a"))]));
    }

    [Fact]
    public void Present_empty_or_whitespace_document_id_is_accepted()
    {
        PreviewParameterUpdatesRequestValidator.Validate(Request("", [Update("el", "pr", StringValue("a"))]));
        PreviewParameterUpdatesRequestValidator.Validate(Request(" ", [Update("el", "pr", StringValue("a"))]));
        PreviewParameterUpdatesRequestValidator.Validate(Request("\t", [Update("el", "pr", StringValue("a"))]));
    }

    [Fact]
    public void Update_count_is_bounded_from_one_to_twenty()
    {
        AssertInvalid(Request("doc", null!));
        AssertInvalid(Request("doc", []));
        AssertInvalid(Request("doc", Enumerable.Range(0, 21).Select(index => Update($"el-{index}", "pr", StringValue("a"))).ToArray()));
        PreviewParameterUpdatesRequestValidator.Validate(
            Request("doc", Enumerable.Range(0, 20).Select(index => Update($"el-{index}", "pr", StringValue("a"))).ToArray()));
    }

    [Fact]
    public void Null_update_refs_or_value_are_invalid()
    {
        AssertInvalid(Request("doc", [null!]));
        AssertInvalid(Request("doc", [new PreviewParameterUpdate { ElementRef = null!, ParameterRef = "pr", Value = StringValue("a") }]));
        AssertInvalid(Request("doc", [new PreviewParameterUpdate { ElementRef = "el", ParameterRef = null!, Value = StringValue("a") }]));
        AssertInvalid(Request("doc", [new PreviewParameterUpdate { ElementRef = "el", ParameterRef = "pr", Value = null! }]));
    }

    [Fact]
    public void Empty_and_whitespace_refs_are_preserved_as_valid_opaque_strings()
    {
        var request = Request(" ", [Update("", "\t", StringValue(""))]);
        PreviewParameterUpdatesRequestValidator.Validate(request);
        Assert.Equal(" ", request.DocumentId);
        Assert.Equal("", request.Updates[0].ElementRef);
        Assert.Equal("\t", request.Updates[0].ParameterRef);
    }

    [Fact]
    public void Ordinal_duplicate_pair_is_invalid()
    {
        AssertInvalid(Request("doc", [Update("el", "pr", StringValue("a")), Update("el", "pr", StringValue("b"))]));
    }

    [Fact]
    public void Pairs_differing_only_by_case_or_whitespace_remain_distinct()
    {
        PreviewParameterUpdatesRequestValidator.Validate(Request("doc", [Update("El", "Pr", StringValue("a")), Update("el", "pr", StringValue("b"))]));
        PreviewParameterUpdatesRequestValidator.Validate(Request("doc", [Update("el", "pr", StringValue("a")), Update("el ", "pr", StringValue("b"))]));
        PreviewParameterUpdatesRequestValidator.Validate(Request("doc", [Update("el", "pr-a", StringValue("a")), Update("el", "pr-b", IntegerValue(1))]));
    }

    [Fact]
    public void String_length_accepts_512_and_rejects_513()
    {
        PreviewParameterUpdatesRequestValidator.Validate(Request("doc", [Update("el", "pr", StringValue(new string('a', 512)))]));
        PreviewParameterUpdatesRequestValidator.Validate(Request("doc", [Update("el", "pr", StringValue(""))] ));
        AssertInvalid(Request("doc", [Update("el", "pr", StringValue(new string('a', 513)))]));
        AssertInvalid(Request("doc", [Update("el", "pr", new PreviewParameterStringValue { Value = null! })]));
    }

    [Fact]
    public void Quantity_must_be_finite_and_its_unit_must_be_non_empty()
    {
        PreviewParameterUpdatesRequestValidator.Validate(Request("doc", [Update("el", "pr", Quantity(-0.0, " "))]));
        PreviewParameterUpdatesRequestValidator.Validate(Request("doc", [Update("el", "pr", Quantity(1.5, "autodesk.unit.unit:feet-1.0.1"))]));
        AssertInvalid(Request("doc", [Update("el", "pr", Quantity(double.NaN, "unit"))]));
        AssertInvalid(Request("doc", [Update("el", "pr", Quantity(double.PositiveInfinity, "unit"))]));
        AssertInvalid(Request("doc", [Update("el", "pr", Quantity(double.NegativeInfinity, "unit"))]));
        AssertInvalid(Request("doc", [Update("el", "pr", Quantity(1.0, ""))]));
        AssertInvalid(Request("doc", [Update("el", "pr", new PreviewParameterQuantityValue { Value = 1.0, UnitTypeId = null! })]));
    }

    [Fact]
    public void Accepted_value_variants_are_string_integer_and_quantity()
    {
        PreviewParameterUpdatesRequestValidator.Validate(Request("doc",
        [
            Update("el-1", "pr-1", StringValue("")),
            Update("el-2", "pr-2", IntegerValue(int.MinValue)),
            Update("el-3", "pr-3", Quantity(-0.0, "autodesk.unit.unit:meters-1.0.0"))
        ]));
        AssertInvalid(Request("doc", [Update("el", "pr", new ForeignPreviewValue())]));
    }

    private static void AssertInvalid(PreviewParameterUpdatesRequest request)
    {
        var exception = Assert.Throws<BridgeException>(() => PreviewParameterUpdatesRequestValidator.Validate(request));
        Assert.Equal(CapabilityErrorCodes.InvalidParameterUpdatePreview, exception.ErrorCode);
        Assert.Equal("The parameter update preview request is invalid.", exception.Message);
    }

    private static PreviewParameterUpdatesRequest Request(string documentId, IReadOnlyList<PreviewParameterUpdate> updates)
    {
        return new PreviewParameterUpdatesRequest
        {
            DocumentId = documentId,
            Updates = updates
        };
    }

    private static PreviewParameterUpdate Update(string elementRef, string parameterRef, PreviewParameterValue value)
    {
        return new PreviewParameterUpdate
        {
            ElementRef = elementRef,
            ParameterRef = parameterRef,
            Value = value
        };
    }

    private static PreviewParameterStringValue StringValue(string value) => new() { Value = value };

    private static PreviewParameterIntegerValue IntegerValue(int value) => new() { Value = value };

    private static PreviewParameterQuantityValue Quantity(double value, string unitTypeId)
        => new() { Value = value, UnitTypeId = unitTypeId };

    private sealed class ForeignPreviewValue : PreviewParameterValue
    {
    }
}
