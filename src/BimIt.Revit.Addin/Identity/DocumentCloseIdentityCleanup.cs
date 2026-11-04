namespace BimIt.Revit.Addin.Identity;

internal enum DocumentCloseOutcome
{
    Succeeded,
    Cancelled,
    Failed
}

/// <summary>
/// Document state detached during <c>DocumentClosing</c>. <see cref="Forget"/> runs after a
/// successful close and must not use the closed document; <see cref="Restore"/> runs after a
/// cancelled or failed close, while the document is still open.
/// </summary>
internal sealed class DetachedDocumentState
{
    public static readonly DetachedDocumentState None = new(static () => { }, static () => { });

    private readonly Action _forget;
    private readonly Action _restore;

    public DetachedDocumentState(Action forget, Action restore)
    {
        ArgumentNullException.ThrowIfNull(forget);
        ArgumentNullException.ThrowIfNull(restore);
        _forget = forget;
        _restore = restore;
    }

    public void Forget() => _forget();

    public void Restore() => _restore();
}

/// <summary>
/// Two-phase Closing/Closed correlation. <c>DocumentClosing.DocumentId</c> is a
/// temporary event-pair key only; it is not a RevitMCP <c>document_id</c>.
/// Document-keyed state is detached during Closing, while the document is still valid:
/// in live Revit 2026.5 the document is invalid by <c>DocumentClosed</c>, and its
/// <c>GetHashCode</c> throws.
/// </summary>
internal sealed class DocumentCloseIdentityCleanup<TDocument>
    where TDocument : class
{
    private readonly Dictionary<int, DetachedDocumentState> _pending = new();
    private readonly Func<TDocument, DetachedDocumentState> _detach;

    public DocumentCloseIdentityCleanup(Func<TDocument, DetachedDocumentState> detach)
    {
        ArgumentNullException.ThrowIfNull(detach);
        _detach = detach;
    }

    public void OnClosing(int documentEventId, TDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (_pending.ContainsKey(documentEventId))
        {
            return;
        }

        _pending[documentEventId] = _detach(document);
    }

    public void OnClosed(int documentEventId, DocumentCloseOutcome outcome)
    {
        if (!_pending.Remove(documentEventId, out var detached))
        {
            return;
        }

        if (outcome == DocumentCloseOutcome.Succeeded)
        {
            detached.Forget();
        }
        else
        {
            detached.Restore();
        }
    }

    public bool HasPending(int documentEventId) => _pending.ContainsKey(documentEventId);
}
