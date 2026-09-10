using RevitMCP.Contracts;

namespace RevitMCP.Bridge;

public interface IRevitBridgeClient : IAsyncDisposable
{
    Task<BridgeHandshakeResult> HandshakeAsync(BridgeHandshakeRequest request, CancellationToken cancellationToken);

    Task<GetContextResult> GetContextAsync(GetContextRequest request, TimeSpan timeout, CancellationToken cancellationToken);
}
