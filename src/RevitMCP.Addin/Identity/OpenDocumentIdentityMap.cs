using System.Runtime.CompilerServices;

namespace RevitMCP.Addin.Identity;

/// <summary>
/// Process-lifetime bookkeeping for opaque open-document identities.
/// Keys are held weakly so obsolete mappings can be collected; <see cref="Remove"/>
/// releases a mapping explicitly when a document is known to be closed.
/// </summary>
internal sealed class OpenDocumentIdentityMap<TKey>
    where TKey : class
{
    private readonly ConditionalWeakTable<TKey, IdentityHolder> _ids = new();
    private readonly Func<string> _createId;

    public OpenDocumentIdentityMap(Func<string>? createId = null)
    {
        _createId = createId ?? CreateOpaqueId;
    }

    public string GetOrAssign(TKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _ids.GetValue(key, _ => new IdentityHolder(_createId())).Value;
    }

    public bool TryGet(TKey key, out string documentId)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (_ids.TryGetValue(key, out var holder))
        {
            documentId = holder.Value;
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

    private sealed class IdentityHolder
    {
        public IdentityHolder(string value)
        {
            Value = value;
        }

        public string Value { get; }
    }
}
