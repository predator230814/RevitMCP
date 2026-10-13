namespace BimIt.Revit.Addin.Assistant;

internal sealed record AssistantIpcTimeouts(TimeSpan Connect, TimeSpan Workflow, TimeSpan Shutdown, TimeSpan ProcessWatch)
{
    public static AssistantIpcTimeouts Default { get; } = new(
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(3),
        TimeSpan.FromSeconds(1));
}
