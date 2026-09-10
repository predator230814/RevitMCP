namespace RevitMCP.Addin.Identity;

internal enum DocumentCloseOutcome
{
    Succeeded,
    Cancelled,
    Failed
}

/// <summary>
/// Two-phase Closing/Closed correlation. <c>DocumentClosing.DocumentId</c> is a
/// temporary event-pair key only; it is not a RevitMCP <c>document_id</c>.
/// </summary>
internal sealed class DocumentCloseIdentityCleanup<TDocument>
    where TDocument : class
{
    private readonly Dictionary<int, TDocument> _pending = new();
    private readonly Func<TDocument, bool> _forget;

    public DocumentCloseIdentityCleanup(Func<TDocument, bool> forget)
    {
        ArgumentNullException.ThrowIfNull(forget);
        _forget = forget;
    }

    public void OnClosing(int documentEventId, TDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _pending[documentEventId] = document;
    }

    public void OnClosed(int documentEventId, DocumentCloseOutcome outcome)
    {
        if (!_pending.Remove(documentEventId, out var document))
        {
            return;
        }

        if (outcome == DocumentCloseOutcome.Succeeded)
        {
            _forget(document);
        }
    }

    public bool HasPending(int documentEventId) => _pending.ContainsKey(documentEventId);
}
