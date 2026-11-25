using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;

namespace BimIt.Revit.CompilerHost;

internal static class CompilationReferences
{
    private static readonly string[] ReferenceNames =
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
        var pack = ReferencePack(revitApiPath);
        var references = new List<MetadataReference>();
        foreach (var name in ReferenceNames)
        {
            var path = Path.Combine(pack, name);
            if (File.Exists(path))
            {
                references.Add(MetadataReference.CreateFromFile(path));
            }
        }

        if (!references.Exists(reference => reference.Display?.EndsWith("System.Runtime.dll", StringComparison.OrdinalIgnoreCase) == true))
        {
            throw new ReferencePackUnavailableException();
        }

        references.Add(MetadataReference.CreateFromFile(revitApiPath));
        return references;
    }

    private static string ReferencePack(string revitApiPath)
    {
        var runtime = ReferencedRuntime(revitApiPath);
        var major = runtime.Major;
        var moniker = "net" + major.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".0";
        var root = DotNetRoot();
        var packRoot = Path.Combine(root, "packs", "Microsoft.NETCore.App.Ref");
        if (!Directory.Exists(packRoot))
        {
            throw new ReferencePackUnavailableException();
        }

        var match = Directory.GetDirectories(packRoot)
            .Select(path => new DirectoryInfo(path).Name)
            .Where(name => name.StartsWith(major.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".", StringComparison.Ordinal))
            .OrderByDescending(name => name, StringComparer.Ordinal)
            .Select(name => Path.Combine(packRoot, name, "ref", moniker))
            .FirstOrDefault(Directory.Exists);
        return match ?? throw new ReferencePackUnavailableException();
    }

    private static Version ReferencedRuntime(string revitApiPath)
    {
        using var stream = File.OpenRead(revitApiPath);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        foreach (var handle in reader.AssemblyReferences)
        {
            var reference = reader.GetAssemblyReference(handle);
            if (reader.GetString(reference.Name) == "System.Runtime")
            {
                return reference.Version;
            }
        }

        throw new ReferencePackUnavailableException();
    }

    private static string DotNetRoot()
    {
        var runtime = Path.GetDirectoryName(typeof(object).Assembly.Location)
            ?? throw new ReferencePackUnavailableException();
        var root = Directory.GetParent(runtime)?.Parent?.Parent?.FullName;
        return root ?? throw new ReferencePackUnavailableException();
    }
}

internal sealed class ReferencePackUnavailableException : Exception;
