using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;

namespace BimIt.Revit.QueryRuntimeSpike;

internal static class CompilationReferences
{
    private static readonly string[] ReferenceNames =
    [
        "System.Runtime.dll",
        "System.Linq.dll",
        "System.Collections.dll",
        "System.IO.FileSystem.dll",
        "System.Net.Http.dll",
        "System.Net.Primitives.dll",
        "System.Net.Sockets.dll",
        "System.Diagnostics.Process.dll",
        "System.Threading.dll",
        "System.Threading.Tasks.dll",
        "System.Threading.Tasks.Parallel.dll",
        "System.Runtime.InteropServices.dll",
        "System.Reflection.dll",
        "System.Reflection.Emit.dll",
        "System.Linq.Expressions.dll",
        "System.Runtime.Loader.dll",
        "System.Console.dll",
        "System.Runtime.Extensions.dll"
    ];

    private static readonly ConcurrentDictionary<string, IReadOnlyList<MetadataReference>> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<MetadataReference> Create(string? revitApiPath)
    {
        return Cache.GetOrAdd(revitApiPath ?? "", _ => Build(revitApiPath));
    }

    private static IReadOnlyList<MetadataReference> Build(string? revitApiPath)
    {
        var pack = ReferencePackDirectory();
        var references = new List<MetadataReference>(ReferenceNames.Length + 2)
        {
            MetadataReference.CreateFromFile(typeof(QueryBudget).Assembly.Location)
        };
        foreach (var name in ReferenceNames)
        {
            var path = Path.Combine(pack, name);
            if (File.Exists(path))
            {
                references.Add(MetadataReference.CreateFromFile(path));
            }
        }

        if (!references.Any(reference => reference.Display?.EndsWith("System.Runtime.dll", StringComparison.OrdinalIgnoreCase) == true))
        {
            throw new FileNotFoundException("System.Runtime reference assembly was not found.", pack);
        }

        if (revitApiPath is not null)
        {
            references.Add(MetadataReference.CreateFromFile(revitApiPath));
        }

        return references;
    }

    private static string ReferencePackDirectory()
    {
        var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)
            ?? throw new InvalidOperationException("The runtime directory was not found.");
        var version = new DirectoryInfo(runtimeDirectory).Name;
        var moniker = "net" + version.Split('.')[0] + ".0";
        var dotnetRoot = Directory.GetParent(runtimeDirectory)?.Parent?.Parent?.FullName
            ?? throw new InvalidOperationException("The dotnet root was not found.");
        var exact = Path.Combine(dotnetRoot, "packs", "Microsoft.NETCore.App.Ref", version, "ref", moniker);
        if (Directory.Exists(exact))
        {
            return exact;
        }

        var packRoot = Path.Combine(dotnetRoot, "packs", "Microsoft.NETCore.App.Ref");
        var majorPrefix = version.Split('.')[0] + ".";
        return Directory.GetDirectories(packRoot)
            .Where(path => new DirectoryInfo(path).Name.StartsWith(majorPrefix, StringComparison.Ordinal))
            .OrderByDescending(path => new DirectoryInfo(path).Name, StringComparer.Ordinal)
            .Select(path => Path.Combine(path, "ref", moniker))
            .First(Directory.Exists);
    }
}
