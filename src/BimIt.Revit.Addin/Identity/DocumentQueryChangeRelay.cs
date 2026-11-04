namespace BimIt.Revit.Addin.Identity;

/// <summary>
/// Forwards document-change signals from handlers registered with Revit during
/// <c>OnStartup</c> to listeners that subscribe later, such as the dispatcher lifetime
/// created by the Idling bootstrap. Signals published while no listener is subscribed are dropped.
/// </summary>
internal sealed class DocumentQueryChangeRelay<TDocument>
    where TDocument : class
{
    private readonly object _gate = new();
    private Action<TDocument>[] _listeners = [];

    public IDisposable Subscribe(Action<TDocument> listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        lock (_gate)
        {
            _listeners = [.. _listeners, listener];
        }

        return new Listener(this, listener);
    }

    public void Publish(TDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        foreach (var listener in Volatile.Read(ref _listeners))
        {
            listener(document);
        }
    }

    private void Remove(Action<TDocument> listener)
    {
        lock (_gate)
        {
            var index = Array.IndexOf(_listeners, listener);
            if (index < 0)
            {
                return;
            }

            _listeners = [.. _listeners[..index], .. _listeners[(index + 1)..]];
        }
    }

    private sealed class Listener : IDisposable
    {
        private readonly DocumentQueryChangeRelay<TDocument> _relay;
        private readonly Action<TDocument> _listener;
        private int _disposed;

        public Listener(DocumentQueryChangeRelay<TDocument> relay, Action<TDocument> listener)
        {
            _relay = relay;
            _listener = listener;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _relay.Remove(_listener);
            }
        }
    }
}
