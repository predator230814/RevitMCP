using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;

namespace BimIt.Revit.Addin.Identity;

/// <summary>
/// Signals that a host document may have changed in a way that can affect FamilySymbol
/// inventory or placed-instance counts. The revision service advances on every signal.
/// These subscriptions do not prove that Revit raises each event for every such change.
/// </summary>
internal interface IDocumentQueryChangeEventSource
{
    IDisposable Subscribe(Action<Document> onPossibleInventoryChange);
}

internal sealed class RevitDocumentQueryChangeEventSource : IDocumentQueryChangeEventSource
{
    private readonly ControlledApplication _application;

    public RevitDocumentQueryChangeEventSource(ControlledApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);
        _application = application;
    }

    public IDisposable Subscribe(Action<Document> onPossibleInventoryChange)
    {
        ArgumentNullException.ThrowIfNull(onPossibleInventoryChange);
        return new Subscription(_application, onPossibleInventoryChange);
    }

    private sealed class Subscription : IDisposable
    {
        private readonly ControlledApplication _application;
        private readonly EventHandler<DocumentChangedEventArgs> _changed;
        private readonly EventHandler<DocumentSynchronizedWithCentralEventArgs> _synchronized;
        private readonly EventHandler<DocumentReloadedLatestEventArgs> _reloaded;
        private int _disposed;

        public Subscription(ControlledApplication application, Action<Document> onPossibleInventoryChange)
        {
            _application = application;
            _changed = (_, args) => onPossibleInventoryChange(args.GetDocument());
            _synchronized = (_, args) => onPossibleInventoryChange(args.Document);
            _reloaded = (_, args) => onPossibleInventoryChange(args.Document);
            _application.DocumentChanged += _changed;
            _application.DocumentSynchronizedWithCentral += _synchronized;
            _application.DocumentReloadedLatest += _reloaded;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _application.DocumentChanged -= _changed;
            _application.DocumentSynchronizedWithCentral -= _synchronized;
            _application.DocumentReloadedLatest -= _reloaded;
        }
    }
}
