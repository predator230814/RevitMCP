using BimIt.Revit.Addin.Query;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;
using Xunit;

namespace BimIt.Revit.Addin.Tests;

public sealed class QueryElementsRequestValidatorTests
{
    [Fact]
    public void Valid_limit_bounds_are_accepted()
    {
        QueryElementsRequestValidator.Validate(Request(QueryScope.Document, Filters("Mechanical Equipment"), 1));
        QueryElementsRequestValidator.Validate(Request(QueryScope.ActiveView, Filters("Mechanical Equipment"), 100));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Limit_outside_1_to_100_is_invalid(int limit)
    {
        var exception = Assert.Throws<BridgeException>(() =>
            QueryElementsRequestValidator.Validate(Request(QueryScope.Document, Filters("Mechanical Equipment"), limit)));
        Assert.Equal(CapabilityErrorCodes.InvalidQuery, exception.ErrorCode);
    }

    [Fact]
    public void Undefined_scope_is_invalid()
    {
        var exception = Assert.Throws<BridgeException>(() =>
            QueryElementsRequestValidator.Validate(Request((QueryScope)99, Filters("Mechanical Equipment"), 50)));
        Assert.Equal(CapabilityErrorCodes.InvalidQuery, exception.ErrorCode);
    }

    [Fact]
    public void Active_view_accepts_an_empty_filter_object()
    {
        QueryElementsRequestValidator.Validate(Request(QueryScope.ActiveView, new QueryElementFilters(), 25));
    }

    [Fact]
    public void Active_view_empty_array_is_still_invalid()
    {
        var exception = Assert.Throws<BridgeException>(() =>
            QueryElementsRequestValidator.Validate(
                Request(QueryScope.ActiveView, new QueryElementFilters { CategoryNames = [] }, 25)));
        Assert.Equal(CapabilityErrorCodes.InvalidQuery, exception.ErrorCode);
    }

    [Fact]
    public void All_null_filters_are_invalid()
    {
        var exception = Assert.Throws<BridgeException>(() =>
            QueryElementsRequestValidator.Validate(Request(QueryScope.Document, new QueryElementFilters(), 50)));
        Assert.Equal(CapabilityErrorCodes.InvalidQuery, exception.ErrorCode);
    }

    [Fact]
    public void Empty_array_is_invalid()
    {
        var exception = Assert.Throws<BridgeException>(() =>
            QueryElementsRequestValidator.Validate(
                Request(QueryScope.Document, new QueryElementFilters { CategoryNames = [] }, 50)));
        Assert.Equal(CapabilityErrorCodes.InvalidQuery, exception.ErrorCode);
    }

    [Fact]
    public void More_than_twenty_values_is_invalid()
    {
        var values = Enumerable.Range(1, 21).Select(index => $"Category {index}").ToArray();
        var exception = Assert.Throws<BridgeException>(() =>
            QueryElementsRequestValidator.Validate(
                Request(QueryScope.Document, new QueryElementFilters { CategoryNames = values }, 50)));
        Assert.Equal(CapabilityErrorCodes.InvalidQuery, exception.ErrorCode);
    }

    [Fact]
    public void Empty_string_entry_is_invalid()
    {
        var exception = Assert.Throws<BridgeException>(() =>
            QueryElementsRequestValidator.Validate(
                Request(QueryScope.Document, new QueryElementFilters { FamilyNames = [""] }, 50)));
        Assert.Equal(CapabilityErrorCodes.InvalidQuery, exception.ErrorCode);
    }

    [Fact]
    public void Entry_over_256_is_invalid()
    {
        var exception = Assert.Throws<BridgeException>(() =>
            QueryElementsRequestValidator.Validate(
                Request(QueryScope.Document, new QueryElementFilters { TypeNames = [new string('A', 257)] }, 50)));
        Assert.Equal(CapabilityErrorCodes.InvalidQuery, exception.ErrorCode);
    }

    [Fact]
    public void Null_array_item_is_invalid()
    {
        var exception = Assert.Throws<BridgeException>(() =>
            QueryElementsRequestValidator.Validate(
                Request(QueryScope.Document, new QueryElementFilters { LevelNames = [null!] }, 50)));
        Assert.Equal(CapabilityErrorCodes.InvalidQuery, exception.ErrorCode);
    }

    [Fact]
    public void Text_over_256_is_invalid()
    {
        var exception = Assert.Throws<BridgeException>(() =>
            QueryElementsRequestValidator.Validate(
                Request(QueryScope.Document, new QueryElementFilters { TextContains = new string('V', 257) }, 50)));
        Assert.Equal(CapabilityErrorCodes.InvalidQuery, exception.ErrorCode);
    }

    [Fact]
    public void Whitespace_filter_values_are_not_trimmed_or_rejected_for_length()
    {
        QueryElementsRequestValidator.Validate(
            Request(QueryScope.Document, new QueryElementFilters { CategoryNames = [" Mechanical Equipment "] }, 50));
        QueryElementsRequestValidator.Validate(
            Request(QueryScope.Document, new QueryElementFilters { TextContains = " VAV " }, 50));
    }

    private static QueryElementsRequest Request(QueryScope scope, QueryElementFilters filters, int limit)
    {
        return new QueryElementsRequest
        {
            Scope = scope,
            Filters = filters,
            Limit = limit
        };
    }

    private static QueryElementFilters Filters(string category) =>
        new() { CategoryNames = [category] };
}
