using Autodesk.Revit.DB;
using RevitMCP.Addin.Inspection;
using RevitMCP.Contracts;

namespace RevitMCP.Addin.Identity;

internal sealed class OpenDocumentParameterIdentityService
{
    private readonly Dictionary<Document, ParameterIdentityMap> _maps = new(EqualityComparer<Document>.Default);

    public string GetRef(
        Document document,
        GetElementParameterSource source,
        ClassifiedParameterIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(document);
        return GetMap(document).GetRef(source, identity);
    }

    public bool TryResolve(
        Document document,
        string parameterRef,
        out ParameterIdentityBinding binding)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(parameterRef);

        if (_maps.TryGetValue(document, out var map))
        {
            return map.TryResolve(parameterRef, out binding);
        }

        binding = default;
        return false;
    }

    public bool Forget(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return _maps.Remove(document);
    }

    private ParameterIdentityMap GetMap(Document document)
    {
        if (!_maps.TryGetValue(document, out var map))
        {
            map = new ParameterIdentityMap();
            _maps.Add(document, map);
        }

        return map;
    }
}
