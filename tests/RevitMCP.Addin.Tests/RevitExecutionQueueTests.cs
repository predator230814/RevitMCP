using RevitMCP.Addin.Execution;
using Xunit;

namespace RevitMCP.Addin.Tests;

public sealed class RevitExecutionQueueTests
{
    private static readonly object Context = new();

    [Fact]
    public async Task Enqueued_work_executes_in_fifo_order()
    {
        var signal = new RecordingEventSignal();
        var queue = new RevitExecutionQueue<object>(signal);
        var order = new List<int>();

        var first = queue.EnqueueAsync(_ => Add(order, 1), CancellationToken.None);
        var second = queue.EnqueueAsync(_ => Add(order, 2), CancellationToken.None);
        var third = queue.EnqueueAsync(_ => Add(order, 3), CancellationToken.None);

        queue.ExecutePending(Context);

        Assert.Equal(new[] { 1, 2, 3 }, order);
        Assert.Equal(new[] { 1, 2, 3 }, new[] { await first, await second, await third });
    }

    [Fact]
    public async Task Concurrent_enqueue_remains_serialized()
    {
        var signal = new RecordingEventSignal();
        var queue = new RevitExecutionQueue<object>(signal);
        var running = 0;
        var maxRunning = 0;
        var gate = new object();

        var accepted = new Task<int>[16];
        Parallel.For(0, accepted.Length, index =>
        {
            accepted[index] = queue.EnqueueAsync(
                _ =>
                {
                    var now = Interlocked.Increment(ref running);
                    lock (gate)
                    {
                        if (now > maxRunning)
                        {
                            maxRunning = now;
                        }
                    }

                    Thread.SpinWait(2_000);
                    Interlocked.Decrement(ref running);
                    return index;
                },
                CancellationToken.None);
        });

        queue.ExecutePending(Context);

        var results = await Task.WhenAll(accepted);
        Assert.Equal(accepted.Length, results.Distinct().Count());
        Assert.Equal(1, maxRunning);
    }

    [Fact]
    public async Task Only_one_schedule_signal_is_requested_while_already_scheduled()
    {
        var signal = new RecordingEventSignal();
        var queue = new RevitExecutionQueue<object>(signal);

        var first = queue.EnqueueAsync(_ => 1, CancellationToken.None);
        var second = queue.EnqueueAsync(_ => 2, CancellationToken.None);
        var third = queue.EnqueueAsync(_ => 3, CancellationToken.None);

        Assert.Equal(1, signal.Count);
        Assert.Equal(RevitExecutionStatus.ScheduledOrExecuting, queue.Status);

        queue.ExecutePending(Context);

        Assert.Equal(new[] { 1, 2, 3 }, new[] { await first, await second, await third });
        Assert.Equal(1, signal.Count);
    }

    [Fact]
    public async Task Empty_idle_then_new_enqueue_does_not_strand_work()
    {
        var signal = new RecordingEventSignal();
        var queue = new RevitExecutionQueue<object>(signal);

        var first = queue.EnqueueAsync(_ => 1, CancellationToken.None);
        queue.ExecutePending(Context);
        Assert.Equal(1, await first);
        Assert.Equal(RevitExecutionStatus.Idle, queue.Status);

        var second = queue.EnqueueAsync(_ => 2, CancellationToken.None);
        Assert.Equal(2, signal.Count);
        queue.ExecutePending(Context);
        Assert.Equal(2, await second);
    }

    [Fact]
    public async Task Concurrent_enqueue_during_idle_transition_does_not_strand_work()
    {
        var signal = new RecordingEventSignal();
        var queue = new RevitExecutionQueue<object>(signal);
        using var done = new CancellationTokenSource();
        var completed = 0;

        var pump = Task.Run(() =>
        {
            while (!done.IsCancellationRequested || completed < 80)
            {
                queue.ExecutePending(Context);
                Thread.Sleep(1);
            }

            queue.ExecutePending(Context);
        });

        var tasks = new Task<int>[80];
        Parallel.For(0, tasks.Length, index =>
        {
            tasks[index] = queue.EnqueueAsync(
                _ =>
                {
                    Interlocked.Increment(ref completed);
                    return index;
                },
                CancellationToken.None);
        });

        var results = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(15));
        done.Cancel();
        await pump.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(80, results.Length);
        Assert.Equal(80, results.Distinct().Count());
    }

    [Fact]
    public async Task Cancelled_queued_work_never_executes()
    {
        var signal = new RecordingEventSignal();
        var queue = new RevitExecutionQueue<object>(signal);
        using var cts = new CancellationTokenSource();
        var executed = false;

        var cancelled = queue.EnqueueAsync(
            _ =>
            {
                executed = true;
                return 1;
            },
            cts.Token);
        cts.Cancel();

        var later = queue.EnqueueAsync(_ => 2, CancellationToken.None);
        queue.ExecutePending(Context);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        Assert.False(executed);
        Assert.Equal(2, await later);
    }

    [Fact]
    public async Task Cancellation_after_start_lets_operation_finish()
    {
        var signal = new RecordingEventSignal();
        var queue = new RevitExecutionQueue<object>(signal);
        using var cts = new CancellationTokenSource();
        using var started = new ManualResetEventSlim(false);
        using var finish = new ManualResetEventSlim(false);
        var executed = false;

        var caller = queue.EnqueueAsync(
            _ =>
            {
                started.Set();
                finish.Wait();
                executed = true;
                return 7;
            },
            cts.Token);

        var pump = Task.Run(() => queue.ExecutePending(Context));
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => caller);
        Assert.False(executed);

        finish.Set();
        await pump.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(executed);
    }

    [Fact]
    public async Task Exception_in_one_item_does_not_prevent_later_work()
    {
        var signal = new RecordingEventSignal();
        var queue = new RevitExecutionQueue<object>(signal);

        var failed = queue.EnqueueAsync<object>(_ => throw new InvalidOperationException("boom"), CancellationToken.None);
        var later = queue.EnqueueAsync(_ => 9, CancellationToken.None);

        var thrown = Record.Exception(() => queue.ExecutePending(Context));

        Assert.Null(thrown);
        await Assert.ThrowsAsync<InvalidOperationException>(() => failed);
        Assert.Equal(9, await later);
        Assert.Equal(RevitExecutionStatus.Idle, queue.Status);
    }

    [Fact]
    public async Task Completion_continuations_are_asynchronous()
    {
        var signal = new RecordingEventSignal();
        var queue = new RevitExecutionQueue<object>(signal);
        var revitThreadId = 0;
        var continuationThreadId = 0;

        var result = queue.EnqueueAsync(
            _ =>
            {
                revitThreadId = Environment.CurrentManagedThreadId;
                return 1;
            },
            CancellationToken.None);

        var observed = result.ContinueWith(
            task =>
            {
                continuationThreadId = Environment.CurrentManagedThreadId;
                return task.Result;
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        var revitThread = new Thread(() => queue.ExecutePending(Context))
        {
            IsBackground = true,
            Name = "RevitMCP-FakeRevit"
        };
        revitThread.Start();
        revitThread.Join();

        Assert.Equal(1, await observed);
        Assert.NotEqual(0, revitThreadId);
        Assert.NotEqual(revitThreadId, continuationThreadId);
    }

    [Fact]
    public void Shutdown_rejects_new_work()
    {
        var signal = new RecordingEventSignal();
        var queue = new RevitExecutionQueue<object>(signal);

        queue.Stop();

        var exception = Assert.Throws<RevitExecutionException>(() =>
        {
            _ = queue.EnqueueAsync(_ => 1, CancellationToken.None);
        });
        Assert.Equal(RevitExecutionErrorCodes.DispatcherStopped, exception.ErrorCode);
        Assert.Equal(RevitExecutionStatus.Stopped, queue.Status);
    }

    [Fact]
    public async Task Shutdown_completes_queued_not_started_work_and_lets_running_work_finish()
    {
        var signal = new RecordingEventSignal();
        var queue = new RevitExecutionQueue<object>(signal);
        using var started = new ManualResetEventSlim(false);
        using var finish = new ManualResetEventSlim(false);

        var running = queue.EnqueueAsync(
            _ =>
            {
                started.Set();
                finish.Wait();
                return 4;
            },
            CancellationToken.None);

        var pump = Task.Run(() => queue.ExecutePending(Context));
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));

        var queued = queue.EnqueueAsync(_ => 5, CancellationToken.None);
        queue.Stop();

        var rejected = await Assert.ThrowsAsync<RevitExecutionException>(() => queued);
        Assert.Equal(RevitExecutionErrorCodes.DispatcherStopped, rejected.ErrorCode);
        Assert.False(running.IsCompleted);

        finish.Set();
        Assert.Equal(4, await running);
        await pump.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(RevitExecutionStatus.Stopped, queue.Status);
        Assert.Throws<RevitExecutionException>(() =>
        {
            _ = queue.EnqueueAsync(_ => 6, CancellationToken.None);
        });
    }

    [Fact]
    public async Task Scheduling_failure_does_not_leave_queued_tasks_hanging()
    {
        var signal = new RecordingEventSignal
        {
            DefaultResult = ExecutionScheduleResult.Denied
        };
        var queue = new RevitExecutionQueue<object>(signal);

        var denied = queue.EnqueueAsync(_ => 1, CancellationToken.None);
        var exception = await Assert.ThrowsAsync<RevitExecutionException>(() => denied);
        Assert.Equal(RevitExecutionErrorCodes.SchedulingFailed, exception.ErrorCode);
        Assert.Equal(RevitExecutionStatus.Idle, queue.Status);

        signal.DefaultResult = ExecutionScheduleResult.Accepted;
        var recovered = queue.EnqueueAsync(_ => 2, CancellationToken.None);
        queue.ExecutePending(Context);
        Assert.Equal(2, await recovered);
    }

    [Fact]
    public async Task Timed_out_raise_fails_waiting_work_deterministically()
    {
        var signal = new RecordingEventSignal
        {
            DefaultResult = ExecutionScheduleResult.TimedOut
        };
        var queue = new RevitExecutionQueue<object>(signal);

        var timedOut = queue.EnqueueAsync(_ => 1, CancellationToken.None, "corr-timeout");
        var exception = await Assert.ThrowsAsync<RevitExecutionException>(() => timedOut);
        Assert.Equal(RevitExecutionErrorCodes.SchedulingFailed, exception.ErrorCode);
        Assert.False(timedOut.IsCanceled);
    }

    [Fact]
    public async Task Pending_raise_does_not_duplicate_execution()
    {
        var signal = new RecordingEventSignal
        {
            DefaultResult = ExecutionScheduleResult.Pending
        };
        var queue = new RevitExecutionQueue<object>(signal);
        var runs = 0;

        var first = queue.EnqueueAsync(_ => Interlocked.Increment(ref runs), CancellationToken.None);
        var second = queue.EnqueueAsync(_ => Interlocked.Increment(ref runs), CancellationToken.None);

        Assert.Equal(1, signal.Count);
        Assert.Equal(ExecutionScheduleResult.Pending, Assert.Single(signal.History));

        queue.ExecutePending(Context);
        Assert.Equal(new[] { 1, 2 }, new[] { await first, await second });

        queue.ExecutePending(Context);
        Assert.Equal(2, runs);
    }

    [Fact]
    public async Task Schedule_exception_fails_waiting_work()
    {
        var signal = new ThrowingEventSignal();
        var queue = new RevitExecutionQueue<object>(signal);

        var task = queue.EnqueueAsync(_ => 1, CancellationToken.None);
        var exception = await Assert.ThrowsAsync<RevitExecutionException>(() => task);
        Assert.Equal(RevitExecutionErrorCodes.SchedulingFailed, exception.ErrorCode);
        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    private static int Add(List<int> order, int value)
    {
        order.Add(value);
        return value;
    }

    private sealed class ThrowingEventSignal : IRevitEventSignal
    {
        public ExecutionScheduleResult Schedule() =>
            throw new InvalidOperationException("raise failed");
    }
}
