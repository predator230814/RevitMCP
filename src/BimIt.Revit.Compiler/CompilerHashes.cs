using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace BimIt.Revit.Compiler;

public static class CompilerHashes
{
    public static string SourceSha256(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Hex(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
    }

    public static string CacheKey(string source, RevitApiIdentity api, int policyVersion)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(api);
        var payload = string.Join(
            '\n',
            CompilerProtocol.ContractName,
            policyVersion.ToString(CultureInfo.InvariantCulture),
            api.Canonical(),
            source);
        return Hex(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private static string Hex(byte[] hash)
    {
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
