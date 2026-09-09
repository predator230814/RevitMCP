namespace RevitMCP.Bridge.Tests;

internal sealed class FakeProcessInspector : IProcessInspector
{
    private readonly Dictionary<int, DateTimeOffset> _processes = [];

    public void Add(int processId, DateTimeOffset startTimeUtc)
    {
        _processes[processId] = startTimeUtc;
    }

    public ProcessSnapshot? GetProcess(int processId)
    {
        return _processes.TryGetValue(processId, out var startTime)
            ? new ProcessSnapshot(processId, startTime)
            : null;
    }
}
