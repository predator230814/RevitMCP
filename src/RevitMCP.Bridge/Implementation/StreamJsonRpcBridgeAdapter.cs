using RevitMCP.Contracts;
using StreamJsonRpc;

namespace RevitMCP.Bridge.Implementation;

internal sealed class StreamJsonRpcBridgeAdapter
{
    private readonly IRevitBridgeService _handshake;
    private readonly IRevitCapabilityService? _capability;

    public StreamJsonRpcBridgeAdapter(IRevitBridgeService handshake, IRevitCapabilityService? capability)
    {
        _handshake = handshake;
        _capability = capability;
    }

    [JsonRpcMethod("bridge.handshake")]
    public async Task<BridgeHandshakeResult> HandshakeAsync(BridgeHandshakeRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await _handshake.HandshakeAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (BridgeException exception)
        {
            throw StreamJsonRpcExceptionMapper.ToLocalRpc(exception);
        }
    }

    [JsonRpcMethod("revit.get_context")]
    public async Task<GetContextResult> GetContextAsync(GetContextRequest request, CancellationToken cancellationToken)
    {
        if (_capability is null)
        {
            throw StreamJsonRpcExceptionMapper.ToLocalRpc(
                new BridgeException(
                    BridgeErrorCodes.ProtocolIncompatible,
                    "revit.get_context is not available on this endpoint."));
        }

        try
        {
            return await _capability.GetContextAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (BridgeException exception)
        {
            throw StreamJsonRpcExceptionMapper.ToLocalRpc(exception);
        }
        catch (Exception)
        {
            throw StreamJsonRpcExceptionMapper.ToLocalRpc(
                new BridgeException(
                    CapabilityErrorCodes.ExecutionFailed,
                    "The Revit context could not be collected."));
        }
    }
}
