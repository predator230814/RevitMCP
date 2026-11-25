namespace BimIt.Revit.Compiler;

public static class CompilerHostLocation
{
    public static bool TryResolve(string productAssemblyLocation, out string executablePath)
    {
        executablePath = "";
        if (string.IsNullOrWhiteSpace(productAssemblyLocation) || !Path.IsPathRooted(productAssemblyLocation))
        {
            return false;
        }

        var productDirectory = Path.GetDirectoryName(Path.GetFullPath(productAssemblyLocation));
        if (string.IsNullOrEmpty(productDirectory))
        {
            return false;
        }

        var combined = Path.GetFullPath(Path.Combine(
            productDirectory,
            CompilerProtocol.RelativeDirectory,
            CompilerProtocol.ExecutableFileName));
        var rooted = productDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!combined.StartsWith(rooted, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        executablePath = combined;
        return true;
    }
}
