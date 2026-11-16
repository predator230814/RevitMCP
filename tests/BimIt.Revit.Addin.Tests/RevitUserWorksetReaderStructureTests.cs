using BimIt.Revit.Contracts;
using Xunit;

namespace BimIt.Revit.Addin.Tests;

public sealed class RevitUserWorksetReaderStructureTests
{
    [Fact]
    public void The_reader_uses_only_user_worksets_on_the_host_and_skips_a_non_workshared_document()
    {
        var reader = Source("ReadOperations", "RevitUserWorksetReader.cs");
        var guard = reader.IndexOf("if (!document.IsWorkshared)", StringComparison.Ordinal);
        var collector = reader.IndexOf("new FilteredWorksetCollector(document)", StringComparison.Ordinal);
        Assert.True(guard >= 0 && collector > guard);
        Assert.Contains("return new UserWorksetInventory(false, []);", reader, StringComparison.Ordinal);
        Assert.Equal(1, Count(reader, "OfKind(WorksetKind.UserWorkset)"));
        Assert.Contains("workset.Name ?? string.Empty", reader, StringComparison.Ordinal);
        Assert.Contains("workset.IsDefaultWorkset", reader, StringComparison.Ordinal);
        Assert.Contains("workset.IsVisibleByDefault", reader, StringComparison.Ordinal);
        Assert.DoesNotContain("GetLinkDocument", reader, StringComparison.Ordinal);
        Assert.DoesNotContain("RevitLinkInstance", reader, StringComparison.Ordinal);
        Assert.DoesNotContain("WorksetId", reader, StringComparison.Ordinal);
        Assert.DoesNotContain("IsEditable", reader, StringComparison.Ordinal);
        Assert.DoesNotContain("IsOpen", reader, StringComparison.Ordinal);
        Assert.DoesNotContain(".Owner", reader, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_operation_code_has_no_transaction_or_workset_mutation_and_composes_one_operation()
    {
        var root = FindRepoRoot();
        var files = Directory.GetFiles(Path.Combine(root, "src", "BimIt.Revit.Addin", "ReadOperations"), "*.cs");
        var combined = string.Join("\n", files.Select(File.ReadAllText)).Replace("\r\n", "\n", StringComparison.Ordinal);
        foreach (var forbidden in new[]
        {
            "Transaction",
            "SubTransaction",
            "TransactionGroup",
            "SetActiveWorksetId",
            "GetLinkDocument",
            "RevitLinkInstance"
        })
        {
            Assert.DoesNotContain(forbidden, combined, StringComparison.Ordinal);
        }

        var lifecycle = File.ReadAllText(Path.Combine(root, "src", "BimIt.Revit.Addin", "Lifecycle", "RevitLifecycleAdapters.cs"));
        Assert.Contains("ReadOperationComposition.TryCreate", lifecycle, StringComparison.Ordinal);
        Assert.Contains("ReadOperationComposition.ProductionOperations()", lifecycle, StringComparison.Ordinal);
        Assert.Contains("new RevitReadOperationWorkQueue(_dispatcher, _identity)", lifecycle, StringComparison.Ordinal);
        Assert.DoesNotContain("Assembly.GetTypes", lifecycle, StringComparison.Ordinal);
        Assert.Equal(24, BridgeProtocol.CurrentVersion);

        foreach (var file in new[]
        {
            "IRevitReadOperation.cs",
            "IExecutableReadOperation.cs",
            "RevitReadOperation.cs",
            "ReadOperationRegistry.cs",
            "ReadOperationPipeline.cs",
            "ReadOperationResults.cs",
            "RevitReadOperationService.cs"
        })
        {
            var source = Source("ReadOperations", file);
            Assert.DoesNotContain("Workset", source, StringComparison.Ordinal);
            Assert.DoesNotContain("workset", source, StringComparison.Ordinal);
        }

        var lookup = Source("ReadOperations", "RevitLookupReadOperationsService.cs");
        Assert.DoesNotContain("EnqueueAsync", lookup, StringComparison.Ordinal);
        Assert.DoesNotContain("Autodesk.Revit", lookup, StringComparison.Ordinal);
        Assert.DoesNotContain("Workset", lookup, StringComparison.Ordinal);

        var pipeline = Source("ReadOperations", "ReadOperationPipeline.cs");
        var familyGuard = pipeline.IndexOf("document.IsFamilyDocument", StringComparison.Ordinal);
        var hostHandoff = pipeline.IndexOf("Host = document", StringComparison.Ordinal);
        Assert.True(familyGuard >= 0 && hostHandoff > familyGuard);
        Assert.DoesNotContain("as IExecutableReadOperation", pipeline, StringComparison.Ordinal);
        Assert.DoesNotContain("Autodesk.Revit.DB", pipeline, StringComparison.Ordinal);

        var operation = Source("ReadOperations", "ProjectUserWorksetsReadOperation.cs");
        Assert.Contains("_reader.Read(active.Document)", operation, StringComparison.Ordinal);
        Assert.Contains("context.Host is not RevitActiveReadDocument active", operation, StringComparison.Ordinal);
        var registry = Source("ReadOperations", "ReadOperationRegistry.cs");
        Assert.Contains("IReadOnlyList<IExecutableReadOperation>", registry, StringComparison.Ordinal);
        Assert.DoesNotContain("IReadOnlyList<IRevitReadOperation>", registry, StringComparison.Ordinal);
    }

    private static string Source(string folder, string file)
    {
        return File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "BimIt.Revit.Addin", folder, file)).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

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
            if (File.Exists(Path.Combine(directory.FullName, "BimIt.Revit.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate BimIt.Revit.sln from the test output directory.");
    }
}
