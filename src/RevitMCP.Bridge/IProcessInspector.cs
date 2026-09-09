namespace RevitMCP.Bridge;

public interface IProcessInspector
{
    ProcessSnapshot? GetProcess(int processId);
}
