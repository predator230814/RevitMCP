namespace RevitMCP.Bridge.Tests;

internal sealed class FakeBridgeClientFactory : IBridgeClientFactory
{
    public Func<string, Task<IRevitBridgeClient>> Connect { get; set; } =
        _ => throw new InvalidOperationException("Connect was not configured.");

    public Task<IRevitBridgeClient> ConnectAsync(string pipeName, TimeSpan timeout, CancellationToken cancellationToken)
    {
        _ = timeout;
        _ = cancellationToken;
        return Connect(pipeName);
    }
}
