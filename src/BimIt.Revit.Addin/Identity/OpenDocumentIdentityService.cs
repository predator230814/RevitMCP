using Autodesk.Revit.DB;

namespace BimIt.Revit.Addin.Identity;

/// <summary>
/// Addin-owned ADR-0006 mapping from a live Revit <see cref="Document"/> to an
/// opaque process-lifetime <c>document_id</c>. Relies on Revit document
/// <c>Equals</c>/<c>GetHashCode</c>, not managed reference identity. Call only
/// from valid Revit API execution context. Does not read title, path, cloud
/// identity, username, or process id, and does not write into the model.
/// Document close detaches the mapping during Closing, while the document is still valid.
/// A successful close then drops it; a cancelled or failed close restores the same id.
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

    public bool TryDetach(Document document, out string documentId)
    {
        ArgumentNullException.ThrowIfNull(document);
        return _map.TryDetach(document, out documentId);
    }

    public void Restore(Document document, string documentId)
    {
        ArgumentNullException.ThrowIfNull(document);
        _map.Restore(document, documentId);
    }
}
