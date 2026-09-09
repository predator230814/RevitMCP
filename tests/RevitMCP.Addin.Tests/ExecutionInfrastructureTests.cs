using RevitMCP.Addin.Execution;
using Xunit;

namespace RevitMCP.Addin.Tests;

public sealed class ExecutionInfrastructureTests
{
    [Fact]
    public void Production_addin_output_does_not_contain_revit_api_dlls()
    {
        var directory = Path.GetDirectoryName(typeof(RevitExecutionQueue<>).Assembly.Location);
        Assert.False(string.IsNullOrWhiteSpace(directory));

        Assert.False(File.Exists(Path.Combine(directory!, "RevitAPI.dll")));
        Assert.False(File.Exists(Path.Combine(directory!, "RevitAPIUI.dll")));
    }

    [Fact]
    public void Execution_infrastructure_does_not_create_revit_transactions()
    {
        var executionDirectory = Path.Combine(FindRepoRoot(), "src", "RevitMCP.Addin", "Execution");
        var sources = Directory.GetFiles(executionDirectory, "*.cs", SearchOption.TopDirectoryOnly);
        Assert.NotEmpty(sources);

        foreach (var source in sources)
        {
            var text = File.ReadAllText(source);
            Assert.DoesNotContain("new Transaction", text, StringComparison.Ordinal);
            Assert.DoesNotContain("new SubTransaction", text, StringComparison.Ordinal);
            Assert.DoesNotContain("new TransactionGroup", text, StringComparison.Ordinal);
            Assert.DoesNotContain("StreamJsonRpc", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Queue_assembly_does_not_reference_streamjsonrpc_or_mcp()
    {
        var referenced = typeof(RevitExecutionQueue<>).Assembly
            .GetReferencedAssemblies()
            .Select(name => name.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(referenced, name =>
            name.Equals("StreamJsonRpc", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("StreamJsonRpc.", StringComparison.OrdinalIgnoreCase)
            || name.Equals("ModelContextProtocol", StringComparison.OrdinalIgnoreCase));
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
