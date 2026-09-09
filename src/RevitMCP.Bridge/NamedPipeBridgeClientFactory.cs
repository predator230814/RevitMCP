namespace RevitMCP.Bridge;

public sealed class NamedPipeBridgeClientFactory : IBridgeClientFactory
{
    public Task<IRevitBridgeClient> ConnectAsync(string pipeName, TimeSpan timeout, CancellationToken cancellationToken)
    {
        return ConnectCoreAsync(pipeName, timeout, cancellationToken);
    }

    private static async Task<IRevitBridgeClient> ConnectCoreAsync(string pipeName, TimeSpan timeout, CancellationToken cancellationToken)
    {
        return await NamedPipeBridgeClient.ConnectAsync(pipeName, timeout, cancellationToken).ConfigureAwait(false);
    }
}
