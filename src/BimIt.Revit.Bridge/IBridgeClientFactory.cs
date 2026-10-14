namespace BimIt.Revit.Bridge;

public interface IBridgeClientFactory
{
    Task<IRevitBridgeClient> ConnectAsync(string pipeName, TimeSpan timeout, CancellationToken cancellationToken);
}
