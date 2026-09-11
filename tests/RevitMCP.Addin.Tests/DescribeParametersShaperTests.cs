using RevitMCP.Addin.Inspection;
using RevitMCP.Contracts;
using Xunit;

namespace RevitMCP.Addin.Tests;

public sealed class DescribeParametersShaperTests
{
    [Fact]
    public void Missing_refs_are_not_found_and_do_not_contribute_descriptors()
    {
        var result = DescribeParametersShaper.Shape(
            Context(),
            ["ok-ref", "missing-ref"],
            new HashSet<string>(StringComparer.Ordinal) { "ok-ref" },
            [Occurrence("p-flow", "Flow", GetElementParameterSource.Instance, "ok-ref")],
            nameContains: null,
            limit: 50);

        Assert.Equal(GetElementResultStatus.Ok, result.Elements[0].Status);
        Assert.Equal(GetElementResultStatus.NotFound, result.Elements[1].Status);
        Assert.Equal("missing-ref", result.Elements[1].ElementRef);
        Assert.Equal(2, result.Elements.Count);
        Assert.Equal(1, result.MatchedCount);
        Assert.False(result.Truncated);
        Assert.Equal("p-flow", Assert.Single(result.Parameters).ParameterRef);
    }

    [Fact]
    public void Duplicate_names_with_distinct_refs_remain_separate()
    {
        var result = DescribeParametersShaper.Shape(
            Context(),
            ["ref-1"],
            new HashSet<string>(StringComparer.Ordinal) { "ref-1" },
            [
                Occurrence("p-mark-builtin", "Mark", GetElementParameterSource.Instance, "ref-1", DescribeParameterIdentityKind.BuiltIn, "autodesk.revit.parameter:mark"),
                Occurrence("p-mark-shared", "Mark", GetElementParameterSource.Instance, "ref-1", DescribeParameterIdentityKind.Shared, guid: "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")
            ],
            nameContains: null,
            limit: 50);

        Assert.Equal(2, result.Parameters.Count);
        Assert.Equal("p-mark-builtin", result.Parameters[0].ParameterRef);
        Assert.Equal("p-mark-shared", result.Parameters[1].ParameterRef);
        Assert.All(result.Parameters, descriptor => Assert.Equal("Mark", descriptor.Name));
    }

    [Fact]
    public void Aggregation_counts_elements_once_per_definition_and_source()
    {
        var result = DescribeParametersShaper.Shape(
            Context(),
            ["a", "b"],
            new HashSet<string>(StringComparer.Ordinal) { "a", "b" },
            [
                Occurrence("p-flow", "Flow", GetElementParameterSource.Instance, "a", isReadOnly: false),
                Occurrence("p-flow", "Flow", GetElementParameterSource.Instance, "a", isReadOnly: false),
                Occurrence("p-flow", "Flow", GetElementParameterSource.Instance, "b", isReadOnly: true),
                Occurrence("p-flow-type", "Flow", GetElementParameterSource.Type, "a", isReadOnly: false)
            ],
            nameContains: null,
            limit: 50);

        var instance = Assert.Single(result.Parameters, descriptor => descriptor.ParameterRef == "p-flow");
        Assert.Equal(2, instance.PresentOnCount);
        Assert.Equal(1, instance.ReadOnlyOnCount);
        var type = Assert.Single(result.Parameters, descriptor => descriptor.ParameterRef == "p-flow-type");
        Assert.Equal(1, type.PresentOnCount);
        Assert.Equal(0, type.ReadOnlyOnCount);
    }

    [Fact]
    public void Name_contains_is_ordinal_ignore_case_and_does_not_trim()
    {
        var occurrences = new[]
        {
            Occurrence("p-flow", "Flow", GetElementParameterSource.Instance, "ref-1"),
            Occurrence("p-mark", "Mark", GetElementParameterSource.Instance, "ref-1"),
            Occurrence("p-space-flow", " Flow", GetElementParameterSource.Instance, "ref-1")
        };

        var filtered = DescribeParametersShaper.Shape(
            Context(),
            ["ref-1"],
            new HashSet<string>(StringComparer.Ordinal) { "ref-1" },
            occurrences,
            nameContains: "flow",
            limit: 50);
        Assert.Equal(2, filtered.MatchedCount);
        Assert.DoesNotContain(filtered.Parameters, descriptor => descriptor.Name == "Mark");

        var spaced = DescribeParametersShaper.Shape(
            Context(),
            ["ref-1"],
            new HashSet<string>(StringComparer.Ordinal) { "ref-1" },
            occurrences,
            nameContains: " Flow",
            limit: 50);
        Assert.Equal("p-space-flow", Assert.Single(spaced.Parameters).ParameterRef);
    }

    [Fact]
    public void Matched_count_is_computed_before_limit_and_ordering_is_deterministic()
    {
        var occurrences = new[]
        {
            Occurrence("z-ref", "Type", GetElementParameterSource.Type, "ref-1"),
            Occurrence("a-ref", "name", GetElementParameterSource.Instance, "ref-1"),
            Occurrence("b-ref", "Name", GetElementParameterSource.Instance, "ref-1"),
            Occurrence("c-ref", "Name", GetElementParameterSource.Type, "ref-1")
        };

        var result = DescribeParametersShaper.Shape(
            Context(),
            ["ref-1"],
            new HashSet<string>(StringComparer.Ordinal) { "ref-1" },
            occurrences,
            nameContains: null,
            limit: 2);

        Assert.Equal(4, result.MatchedCount);
        Assert.True(result.Truncated);
        Assert.Equal(2, result.Parameters.Count);
        Assert.Equal("b-ref", result.Parameters[0].ParameterRef);
        Assert.Equal("c-ref", result.Parameters[1].ParameterRef);
    }

    private static DescribeParametersContext Context() =>
        new()
        {
            InstanceId = "instance-1",
            DocumentId = "document-1"
        };

    private static DescribeParameterOccurrence Occurrence(
        string parameterRef,
        string name,
        GetElementParameterSource source,
        string elementRef,
        DescribeParameterIdentityKind kind = DescribeParameterIdentityKind.Local,
        string? parameterTypeId = null,
        string? guid = null,
        bool isReadOnly = false)
    {
        return new DescribeParameterOccurrence
        {
            ParameterRef = parameterRef,
            Name = name,
            Source = source,
            Identity = new DescribeParameterIdentity
            {
                Kind = kind,
                ParameterTypeId = parameterTypeId,
                Guid = guid
            },
            DataType = new DescribeParameterDataType
            {
                Kind = DescribeParameterDataTypeKind.Unknown
            },
            ElementRef = elementRef,
            IsReadOnly = isReadOnly
        };
    }
}
