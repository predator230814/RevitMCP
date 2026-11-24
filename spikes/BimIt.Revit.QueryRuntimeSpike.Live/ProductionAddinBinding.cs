using System.Reflection;
using System.Runtime.CompilerServices;
using BimIt.Revit.QueryRuntimeSpike;

namespace BimIt.Revit.QueryRuntimeSpike.Live;

internal static class ProductionAddinBinding
{
    internal const string AddinName = "BimIt.Revit.Addin";

#pragma warning disable CA2255 // This command assembly is loaded by Revit. The resolver must run before any type touches the production Addin.
    [ModuleInitializer]
    internal static void Register()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
        {
            var requested = new AssemblyName(args.Name).Name;
            if (!string.Equals(requested, AddinName, StringComparison.Ordinal))
            {
                return null;
            }

            return LoadedAssemblySelection.SingleNamed(AppDomain.CurrentDomain.GetAssemblies(), AddinName);
        };
    }
#pragma warning restore CA2255
}
