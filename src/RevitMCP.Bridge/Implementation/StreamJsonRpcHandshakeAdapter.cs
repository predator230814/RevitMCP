using RevitMCP.Contracts;
using StreamJsonRpc;

namespace RevitMCP.Bridge.Implementation;

internal sealed class StreamJsonRpcHandshakeAdapter
{
    private readonly IRevitBridgeService _service;

    public StreamJsonRpcHandshakeAdapter(IRevitBridgeService service)
    {
        _service = service;
    }

    [JsonRpcMethod("bridge.handshake")]
    public async Task<BridgeHandshakeResult> HandshakeAsync(BridgeHandshakeRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await _service.HandshakeAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (BridgeException exception)
        {
            throw StreamJsonRpcExceptionMapper.ToLocalRpc(exception);
        }
    }
}
