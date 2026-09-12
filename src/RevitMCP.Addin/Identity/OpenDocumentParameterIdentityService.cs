using Autodesk.Revit.DB;
using RevitMCP.Addin.Inspection;
using RevitMCP.Contracts;

namespace RevitMCP.Addin.Identity;

internal sealed class OpenDocumentParameterIdentityService
{
    private readonly Dictionary<Document, Dictionary<string, string>> _refs = new(EqualityComparer<Document>.Default);

    public string GetRef(
        Document document,
        GetElementParameterSource source,
        ClassifiedParameterIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(identity.StableKey);

        if (!_refs.TryGetValue(document, out var map))
        {
            map = new Dictionary<string, string>(StringComparer.Ordinal);
            _refs.Add(document, map);
        }

        var fingerprint = ((int)source).ToString() + "\n" + ((int)identity.Kind).ToString() + "\n" + identity.StableKey;
        if (map.TryGetValue(fingerprint, out var existing))
        {
            return existing;
        }

        var assigned = Guid.NewGuid().ToString("D");
        map.Add(fingerprint, assigned);
        return assigned;
    }

    public bool Forget(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return _refs.Remove(document);
    }
}
