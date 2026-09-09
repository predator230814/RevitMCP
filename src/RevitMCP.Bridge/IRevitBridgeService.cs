using RevitMCP.Contracts;

namespace RevitMCP.Bridge;

public interface IRevitBridgeService
{
    Task<BridgeHandshakeResult> HandshakeAsync(BridgeHandshakeRequest request, CancellationToken cancellationToken);
}
