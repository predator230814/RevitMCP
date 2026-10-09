using RevitMCP.Addin.Inspection;
using RevitMCP.Addin.Warnings;
using RevitMCP.Bridge;
using RevitMCP.Contracts;
using Xunit;

namespace RevitMCP.Addin.Tests;

public sealed class WarningProjectionTests
{
    [Fact]
    public void Empty_document_returns_zero_counts_and_omits_unmatched_refs()
    {
        var result = WarningProjection.Project("instance", "document", Request(), []);

        Assert.Equal(0, result.MatchedCount);
        Assert.Equal(0, result.CountsBySeverity.Warning + result.CountsBySeverity.Error + result.CountsBySeverity.DocumentCorruption + result.CountsBySeverity.Other);
        Assert.Empty(result.Definitions);
        Assert.Empty(result.Warnings);
        Assert.Null(result.UnmatchedElementRefs);
        Assert.False(result.Truncated);
        Assert.Empty(result.TruncationReasons);
    }

    [Fact]
    public void Order_is_independent_of_source_order_and_failing_wins_over_additional()
    {
        var result = WarningProjection.Project(
            "instance",
            "document",
            Request(),
            [
                Message("b", WarningSeverity.Warning, "second", failing: ["z"], additional: ["a"]),
                Message("a", WarningSeverity.Error, "first", failing: ["m"], additional: ["m"], unresolved: 2)
            ]);

        Assert.Equal(["a", "b"], result.Warnings.Select(item => item.FailureKey).ToArray());
        Assert.Equal(WarningElementRole.Failing, Assert.Single(result.Warnings[0].Elements).Role);
        Assert.Equal("m", result.Warnings[0].Elements[0].ElementRef);
        Assert.Equal(2, result.Warnings[0].UnresolvedElementCount);
    }

    [Fact]
    public void Element_filter_keeps_requested_refs_first_and_reports_unmatched_before_the_warning_cap()
    {
        var request = Request(elementRefs: ["missing", "kept"], maxWarnings: 1, maxElements: 1);
        var result = WarningProjection.Project(
            "instance",
            "document",
            request,
            [
                Message("late", WarningSeverity.Warning, "late", failing: ["kept", "extra"]),
                Message("early", WarningSeverity.DocumentCorruption, "early", failing: ["other"])
            ]);

        Assert.Equal(1, result.MatchedCount);
        Assert.Equal(["missing"], result.UnmatchedElementRefs);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal("late", warning.FailureKey);
        Assert.Equal("kept", Assert.Single(warning.Elements).ElementRef);
        Assert.True(warning.ElementsTruncated);
        Assert.Equal([WarningTruncationReason.Elements], result.TruncationReasons);
    }

    [Fact]
    public void Description_and_definition_caps_are_explicit()
    {
        var messages = Enumerable.Range(0, 51)
            .Select(index => Message("k" + index.ToString("00"), WarningSeverity.Warning, new string('x', 513), failing: []))
            .ToArray();
        var result = WarningProjection.Project("instance", "document", Request(maxWarnings: 1), messages);

        Assert.Equal(51, result.MatchedCount);
        Assert.Equal(50, result.Definitions.Count);
        Assert.Equal(513, messages[0].Description.Length);
        Assert.Equal(512, result.Warnings[0].DescriptionText.Length);
        Assert.True(result.Warnings[0].DescriptionTruncated);
        Assert.Contains(WarningTruncationReason.Definitions, result.TruncationReasons);
        Assert.Contains(WarningTruncationReason.Warnings, result.TruncationReasons);
        Assert.DoesNotContain(WarningTruncationReason.Elements, result.TruncationReasons);
    }

    [Fact]
    public void Empty_failure_key_is_an_exact_filter()
    {
        var result = WarningProjection.Project(
            "instance",
            "document",
            Request(failureKey: ""),
            [
                Message("", WarningSeverity.Other, "blank"),
                Message("named", WarningSeverity.Warning, "named")
            ]);

        Assert.Equal(1, result.MatchedCount);
        Assert.Equal("", Assert.Single(result.Warnings).FailureKey);
        Assert.Equal(1, result.CountsBySeverity.Other);
    }

    private static ValidatedGetWarningsRequest Request(
        IReadOnlyList<string>? elementRefs = null,
        int maxWarnings = 25,
        int maxElements = 10,
        string? failureKey = null)
    {
        return new ValidatedGetWarningsRequest("document", null, failureKey, elementRefs, maxWarnings, maxElements);
    }

    private static SourceWarning Message(
        string key,
        WarningSeverity severity,
        string description,
        IReadOnlyList<string>? failing = null,
        IReadOnlyList<string>? additional = null,
        int unresolved = 0)
    {
        return new SourceWarning
        {
            FailureKey = key,
            Severity = severity,
            Description = description,
            HasResolutions = false,
            FailingRefs = failing ?? [],
            AdditionalRefs = additional ?? [],
            UnresolvedElementCount = unresolved
        };
    }
}

public sealed class GetWarningsRequestValidatorTests
{
    [Fact]
    public void Null_document_id_is_invalid_and_blank_document_id_is_preserved()
    {
        var invalid = Assert.Throws<BridgeException>(() =>
            GetWarningsRequestValidator.Validate(new GetWarningsRequest { DocumentId = null! }));
        Assert.Equal(CapabilityErrorCodes.InvalidWarnings, invalid.ErrorCode);

        var blank = GetWarningsRequestValidator.Validate(new GetWarningsRequest { DocumentId = " " });
        Assert.Equal(" ", blank.DocumentId);
        Assert.Equal(25, blank.MaxWarnings);
        Assert.Equal(10, blank.MaxElementsPerWarning);
    }

    [Fact]
    public void Out_of_range_bounds_and_duplicate_refs_are_invalid()
    {
        Assert.Throws<BridgeException>(() =>
            GetWarningsRequestValidator.Validate(new GetWarningsRequest { DocumentId = "doc", MaxWarnings = 0 }));
        Assert.Throws<BridgeException>(() =>
            GetWarningsRequestValidator.Validate(new GetWarningsRequest
            {
                DocumentId = "doc",
                ElementRefs = ["same", "same"]
            }));
    }
}
