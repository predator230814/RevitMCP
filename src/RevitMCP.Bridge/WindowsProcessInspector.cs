using System.Diagnostics;

namespace RevitMCP.Bridge;

public sealed class WindowsProcessInspector : IProcessInspector
{
    public ProcessSnapshot? GetProcess(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return new ProcessSnapshot(processId, new DateTimeOffset(process.StartTime).ToUniversalTime());
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
