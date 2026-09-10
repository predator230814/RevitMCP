using RevitMCP.Addin.Query;
using RevitMCP.Contracts;
using Xunit;

namespace RevitMCP.Addin.Tests;

public sealed class QueryElementMatcherTests
{
    [Fact]
    public void Category_list_uses_or()
    {
        var candidate = Candidate(category: "Air Terminals");
        var filters = new QueryElementFilters
        {
            CategoryNames = ["Mechanical Equipment", "Air Terminals"]
        };

        Assert.True(QueryElementMatcher.Matches(candidate, filters));
    }

    [Fact]
    public void Category_and_level_are_anded()
    {
        var candidate = Candidate(category: "Mechanical Equipment", level: "Level 2");
        var filters = new QueryElementFilters
        {
            CategoryNames = ["Mechanical Equipment"],
            LevelNames = ["Level 1"]
        };

        Assert.False(QueryElementMatcher.Matches(candidate, filters));
        Assert.True(QueryElementMatcher.Matches(
            candidate,
            new QueryElementFilters
            {
                CategoryNames = ["Mechanical Equipment"],
                LevelNames = ["Level 2"]
            }));
    }

    [Fact]
    public void Exact_filters_are_ordinal_case_insensitive()
    {
        var candidate = Candidate(category: "Mechanical Equipment", family: "VAV Box", type: "VAV-6in");
        Assert.True(QueryElementMatcher.Matches(
            candidate,
            new QueryElementFilters { CategoryNames = ["mechanical equipment"] }));
        Assert.True(QueryElementMatcher.Matches(
            candidate,
            new QueryElementFilters { FamilyNames = ["vav box"] }));
        Assert.True(QueryElementMatcher.Matches(
            candidate,
            new QueryElementFilters { TypeNames = ["vav-6IN"] }));
    }

    [Fact]
    public void Filters_are_not_trimmed()
    {
        var candidate = Candidate(category: "Mechanical Equipment");
        Assert.False(QueryElementMatcher.Matches(
            candidate,
            new QueryElementFilters { CategoryNames = [" Mechanical Equipment "] }));
        Assert.False(QueryElementMatcher.Matches(
            candidate,
            new QueryElementFilters { CategoryNames = ["Mechanical Equipment "] }));
    }

    [Fact]
    public void Text_substring_searches_element_family_and_type_names()
    {
        Assert.True(QueryElementMatcher.Matches(
            Candidate(name: "Supply VAV-1"),
            new QueryElementFilters { TextContains = "vav" }));
        Assert.True(QueryElementMatcher.Matches(
            Candidate(family: "VAV Box"),
            new QueryElementFilters { TextContains = "Box" }));
        Assert.True(QueryElementMatcher.Matches(
            Candidate(type: "VAV-6in"),
            new QueryElementFilters { TextContains = "6IN" }));
        Assert.False(QueryElementMatcher.Matches(
            Candidate(category: "VAV Category", level: "VAV Level"),
            new QueryElementFilters { TextContains = "VAV" }));
    }

    [Fact]
    public void Unavailable_family_type_or_level_cannot_match_those_filters()
    {
        var candidate = Candidate();
        Assert.False(QueryElementMatcher.Matches(candidate, new QueryElementFilters { FamilyNames = ["VAV Box"] }));
        Assert.False(QueryElementMatcher.Matches(candidate, new QueryElementFilters { TypeNames = ["VAV-6in"] }));
        Assert.False(QueryElementMatcher.Matches(candidate, new QueryElementFilters { LevelNames = ["Level 2"] }));
    }

    [Fact]
    public void Zero_match_is_a_failed_match_not_an_exception()
    {
        Assert.False(QueryElementMatcher.Matches(
            Candidate(category: "Doors"),
            new QueryElementFilters { CategoryNames = ["Windows"] }));
    }

    [Fact]
    public void Result_projection_orders_refs_ordinal_and_truncates_without_changing_count()
    {
        var refs = new[] { "c-ref", "a-ref", "b-ref", "d-ref" };
        var ordered = refs.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "a-ref", "b-ref", "c-ref", "d-ref" }, ordered);

        const int limit = 1;
        var limited = ordered.Take(limit).ToArray();
        Assert.Equal(4, ordered.Length);
        Assert.Equal(new[] { "a-ref" }, limited);
        Assert.True(ordered.Length > limited.Length);
    }

    private static QueryElementCandidate Candidate(
        string? name = null,
        string? category = null,
        string? family = null,
        string? type = null,
        string? level = null)
    {
        return new QueryElementCandidate
        {
            ElementRef = "opaque-ref",
            ElementName = name,
            CategoryName = category,
            FamilyName = family,
            TypeName = type,
            LevelName = level
        };
    }
}
