using System.Globalization;
using System.Reflection;

namespace BimIt.Revit.Compiler;

public sealed record RevitApiIdentity(string Name, string Version, string PublicKeyToken, long Length)
{
    public bool IsSupportedApiIdentity =>
        string.Equals(Name, CompilerProtocol.SupportedApiName, StringComparison.Ordinal)
        && string.Equals(Version, CompilerProtocol.SupportedApiVersion, StringComparison.Ordinal);

    public bool Matches(RevitApiIdentity other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return string.Equals(Name, other.Name, StringComparison.Ordinal)
            && string.Equals(Version, other.Version, StringComparison.Ordinal)
            && string.Equals(PublicKeyToken, other.PublicKeyToken, StringComparison.Ordinal)
            && Length == other.Length;
    }

    public static RevitApiIdentity FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var name = AssemblyName.GetAssemblyName(path);
        return new RevitApiIdentity(
            name.Name ?? "",
            name.Version?.ToString() ?? "",
            Token(name),
            new FileInfo(path).Length);
    }

    public static string Token(AssemblyName name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var token = name.GetPublicKeyToken();
        return token is null || token.Length == 0
            ? "none"
            : Convert.ToHexString(token).ToLowerInvariant();
    }

    public string Canonical()
    {
        return string.Join(
            '|',
            Name,
            Version,
            PublicKeyToken,
            Length.ToString(CultureInfo.InvariantCulture));
    }
}
