using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace RevitMCP.Addin.Identity;

internal delegate void ActiveDocumentChanged(bool hasExistingDocumentId, string? documentId);

internal interface IActiveDocumentEventSource
{
    IDisposable Subscribe(ActiveDocumentChanged onChanged, OpenDocumentIdentityService identity);
}

internal sealed class RevitActiveDocumentEventSource : IActiveDocumentEventSource
{
    private readonly UIControlledApplication _application;

    public RevitActiveDocumentEventSource(UIControlledApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);
        _application = application;
    }

    public IDisposable Subscribe(ActiveDocumentChanged onChanged, OpenDocumentIdentityService identity)
    {
        ArgumentNullException.ThrowIfNull(onChanged);
        ArgumentNullException.ThrowIfNull(identity);
        return new Subscription(_application, onChanged, identity);
    }

    private sealed class Subscription : IDisposable
    {
        private readonly UIControlledApplication _application;
        private readonly EventHandler<ViewActivatedEventArgs> _handler;
        private int _disposed;

        public Subscription(
            UIControlledApplication application,
            ActiveDocumentChanged onChanged,
            OpenDocumentIdentityService identity)
        {
            _application = application;
            _handler = (_, args) =>
            {
                var document = args.CurrentActiveView?.Document;
                if (document is not null && identity.TryGet(document, out var documentId))
                {
                    onChanged(true, documentId);
                    return;
                }

                onChanged(false, null);
            };
            _application.ViewActivated += _handler;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _application.ViewActivated -= _handler;
        }
    }
}

internal sealed class DisabledActiveDocumentEventSource : IActiveDocumentEventSource
{
    public IDisposable Subscribe(ActiveDocumentChanged onChanged, OpenDocumentIdentityService identity)
    {
        throw new InvalidOperationException("Active-document observation is unavailable.");
    }
}
