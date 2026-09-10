namespace RevitMCP.Addin.Identity;

/// <summary>
/// Process-lifetime bookkeeping for opaque open-document identities.
/// Uses <see cref="EqualityComparer{T}.Default"/> so logically equal keys
/// (including distinct Revit <c>Document</c> wrappers for the same open document)
/// share one id. The dictionary holds strong references; <see cref="Remove"/>
/// must be called when a document is closed. There is no automatic weak-key
/// collection.
/// </summary>
internal sealed class OpenDocumentIdentityMap<TKey>
    where TKey : class
{
    private readonly Dictionary<TKey, string> _ids = new(EqualityComparer<TKey>.Default);
    private readonly Func<string> _createId;

    public OpenDocumentIdentityMap(Func<string>? createId = null)
    {
        _createId = createId ?? CreateOpaqueId;
    }

    public string GetOrAssign(TKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (_ids.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var id = _createId();
        _ids.Add(key, id);
        return id;
    }

    public bool TryGet(TKey key, out string documentId)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (_ids.TryGetValue(key, out var existing))
        {
            documentId = existing;
            return true;
        }

        documentId = string.Empty;
        return false;
    }

    public bool Remove(TKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _ids.Remove(key);
    }

    private static string CreateOpaqueId() => Guid.NewGuid().ToString("D");
}
