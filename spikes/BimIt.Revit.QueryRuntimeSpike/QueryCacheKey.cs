using System.Security.Cryptography;
using System.Text;

namespace BimIt.Revit.QueryRuntimeSpike;

public static class QueryCacheKey
{
    public static string Create(string source, string contract, RevitApiIdentity api, int policyVersion)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(contract);
        ArgumentNullException.ThrowIfNull(api);
        var payload = string.Join(
            '\n',
            contract,
            policyVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            api.Text,
            source);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }
}
