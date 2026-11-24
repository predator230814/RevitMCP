using System.Globalization;
using System.Reflection;

namespace BimIt.Revit.QueryRuntimeSpike;

public sealed record RevitApiIdentity(string Name, string Version, string PublicKeyToken, long Length)
{
    public static RevitApiIdentity HostProbe { get; } = new("host-probe", "0", "none", 0);

    public string Text => string.Join(
        '|',
        Name,
        Version,
        PublicKeyToken,
        Length.ToString(CultureInfo.InvariantCulture));

    public static RevitApiIdentity FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var name = AssemblyName.GetAssemblyName(path);
        var token = name.GetPublicKeyToken();
        var tokenText = token is null || token.Length == 0
            ? "none"
            : Convert.ToHexString(token).ToLowerInvariant();
        return new RevitApiIdentity(
            name.Name ?? "",
            name.Version?.ToString() ?? "",
            tokenText,
            new FileInfo(path).Length);
    }
}
