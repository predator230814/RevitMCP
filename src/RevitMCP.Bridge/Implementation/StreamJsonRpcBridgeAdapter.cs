using RevitMCP.Contracts;
using StreamJsonRpc;

namespace RevitMCP.Bridge.Implementation;

internal sealed class StreamJsonRpcBridgeAdapter
{
    private readonly IRevitBridgeService _handshake;
    private readonly IRevitCapabilityService? _capability;
    private readonly IRevitQueryElementsService? _query;
    private readonly IRevitGetElementsService? _getElements;
    private int _selectedProtocolVersion;

    public StreamJsonRpcBridgeAdapter(
        IRevitBridgeService handshake,
        IRevitCapabilityService? capability,
        IRevitQueryElementsService? query = null,
        IRevitGetElementsService? getElements = null)
    {
        _handshake = handshake;
        _capability = capability;
        _query = query;
        _getElements = getElements;
    }

    [JsonRpcMethod("bridge.handshake")]
    public async Task<BridgeHandshakeResult> HandshakeAsync(BridgeHandshakeRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _handshake.HandshakeAsync(request, cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _selectedProtocolVersion, result.SelectedProtocolVersion);
            return result;
        }
        catch (BridgeException exception)
        {
            throw StreamJsonRpcExceptionMapper.ToLocalRpc(exception);
        }
    }

    [JsonRpcMethod("revit.get_context")]
    public async Task<GetContextResult> GetContextAsync(GetContextRequest request, CancellationToken cancellationToken)
    {
        EnsureCapabilityAllowed(BridgeProtocol.SupportsGetContext, "revit.get_context is not available on this connection.");
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
        EnsureCapabilityAllowed(BridgeProtocol.SupportsQueryElements, "revit.query_elements is not available on this connection.");
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

    [JsonRpcMethod("revit.get_elements")]
    public async Task<GetElementsResult> GetElementsAsync(GetElementsRequest request, CancellationToken cancellationToken)
    {
        EnsureCapabilityAllowed(BridgeProtocol.SupportsGetElements, "revit.get_elements is not available on this connection.");
        if (_getElements is null)
        {
            throw StreamJsonRpcExceptionMapper.ToLocalRpc(
                new BridgeException(
                    BridgeErrorCodes.ProtocolIncompatible,
                    "revit.get_elements is not available on this endpoint."));
        }

        try
        {
            return await _getElements.GetElementsAsync(request, cancellationToken).ConfigureAwait(false);
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
                    "The Revit inspection could not be executed."));
        }
    }

    private void EnsureCapabilityAllowed(Func<int, bool> isSupported, string incompatibleMessage)
    {
        var selected = Volatile.Read(ref _selectedProtocolVersion);
        if (selected == 0)
        {
            throw StreamJsonRpcExceptionMapper.ToLocalRpc(
                new BridgeException(
                    BridgeErrorCodes.HandshakeFailed,
                    "A successful handshake is required before capability requests."));
        }

        if (!isSupported(selected))
        {
            throw StreamJsonRpcExceptionMapper.ToLocalRpc(
                new BridgeException(BridgeErrorCodes.ProtocolIncompatible, incompatibleMessage));
        }
    }
}
