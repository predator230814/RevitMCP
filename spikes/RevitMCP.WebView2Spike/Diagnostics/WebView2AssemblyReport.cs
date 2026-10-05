using System.Reflection;
using RevitMCP.WebView2Spike.Messaging;

namespace RevitMCP.WebView2Spike.Diagnostics;

internal static class WebView2AssemblyReport
{
    public static IReadOnlyList<WebView2AssemblyInfo> LoadedAssemblies()
    {
        var results = new List<WebView2AssemblyInfo>();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var name = assembly.GetName();
            if (name.Name is null || !name.Name.StartsWith("Microsoft.Web.WebView2", StringComparison.Ordinal))
            {
                continue;
            }

            results.Add(new WebView2AssemblyInfo
            {
                Name = name.Name,
                Version = name.Version?.ToString() ?? "",
                Location = RedactUserProfile(SafeLocation(assembly)),
            });
        }

        results.Sort(static (left, right) => string.Compare(left.Name, right.Name, StringComparison.Ordinal));
        return results;
    }

    public static string VersionOf(string assemblyName)
    {
        foreach (var info in LoadedAssemblies())
        {
            if (string.Equals(info.Name, assemblyName, StringComparison.Ordinal))
            {
                return info.Version;
            }
        }

        return "";
    }

    private static string SafeLocation(Assembly assembly)
    {
        try
        {
            return assembly.IsDynamic ? "" : assembly.Location;
        }
        catch (NotSupportedException)
        {
            return "";
        }
    }

    internal static string RedactUserProfile(string location)
    {
        if (string.IsNullOrEmpty(location))
        {
            return "";
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (profile.Length > 0 && location.StartsWith(profile, StringComparison.OrdinalIgnoreCase))
        {
            return "%USERPROFILE%" + location[profile.Length..];
        }

        return location;
    }
}
