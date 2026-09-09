using System.ComponentModel;
using System.Diagnostics;

namespace RevitMCP.Bridge;

public sealed class WindowsProcessInspector : IProcessInspector
{
    private readonly Func<int, Process> _getProcessById;

    public WindowsProcessInspector()
        : this(Process.GetProcessById)
    {
    }

    public WindowsProcessInspector(Func<int, Process> getProcessById)
    {
        _getProcessById = getProcessById;
    }

    public ProcessSnapshot? GetProcess(int processId)
    {
        try
        {
            using var process = _getProcessById(processId);
            return new ProcessSnapshot(processId, new DateTimeOffset(process.StartTime).ToUniversalTime());
        }
        catch (Exception exception) when (IsNonLiveProcess(exception))
        {
            return null;
        }
    }

    private static bool IsNonLiveProcess(Exception exception)
    {
        return exception is ArgumentException
            or InvalidOperationException
            or NotSupportedException
            or Win32Exception;
    }
}
