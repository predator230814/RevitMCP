using BimIt.Revit.Contracts;

namespace BimIt.Revit.Bridge;

public interface IRevitBridgeService
{
    Task<BridgeHandshakeResult> HandshakeAsync(BridgeHandshakeRequest request, CancellationToken cancellationToken);
}
