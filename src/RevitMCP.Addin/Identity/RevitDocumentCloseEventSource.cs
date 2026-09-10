using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;

namespace RevitMCP.Addin.Identity;

internal interface IDocumentCloseEventSource
{
    IDisposable Subscribe(DocumentCloseIdentityCleanup<Document> cleanup);
}

internal sealed class RevitDocumentCloseEventSource : IDocumentCloseEventSource
{
    private readonly ControlledApplication _application;

    public RevitDocumentCloseEventSource(ControlledApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);
        _application = application;
    }

    public IDisposable Subscribe(DocumentCloseIdentityCleanup<Document> cleanup)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        return new Subscription(_application, cleanup);
    }

    private sealed class Subscription : IDisposable
    {
        private readonly ControlledApplication _application;
        private readonly DocumentCloseIdentityCleanup<Document> _cleanup;
        private readonly EventHandler<DocumentClosingEventArgs> _closing;
        private readonly EventHandler<DocumentClosedEventArgs> _closed;
        private int _disposed;

        public Subscription(ControlledApplication application, DocumentCloseIdentityCleanup<Document> cleanup)
        {
            _application = application;
            _cleanup = cleanup;
            _closing = (_, args) => _cleanup.OnClosing(args.DocumentId, args.Document);
            _closed = (_, args) => _cleanup.OnClosed(args.DocumentId, Map(args.Status));
            _application.DocumentClosing += _closing;
            _application.DocumentClosed += _closed;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _application.DocumentClosing -= _closing;
            _application.DocumentClosed -= _closed;
        }

        private static DocumentCloseOutcome Map(RevitAPIEventStatus status)
        {
            return status switch
            {
                RevitAPIEventStatus.Succeeded => DocumentCloseOutcome.Succeeded,
                RevitAPIEventStatus.Cancelled => DocumentCloseOutcome.Cancelled,
                RevitAPIEventStatus.Failed => DocumentCloseOutcome.Failed,
                _ => DocumentCloseOutcome.Failed
            };
        }
    }
}
