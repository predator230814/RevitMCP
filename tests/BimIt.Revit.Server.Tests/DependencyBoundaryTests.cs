using System.Reflection;
using System.Text;
using BimIt.Revit.Server;
using Xunit;

namespace BimIt.Revit.Server.Tests;

public sealed class DependencyBoundaryTests
{
    private static readonly string[] ForbiddenNames =
    [
        "RevitAPI",
        "RevitAPIUI",
        "BimIt.Revit.Addin",
        "Autodesk.Revit",
        "ModelContextProtocol.AspNetCore"
    ];

    [Fact]
    public void Server_has_no_autodesk_revit_api_or_addin_references()
    {
        var referenced = typeof(Program).Assembly
            .GetReferencedAssemblies()
            .Select(name => name.Name ?? string.Empty)
            .ToArray();

        foreach (var forbidden in ForbiddenNames)
        {
            var match = referenced.FirstOrDefault(name =>
                name.Equals(forbidden, StringComparison.OrdinalIgnoreCase)
                || name.StartsWith(forbidden + ".", StringComparison.OrdinalIgnoreCase));

            Assert.True(match is null, $"Assembly '{typeof(Program).Assembly.GetName().Name}' unexpectedly references '{match}'.");
        }

        Assert.Contains(referenced, name => name.Equals("ModelContextProtocol", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(referenced, name => name.Equals("BimIt.Revit.Bridge", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Server_project_references_mcp_sdk_but_not_aspnetcore()
    {
        var project = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "BimIt.Revit.Server", "BimIt.Revit.Server.csproj"));
        Assert.Contains("ModelContextProtocol", project, StringComparison.Ordinal);
        Assert.DoesNotContain("ModelContextProtocol.AspNetCore", project, StringComparison.Ordinal);
        Assert.DoesNotContain("RevitAPI", project, StringComparison.Ordinal);
    }

    [Fact]
    public void Server_assembly_has_no_workset_type_or_production_operation_descriptor()
    {
        var assembly = typeof(Program).Assembly;
        var types = LoadTypes(assembly);
        Assert.DoesNotContain(
            types,
            type => type.Name.Contains("Workset", StringComparison.Ordinal)
                || (type.FullName ?? string.Empty).Contains("Workset", StringComparison.Ordinal));

        var image = File.ReadAllBytes(assembly.Location);
        Assert.False(Contains(image, "project.user_worksets"));
        Assert.False(Contains(image, "Workset"));
        Assert.False(Contains(image, "FilteredWorksetCollector"));

        var sourceRoot = Path.Combine(FindRepositoryRoot(), "src", "BimIt.Revit.Server");
        foreach (var file in Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("project.user_worksets", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Workset", source, StringComparison.Ordinal);
            Assert.DoesNotContain("FilteredWorksetCollector", source, StringComparison.Ordinal);
        }
    }

    private static Type[] LoadTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.Where(type => type is not null).Cast<Type>().ToArray();
        }
    }

    private static bool Contains(byte[] haystack, string value)
    {
        return IndexOf(haystack, Encoding.UTF8.GetBytes(value)) >= 0
            || IndexOf(haystack, Encoding.Unicode.GetBytes(value)) >= 0;
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || haystack.Length < needle.Length)
        {
            return -1;
        }

        for (var start = 0; start <= haystack.Length - needle.Length; start++)
        {
            var matched = true;
            for (var offset = 0; offset < needle.Length; offset++)
            {
                if (haystack[start + offset] != needle[offset])
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
            {
                return start;
            }
        }

        return -1;
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
