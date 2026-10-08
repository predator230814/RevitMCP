using Xunit;

namespace RevitMCP.Addin.Tests;

public sealed class PreviewParameterUpdatesServiceStructureTests
{
    [Fact]
    public void Preview_service_keeps_revit_access_inside_execution_and_the_accepted_status_order()
    {
        var source = File.ReadAllText(Source("src", "RevitMCP.Addin", "Capabilities", "RevitPreviewParameterUpdatesService.cs"));
        var validate = source.IndexOf("PreviewParameterUpdatesRequestValidator.Validate(request)", StringComparison.Ordinal);
        var enqueue = source.IndexOf("_dispatcher.EnqueueAsync", StringComparison.Ordinal);
        var execute = source.IndexOf("private PreviewParameterUpdatesResult Execute", StringComparison.Ordinal);
        Assert.True(validate >= 0 && validate < enqueue);
        Assert.True(enqueue < execute);

        var statuses = new[]
        {
            "PreviewParameterUpdateStatus.ParameterRefNotFound",
            "PreviewParameterUpdateStatus.ElementNotFound",
            "PreviewParameterUpdateStatus.UnsupportedParameterSource",
            "PreviewParameterUpdateStatus.ParameterNotPresent",
            "PreviewParameterUpdateStatus.ParameterNotWritable",
            "PreviewParameterUpdateStatus.ValueTypeMismatch",
            "PreviewParameterUpdateStatus.InvalidUnit",
            "PreviewParameterUpdateStatus.UnsupportedValue",
            "PreviewParameterUpdateStatus.NoChange",
            "PreviewParameterUpdateStatus.Ok"
        };
        var cursor = execute;
        foreach (var status in statuses)
        {
            var next = source.IndexOf(status, cursor, StringComparison.Ordinal);
            Assert.True(next > cursor, status);
            cursor = next + status.Length;
        }

        Assert.Contains("element.GetOrderedParameters()", source, StringComparison.Ordinal);
        Assert.Contains("RevitParameterIdentity.Classify(parameter, document)", source, StringComparison.Ordinal);
        Assert.Contains("integerSpec.Equals(SpecTypeId.Int.Integer)", source, StringComparison.Ordinal);
        var eligibility = source.IndexOf(
            "PreviewParameterWriteEligibility.IsWritable(match.IsReadOnly, match.IsShared, match.UserModifiable)",
            StringComparison.Ordinal);
        var valueTypeMismatch = source.IndexOf("PreviewParameterUpdateStatus.ValueTypeMismatch", StringComparison.Ordinal);
        Assert.True(eligibility >= 0 && eligibility < valueTypeMismatch);
        Assert.Contains("match.IsReadOnly", source, StringComparison.Ordinal);
        Assert.Contains("match.IsShared", source, StringComparison.Ordinal);
        Assert.Contains("match.UserModifiable", source, StringComparison.Ordinal);
        Assert.DoesNotContain("match.IsReadOnly || !match.UserModifiable", source, StringComparison.Ordinal);
        Assert.Contains("document.IsFamilyDocument", source, StringComparison.Ordinal);
        Assert.Contains("document.IsReadOnly", source, StringComparison.Ordinal);
        Assert.Contains("UnitUtils.ConvertFromInternalUnits(parameter.AsDouble(), unit)", source, StringComparison.Ordinal);
        Assert.Contains("CapabilityErrorCodes.NoActiveDocument", source, StringComparison.Ordinal);
        Assert.Contains("CapabilityErrorCodes.DocumentContextChanged", source, StringComparison.Ordinal);
        Assert.Contains("CapabilityErrorCodes.UnsupportedDocumentKind", source, StringComparison.Ordinal);
        Assert.Contains("CapabilityErrorCodes.DocumentNotWritable", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LookupParameter", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IsModifiable", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Transaction", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetValueString", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Set(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Synchronize", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Save(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ConvertToInternalUnits", source, StringComparison.Ordinal);
        Assert.Equal(1, Count(source, "foreach (var update in request.Updates)"));
        Assert.True(source.IndexOf("foreach (var update in request.Updates)", StringComparison.Ordinal)
            < source.IndexOf("_intentStore.TryCreate", StringComparison.Ordinal));
    }

    [Fact]
    public void Lifetime_factory_passes_the_owned_intent_store_and_coordinator_composes_preview()
    {
        var adapters = File.ReadAllText(Source("src", "RevitMCP.Addin", "Lifecycle", "RevitLifecycleAdapters.cs"));
        var coordinator = File.ReadAllText(Source("src", "RevitMCP.Addin", "Lifecycle", "AddinLifecycleCoordinator.cs"));
        var protocol = File.ReadAllText(Source("src", "RevitMCP.Contracts", "BridgeProtocol.cs"));

        Assert.Contains(
            "new RevitPreviewParameterUpdatesService(_dispatcher, metadata, _identity, _parameterRefs, _intentStore)",
            adapters,
            StringComparison.Ordinal);
        Assert.Equal(1, Count(adapters, "new EphemeralWriteIntentStore()"));
        Assert.Equal(1, Count(adapters, "new OpenDocumentIdentityService()"));
        Assert.Contains("dispatcher.CreatePreviewParameterUpdates(metadata)", coordinator, StringComparison.Ordinal);
        Assert.Contains("public const int CurrentVersion = ApplyParameterUpdatesVersion", protocol, StringComparison.Ordinal);
        Assert.Contains("PreviewParameterUpdatesVersion = 8", protocol, StringComparison.Ordinal);
        Assert.Contains("RequestParameterUpdateReviewVersion = 9", protocol, StringComparison.Ordinal);
        Assert.Contains("ApplyParameterUpdatesVersion = 10", protocol, StringComparison.Ordinal);
        Assert.Contains("SupportsPreviewParameterUpdates", protocol, StringComparison.Ordinal);
        Assert.Contains("SupportsApplyParameterUpdates", protocol, StringComparison.Ordinal);
    }

    private static string Source(params string[] parts)
        => Path.Combine(new[] { FindRepoRoot() }.Concat(parts).ToArray());

    private static int Count(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "RevitMCP.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
