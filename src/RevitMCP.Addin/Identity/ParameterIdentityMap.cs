using RevitMCP.Addin.Inspection;
using RevitMCP.Contracts;

namespace RevitMCP.Addin.Identity;

internal readonly record struct ParameterIdentityBinding(
    GetElementParameterSource Source,
    ClassifiedParameterIdentity Identity);

internal sealed class ParameterIdentityMap
{
    private readonly Dictionary<string, string> _forward = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ParameterIdentityBinding> _reverse = new(StringComparer.Ordinal);

    public string GetRef(GetElementParameterSource source, ClassifiedParameterIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity.StableKey);

        var fingerprint = ((int)source).ToString() + "\n" + ((int)identity.Kind).ToString() + "\n" + identity.StableKey;
        if (_forward.TryGetValue(fingerprint, out var existing))
        {
            return existing;
        }

        var assigned = Guid.NewGuid().ToString("D");
        _forward.Add(fingerprint, assigned);
        _reverse.Add(assigned, new ParameterIdentityBinding(source, identity));
        return assigned;
    }

    public bool TryResolve(string parameterRef, out ParameterIdentityBinding binding)
    {
        ArgumentNullException.ThrowIfNull(parameterRef);
        return _reverse.TryGetValue(parameterRef, out binding);
    }
}
