using Xunit;

namespace BimIt.Revit.Server.Tests;

public sealed class ProjectReferenceTests
{
    [Fact]
    public void Server_and_addin_projects_do_not_reference_each_other()
    {
        var root = FindRepositoryRoot();
        var serverProject = File.ReadAllText(Path.Combine(root, "src", "BimIt.Revit.Server", "BimIt.Revit.Server.csproj"));
        var addinProject = File.ReadAllText(Path.Combine(root, "src", "BimIt.Revit.Addin", "BimIt.Revit.Addin.csproj"));

        Assert.DoesNotContain("BimIt.Revit.Addin", serverProject, StringComparison.Ordinal);
        Assert.DoesNotContain("BimIt.Revit.Server", addinProject, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
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