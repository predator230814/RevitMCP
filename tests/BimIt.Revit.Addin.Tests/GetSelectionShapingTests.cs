using BimIt.Revit.Addin.Inspection;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;
using Xunit;

namespace BimIt.Revit.Addin.Tests;

public sealed class GetSelectionShapingTests
{
    [Fact]
    public void Omitted_limit_defaults_to_25_and_bounds_are_closed()
    {
        var omitted = GetSelectionRequestValidator.Validate(new GetSelectionRequest());
        var minimum = GetSelectionRequestValidator.Validate(new GetSelectionRequest { Limit = 1 });
        var maximum = GetSelectionRequestValidator.Validate(new GetSelectionRequest { Limit = 100 });

        Assert.Equal(25, omitted.Limit);
        Assert.Null(omitted.DocumentId);
        Assert.Equal(1, minimum.Limit);
        Assert.Equal(100, maximum.Limit);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Out_of_range_limit_is_invalid_selection(int limit)
    {
        var exception = Assert.Throws<BridgeException>(() =>
            GetSelectionRequestValidator.Validate(new GetSelectionRequest { Limit = limit }));

        Assert.Equal(CapabilityErrorCodes.InvalidSelection, exception.ErrorCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("not-a-guid")]
    public void Present_document_id_stays_raw(string documentId)
    {
        var validated = GetSelectionRequestValidator.Validate(new GetSelectionRequest
        {
            DocumentId = documentId
        });

        Assert.Equal(documentId, validated.DocumentId);
    }

    [Fact]
    public void Shaping_is_ordinal_unique_sorted_and_counts_before_limit()
    {
        var shaped = GetSelectionShaper.Shape(
            ["m-ref", "a-ref", "m-ref", "b-ref", "A-ref"],
            limit: 3);

        Assert.Equal(4, shaped.SelectedCount);
        Assert.Equal(["A-ref", "a-ref", "b-ref"], shaped.ElementRefs);
        Assert.True(shaped.Truncated);
    }

    [Fact]
    public void Shaping_does_not_depend_on_input_order()
    {
        var forward = GetSelectionShaper.Shape(["z", "a", "m"], 25);
        var reverse = GetSelectionShaper.Shape(["m", "z", "a"], 25);

        Assert.Equal(forward.ElementRefs, reverse.ElementRefs);
        Assert.Equal(3, forward.SelectedCount);
        Assert.False(forward.Truncated);
    }

    [Fact]
    public void Empty_resolved_selection_is_not_truncated()
    {
        var shaped = GetSelectionShaper.Shape([], 25);

        Assert.Equal(0, shaped.SelectedCount);
        Assert.Empty(shaped.ElementRefs);
        Assert.False(shaped.Truncated);
    }

    [Fact]
    public void Selection_service_reads_get_element_ids_without_element_type_exclusion()
    {
        var text = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "src",
            "BimIt.Revit.Addin",
            "Capabilities",
            "RevitGetSelectionService.cs"));

        Assert.Contains("uiDocument.Selection.GetElementIds()", text, StringComparison.Ordinal);
        Assert.Contains("document.GetElement(id)", text, StringComparison.Ordinal);
        Assert.Contains("element?.UniqueId", text, StringComparison.Ordinal);
        Assert.DoesNotContain("GetReferences", text, StringComparison.Ordinal);
        Assert.DoesNotContain("SetElementIds", text, StringComparison.Ordinal);
        Assert.DoesNotContain("PickObject", text, StringComparison.Ordinal);
        Assert.DoesNotContain("WhereElementIsNotElementType", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ElementType", text, StringComparison.Ordinal);
        Assert.DoesNotContain("new Transaction", text, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BimIt.Revit.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate BimIt.Revit.sln from the test output directory.");
    }
}
