using RevitMCP.Addin.Execution;

namespace RevitMCP.Addin.Tests;

internal sealed class RecordingEventSignal : IRevitEventSignal
{
    private readonly object _gate = new();
    private readonly Queue<ExecutionScheduleResult> _results = new();
    private readonly List<ExecutionScheduleResult> _history = [];
    private int _count;

    public ExecutionScheduleResult DefaultResult { get; set; } = ExecutionScheduleResult.Accepted;

    public int Count => Volatile.Read(ref _count);

    public IReadOnlyList<ExecutionScheduleResult> History
    {
        get
        {
            lock (_gate)
            {
                return [.. _history];
            }
        }
    }

    public void EnqueueResult(ExecutionScheduleResult result)
    {
        lock (_gate)
        {
            _results.Enqueue(result);
        }
    }

    public ExecutionScheduleResult Schedule()
    {
        Interlocked.Increment(ref _count);
        lock (_gate)
        {
            var result = _results.Count > 0 ? _results.Dequeue() : DefaultResult;
            _history.Add(result);
            return result;
        }
    }
}
