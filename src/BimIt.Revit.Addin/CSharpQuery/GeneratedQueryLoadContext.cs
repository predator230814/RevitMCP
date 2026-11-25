using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using Autodesk.Revit.DB;

namespace BimIt.Revit.Addin.CSharpQuery;

internal sealed class GeneratedQueryLoadContext : AssemblyLoadContext
{
    public GeneratedQueryLoadContext()
        : base(name: "BimIt.Revit.GeneratedQuery", isCollectible: true)
    {
    }

    public Assembly LoadGenerated(byte[] assemblyBytes)
    {
        return LoadFromStream(new MemoryStream(assemblyBytes, writable: false));
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (string.Equals(assemblyName.Name, "RevitAPI", StringComparison.Ordinal))
        {
            return typeof(Document).Assembly;
        }

        // Policy v1 emits against the packaged net10 reference assemblies. That set matches
        // Revit 2026.5, which hosts .NET 10. Framework names still bind to the assembly
        // already loaded in this process, so an earlier .NET 8 host does not get a private copy.
        foreach (var candidate in AssemblyLoadContext.Default.Assemblies)
        {
            if (string.Equals(candidate.GetName().Name, assemblyName.Name, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        if (!IsHostFrameworkName(assemblyName.Name))
        {
            return null;
        }

        try
        {
            return AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName(assemblyName.Name!));
        }
        catch (Exception ex) when (ex is FileNotFoundException or FileLoadException)
        {
            return null;
        }
    }

    private static bool IsHostFrameworkName(string? name)
    {
        return name is not null
            && (string.Equals(name, "System", StringComparison.Ordinal)
                || name.StartsWith("System.", StringComparison.Ordinal)
                || name.StartsWith("Microsoft.", StringComparison.Ordinal));
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        return IntPtr.Zero;
    }
}
