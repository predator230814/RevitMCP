using Autodesk.Revit.DB;

namespace RevitMCP.Addin.Identity;

/// <summary>
/// Addin-owned ADR-0006 mapping from a live Revit <see cref="Document"/> to an
/// opaque process-lifetime <c>document_id</c>. Call only from valid Revit API
/// execution context. Does not read title, path, cloud identity, username, or
/// process id, and does not write into the model.
/// </summary>
internal sealed class OpenDocumentIdentityService
{
    private readonly OpenDocumentIdentityMap<Document> _map = new();

    public string GetId(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return _map.GetOrAssign(document);
    }

    public bool TryGet(Document document, out string documentId)
    {
        ArgumentNullException.ThrowIfNull(document);
        return _map.TryGet(document, out documentId);
    }

    public bool Forget(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return _map.Remove(document);
    }
}
