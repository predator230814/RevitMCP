namespace RevitMCP.Addin.Execution;

/// <summary>
/// Serialized FIFO execution queue. The Autodesk <c>ExternalEvent</c> adapter is
/// substituted through <see cref="IRevitEventSignal"/> so the state machine can
/// be tested without Revit.
/// </summary>
public sealed class RevitExecutionQueue<TContext>
{
    private readonly object _gate = new();
    private readonly Queue<IRevitWorkItem<TContext>> _pending = new();
    private readonly IRevitEventSignal _signal;
    private RevitExecutionStatus _status = RevitExecutionStatus.Idle;
    private bool _executing;

    public RevitExecutionQueue(IRevitEventSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        _signal = signal;
    }

    public RevitExecutionStatus Status
    {
        get
        {
            lock (_gate)
            {
                return _status;
            }
        }
    }

    public Task<T> EnqueueAsync<T>(
        Func<TContext, T> operation,
        CancellationToken cancellationToken,
        string? correlationId = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();

        var item = new RevitWorkItem<T>(operation, cancellationToken, correlationId);
        var shouldSchedule = false;

        lock (_gate)
        {
            EnsureAccepting_NoLock();
            _pending.Enqueue(item);
            if (_status == RevitExecutionStatus.Idle)
            {
                _status = RevitExecutionStatus.ScheduledOrExecuting;
                shouldSchedule = true;
            }
        }

        if (shouldSchedule)
        {
            ScheduleOrFailWaitingWork();
        }

        return item.Completion.WaitAsync(cancellationToken);
    }

    public void ExecutePending(TContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        while (true)
        {
            IRevitWorkItem<TContext>? item;
            lock (_gate)
            {
                if (_status == RevitExecutionStatus.Stopped)
                {
                    return;
                }

                if (_status == RevitExecutionStatus.Stopping)
                {
                    RejectQueuedWork_NoLock(CreateStoppedException());
                    _status = RevitExecutionStatus.Stopped;
                    return;
                }

                item = DequeueNextExecutable_NoLock();
                if (item is null)
                {
                    _status = RevitExecutionStatus.Idle;
                    return;
                }

                _executing = true;
            }

            try
            {
                item.Run(context);
            }
            catch (Exception exception)
            {
                item.TryFail(exception);
            }
            finally
            {
                lock (_gate)
                {
                    _executing = false;
                }
            }
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_status is RevitExecutionStatus.Stopping or RevitExecutionStatus.Stopped)
            {
                return;
            }

            RejectQueuedWork_NoLock(CreateStoppedException());
            _status = _executing ? RevitExecutionStatus.Stopping : RevitExecutionStatus.Stopped;
        }
    }

    private void ScheduleOrFailWaitingWork()
    {
        ExecutionScheduleResult result;
        try
        {
            result = _signal.Schedule();
        }
        catch (Exception exception)
        {
            FailWaitingWork(new RevitExecutionException(
                RevitExecutionErrorCodes.SchedulingFailed,
                "The execution dispatcher failed while scheduling the Revit ExternalEvent.",
                exception));
            return;
        }

        if (result is ExecutionScheduleResult.Accepted or ExecutionScheduleResult.Pending)
        {
            return;
        }

        FailWaitingWork(new RevitExecutionException(
            RevitExecutionErrorCodes.SchedulingFailed,
            $"The execution dispatcher could not schedule the Revit ExternalEvent ({result})."));
    }

    private void FailWaitingWork(RevitExecutionException exception)
    {
        lock (_gate)
        {
            RejectQueuedWork_NoLock(exception);
            if (_status is RevitExecutionStatus.Stopping or RevitExecutionStatus.Stopped)
            {
                if (!_executing)
                {
                    _status = RevitExecutionStatus.Stopped;
                }

                return;
            }

            if (!_executing)
            {
                _status = RevitExecutionStatus.Idle;
            }
        }
    }

    private void EnsureAccepting_NoLock()
    {
        if (_status is RevitExecutionStatus.Stopping or RevitExecutionStatus.Stopped)
        {
            throw CreateStoppedException();
        }
    }

    private void RejectQueuedWork_NoLock(Exception exception)
    {
        while (_pending.Count > 0)
        {
            _pending.Dequeue().TryReject(exception);
        }
    }

    private IRevitWorkItem<TContext>? DequeueNextExecutable_NoLock()
    {
        while (_pending.Count > 0)
        {
            var item = _pending.Dequeue();
            if (item.TryBegin())
            {
                return item;
            }
        }

        return null;
    }

    private static RevitExecutionException CreateStoppedException() =>
        new(
            RevitExecutionErrorCodes.DispatcherStopped,
            "The Revit execution dispatcher is stopped and is not accepting work.");

    private interface IRevitWorkItem<in TWorkContext>
    {
        bool TryBegin();
        void Run(TWorkContext context);
        void TryFail(Exception exception);
        void TryReject(Exception exception);
    }

    private sealed class RevitWorkItem<TResult> : IRevitWorkItem<TContext>
    {
        private const int Queued = 0;
        private const int Running = 1;
        private const int Completed = 2;

        private readonly Func<TContext, TResult> _operation;
        private readonly CancellationToken _cancellationToken;
        private readonly TaskCompletionSource<TResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenRegistration _cancellationRegistration;
        private int _state;

        public RevitWorkItem(
            Func<TContext, TResult> operation,
            CancellationToken cancellationToken,
            string? correlationId)
        {
            _operation = operation;
            _cancellationToken = cancellationToken;
            CorrelationId = correlationId;
            if (cancellationToken.CanBeCanceled)
            {
                _cancellationRegistration = cancellationToken.Register(TryCancelIfNotStarted);
            }
        }

        public string? CorrelationId { get; }

        public Task<TResult> Completion => _completion.Task;

        public bool TryBegin()
        {
            if (Interlocked.CompareExchange(ref _state, Running, Queued) != Queued)
            {
                return false;
            }

            _cancellationRegistration.Dispose();
            return true;
        }

        public void Run(TContext context)
        {
            try
            {
                var result = _operation(context);
                CompleteResult(result);
            }
            catch (Exception exception)
            {
                TryFail(exception);
            }
        }

        public void TryFail(Exception exception)
        {
            Interlocked.Exchange(ref _state, Completed);
            _cancellationRegistration.Dispose();
            _completion.TrySetException(exception);
        }

        public void TryReject(Exception exception)
        {
            if (Interlocked.CompareExchange(ref _state, Completed, Queued) != Queued)
            {
                return;
            }

            _cancellationRegistration.Dispose();
            if (!string.IsNullOrWhiteSpace(CorrelationId))
            {
                exception.Data["correlation_id"] = CorrelationId;
            }

            _completion.TrySetException(exception);
        }

        private void CompleteResult(TResult result)
        {
            Interlocked.Exchange(ref _state, Completed);
            _cancellationRegistration.Dispose();
            _completion.TrySetResult(result);
        }

        private void TryCancelIfNotStarted()
        {
            if (Interlocked.CompareExchange(ref _state, Completed, Queued) != Queued)
            {
                return;
            }

            _cancellationRegistration.Dispose();
            _completion.TrySetCanceled(_cancellationToken);
        }
    }
}
