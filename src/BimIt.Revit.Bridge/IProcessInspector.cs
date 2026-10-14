namespace BimIt.Revit.Bridge;

public interface IProcessInspector
{
    ProcessSnapshot? GetProcess(int processId);
}
