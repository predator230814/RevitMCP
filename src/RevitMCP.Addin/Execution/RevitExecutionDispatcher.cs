using Autodesk.Revit.UI;

namespace RevitMCP.Addin.Execution;

/// <summary>
/// Production facade over the serialized execution queue and one long-lived
/// Revit <see cref="ExternalEvent"/> / <see cref="IExternalEventHandler"/> pair.
/// <see cref="Create"/> must be called from a valid Revit API context.
/// </summary>
public sealed class RevitExecutionDispatcher : IDisposable
{
    private readonly RevitExecutionQueue<UIApplication> _queue;
    private readonly ExternalEvent? _externalEvent;
    private int _disposed;

    internal RevitExecutionDispatcher(
        RevitExecutionQueue<UIApplication> queue,
        ExternalEvent? externalEvent)
    {
        ArgumentNullException.ThrowIfNull(queue);
        _queue = queue;
        _externalEvent = externalEvent;
    }

    public RevitExecutionStatus Status => _queue.Status;

    public static RevitExecutionDispatcher Create()
    {
        var relay = new RelayEventSignal();
        var queue = new RevitExecutionQueue<UIApplication>(relay);
        var handler = new RevitExternalEventHandler(queue);
        var externalEvent = ExternalEvent.Create(handler);
        relay.Target = new RevitExternalEventSignal(externalEvent);
        return new RevitExecutionDispatcher(queue, externalEvent);
    }

    public Task<T> EnqueueAsync<T>(
        Func<UIApplication, T> operation,
        CancellationToken cancellationToken,
        string? correlationId = null)
    {
        return _queue.EnqueueAsync(operation, cancellationToken, correlationId);
    }

    public void Stop() => _queue.Stop();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Stop();
        _externalEvent?.Dispose();
    }

    private sealed class RelayEventSignal : IRevitEventSignal
    {
        public IRevitEventSignal? Target { get; set; }

        public ExecutionScheduleResult Schedule()
        {
            if (Target is null)
            {
                throw new InvalidOperationException("The Revit ExternalEvent has not been attached.");
            }

            return Target.Schedule();
        }
    }
}
