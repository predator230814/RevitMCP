using Xunit;

namespace RevitMCP.Addin.Tests;

public sealed class LifecycleInfrastructureTests
{
    [Fact]
    public void Lifecycle_sources_do_not_create_transactions_or_cap0001()
    {
        var lifecycleDirectory = Path.Combine(FindRepoRoot(), "src", "RevitMCP.Addin", "Lifecycle");
        var sources = Directory.GetFiles(lifecycleDirectory, "*.cs", SearchOption.AllDirectories)
            .Append(Path.Combine(FindRepoRoot(), "src", "RevitMCP.Addin", "RevitMcpApplication.cs"))
            .ToArray();
        Assert.NotEmpty(sources);

        foreach (var source in sources)
        {
            var text = File.ReadAllText(source);
            Assert.DoesNotContain("new Transaction", text, StringComparison.Ordinal);
            Assert.DoesNotContain("new SubTransaction", text, StringComparison.Ordinal);
            Assert.DoesNotContain("new TransactionGroup", text, StringComparison.Ordinal);
            Assert.DoesNotContain("revit_get_context", text, StringComparison.Ordinal);
            Assert.DoesNotContain("GetContextRequest", text, StringComparison.Ordinal);
            Assert.DoesNotContain("GetContextResult", text, StringComparison.Ordinal);
        }
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

        throw new InvalidOperationException("Could not locate RevitMCP.sln from the test output directory.");
    }
}
