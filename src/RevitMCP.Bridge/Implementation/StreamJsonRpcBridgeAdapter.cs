using RevitMCP.Contracts;
using StreamJsonRpc;

namespace RevitMCP.Bridge.Implementation;

internal sealed class StreamJsonRpcBridgeAdapter
{
    private readonly IRevitBridgeService _handshake;
    private readonly IRevitCapabilityService? _capability;
    private readonly IRevitQueryElementsService? _query;

    public StreamJsonRpcBridgeAdapter(
        IRevitBridgeService handshake,
        IRevitCapabilityService? capability,
        IRevitQueryElementsService? query = null)
    {
        _handshake = handshake;
        _capability = capability;
        _query = query;
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

    [JsonRpcMethod("revit.query_elements")]
    public async Task<QueryElementsResult> QueryElementsAsync(QueryElementsRequest request, CancellationToken cancellationToken)
    {
        if (_query is null)
        {
            throw StreamJsonRpcExceptionMapper.ToLocalRpc(
                new BridgeException(
                    BridgeErrorCodes.ProtocolIncompatible,
                    "revit.query_elements is not available on this endpoint."));
        }

        try
        {
            return await _query.QueryElementsAsync(request, cancellationToken).ConfigureAwait(false);
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
                    "The Revit query could not be executed."));
        }
    }
}
