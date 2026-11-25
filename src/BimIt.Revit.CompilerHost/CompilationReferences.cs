using Microsoft.CodeAnalysis;

namespace BimIt.Revit.CompilerHost;

internal static class CompilationReferences
{
    internal const string RelativeDirectory = "refs";
    internal const string Moniker = "net10.0";

    private static readonly string[] RequiredNames =
    [
        "System.Runtime.dll",
        "System.Linq.dll",
        "System.Collections.dll",
        "System.Console.dll",
        "System.Diagnostics.Process.dll",
        "System.IO.FileSystem.dll",
        "System.Linq.Expressions.dll",
        "System.Net.Http.dll",
        "System.Net.Primitives.dll",
        "System.Reflection.dll",
        "System.Reflection.Emit.dll",
        "System.Runtime.Extensions.dll",
        "System.Runtime.InteropServices.dll",
        "System.Runtime.Loader.dll",
        "System.Threading.dll",
        "System.Threading.Tasks.dll",
        "System.Threading.Tasks.Parallel.dll"
    ];

    public static IReadOnlyList<MetadataReference> Create(string revitApiPath)
    {
        return Create(revitApiPath, PackagedDirectory());
    }

    internal static IReadOnlyList<MetadataReference> Create(string revitApiPath, string referenceDirectory)
    {
        if (string.IsNullOrWhiteSpace(referenceDirectory))
        {
            throw new ReferencePackUnavailableException();
        }

        var root = Path.GetFullPath(referenceDirectory);
        if (!Directory.Exists(root))
        {
            throw new ReferencePackUnavailableException();
        }

        var references = new List<MetadataReference>(RequiredNames.Length + 1);
        var included = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in RequiredNames)
        {
            var path = Path.Combine(root, name);
            if (!File.Exists(path))
            {
                throw new ReferencePackUnavailableException();
            }

            references.Add(MetadataReference.CreateFromFile(path));
            included.Add(name);
        }

        foreach (var path in Directory.GetFiles(root, "*.dll").OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal))
        {
            var name = Path.GetFileName(path);
            if (!included.Add(name))
            {
                continue;
            }

            references.Add(MetadataReference.CreateFromFile(path));
        }

        references.Add(MetadataReference.CreateFromFile(revitApiPath));
        return references;
    }

    internal static string PackagedDirectory()
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(CompilationReferences).Assembly.Location);
        if (string.IsNullOrWhiteSpace(assemblyDirectory))
        {
            throw new ReferencePackUnavailableException();
        }

        return Path.Combine(assemblyDirectory, RelativeDirectory, Moniker);
    }
}

internal sealed class ReferencePackUnavailableException : Exception;
