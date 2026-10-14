namespace BimIt.Revit.Addin.Assistant;

internal enum AssistantProcessPhase
{
    NotStarted,
    Starting,
    Ready,
    Failed,
    Stopped,
}

internal interface IAssistantProcessHandle : IDisposable
{
    bool HasExited { get; }

    void Kill();
}

internal interface IAssistantProcessFactory
{
    IAssistantProcessHandle Start(string fileName, string arguments);
}

internal sealed class AssistantProcessSupervisor
{
    private readonly IAssistantProcessFactory _factory;
    private readonly object _gate = new();
    private IAssistantProcessHandle? _process;

    public AssistantProcessSupervisor(IAssistantProcessFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    public AssistantProcessPhase Phase { get; private set; } = AssistantProcessPhase.NotStarted;

    public int StartCount { get; private set; }

    public bool IsAlive
    {
        get
        {
            lock (_gate)
            {
                return _process is { HasExited: false };
            }
        }
    }

    public bool ObserveExit()
    {
        lock (_gate)
        {
            if (_process is not { HasExited: true } || Phase is AssistantProcessPhase.Stopped or AssistantProcessPhase.NotStarted)
            {
                return false;
            }

            if (Phase == AssistantProcessPhase.Failed)
            {
                return false;
            }

            Phase = AssistantProcessPhase.Failed;
            Failure = "The Assistant process exited.";
            return true;
        }
    }

    public string? Failure { get; private set; }

    public void OnRevitStartup()
    {
    }

    public bool EnsureStarted(string fileName, string arguments)
    {
        lock (_gate)
        {
            if (_process is { HasExited: false })
            {
                return true;
            }

            Phase = AssistantProcessPhase.Starting;
            try
            {
                _process?.Dispose();
                _process = _factory.Start(fileName, arguments);
                StartCount++;
                Phase = AssistantProcessPhase.Ready;
                Failure = null;
                return true;
            }
            catch (Exception exception)
            {
                Phase = AssistantProcessPhase.Failed;
                Failure = exception.Message;
                return false;
            }
        }
    }

    public void MarkReady()
    {
        lock (_gate)
        {
            if (Phase == AssistantProcessPhase.Starting || Phase == AssistantProcessPhase.Ready)
            {
                Phase = AssistantProcessPhase.Ready;
            }
        }
    }

    public void MarkFailed(string failure)
    {
        lock (_gate)
        {
            Phase = AssistantProcessPhase.Failed;
            Failure = failure;
            if (_process is { HasExited: false })
            {
                try
                {
                    _process.Kill();
                }
                catch (Exception)
                {
                    // The capability host must stay up if the Assistant cannot be killed.
                }
            }
        }
    }

    public void Shutdown()
    {
        lock (_gate)
        {
            if (_process is not null)
            {
                try
                {
                    if (!_process.HasExited)
                    {
                        _process.Kill();
                    }
                }
                catch (Exception)
                {
                    // Shutdown still completes so Revit can exit.
                }

                _process.Dispose();
                _process = null;
            }

            Phase = AssistantProcessPhase.Stopped;
        }
    }
}
