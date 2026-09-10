namespace RevitMCP.Server;

internal interface IWindowsSession
{
    int CurrentSessionId { get; }
}

internal sealed class ProcessWindowsSession : IWindowsSession
{
    public int CurrentSessionId => System.Diagnostics.Process.GetCurrentProcess().SessionId;
}
