using Xunit;

namespace RevitMCP.Server.Tests;

public sealed class ProjectReferenceTests
{
    [Fact]
    public void Server_and_addin_projects_do_not_reference_each_other()
    {
        var root = FindRepositoryRoot();
        var serverProject = File.ReadAllText(Path.Combine(root, "src", "RevitMCP.Server", "RevitMCP.Server.csproj"));
        var addinProject = File.ReadAllText(Path.Combine(root, "src", "RevitMCP.Addin", "RevitMCP.Addin.csproj"));

        Assert.DoesNotContain("RevitMCP.Addin", serverProject, StringComparison.Ordinal);
        Assert.DoesNotContain("RevitMCP.Server", addinProject, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
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