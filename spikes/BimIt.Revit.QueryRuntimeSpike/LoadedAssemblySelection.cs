using System.Reflection;

namespace BimIt.Revit.QueryRuntimeSpike;

/// <summary>
/// Selects an assembly that is already loaded. Zero or several matches fail closed.
/// </summary>
public static class LoadedAssemblySelection
{
    public static Assembly? SingleNamed(IEnumerable<Assembly> loaded, string simpleName)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        ArgumentException.ThrowIfNullOrWhiteSpace(simpleName);

        Assembly? match = null;
        foreach (var assembly in loaded)
        {
            if (!string.Equals(assembly.GetName().Name, simpleName, StringComparison.Ordinal))
            {
                continue;
            }

            if (match is not null)
            {
                return null;
            }

            match = assembly;
        }

        return match;
    }

    public static bool SharesDirectory(string? firstLocation, string? secondLocation)
    {
        var first = DirectoryOf(firstLocation);
        var second = DirectoryOf(secondLocation);
        if (first is null || second is null)
        {
            return false;
        }

        return string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
    }

    private static string? DirectoryOf(string? location)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            return null;
        }

        var directory = Path.GetDirectoryName(location);
        return directory is null ? null : Path.GetFullPath(directory);
    }
}
