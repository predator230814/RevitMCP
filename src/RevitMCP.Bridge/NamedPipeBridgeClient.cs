using System.IO.Pipes;
using RevitMCP.Bridge.Implementation;
using RevitMCP.Contracts;
using StreamJsonRpc;

namespace RevitMCP.Bridge;

public sealed class NamedPipeBridgeClient : IRevitBridgeClient
{
    private readonly JsonRpc _rpc;
    private readonly NamedPipeClientStream _pipe;
    private readonly TimeSpan _handshakeTimeout;

    private NamedPipeBridgeClient(JsonRpc rpc, NamedPipeClientStream pipe, TimeSpan handshakeTimeout)
    {
        _rpc = rpc;
        _pipe = pipe;
        _handshakeTimeout = handshakeTimeout;
    }

    public static async Task<NamedPipeBridgeClient> ConnectAsync(string pipeName, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var pipe = JsonRpcFactory.CreateClient(pipeName);
        try
        {
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCts.CancelAfter(timeout);
            try
            {
                await pipe.ConnectAsync(connectCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                throw new BridgeException(BridgeErrorCodes.HandshakeTimeout, "The Named Pipe connection timed out.", exception);
            }

            var rpc = JsonRpcFactory.Create(pipe, localTarget: null);
            return new NamedPipeBridgeClient(rpc, pipe, timeout);
        }
        catch
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task<BridgeHandshakeResult> HandshakeAsync(BridgeHandshakeRequest request, CancellationToken cancellationToken)
    {
        using var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        handshakeCts.CancelAfter(_handshakeTimeout);

        try
        {
            return await _rpc.InvokeWithCancellationAsync<BridgeHandshakeResult>(
                    "bridge.handshake",
                    [request],
                    handshakeCts.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BridgeException(BridgeErrorCodes.HandshakeTimeout, "The bridge handshake timed out.", exception);
        }
        catch (RemoteInvocationException exception)
        {
            throw StreamJsonRpcExceptionMapper.FromRemote(exception);
        }
        catch (ConnectionLostException exception)
        {
            throw new BridgeException(BridgeErrorCodes.HandshakeFailed, "The bridge connection was lost during handshake.", exception);
        }
        catch (BridgeException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or TimeoutException)
        {
            throw new BridgeException(BridgeErrorCodes.HandshakeFailed, "The bridge handshake could not be completed.", exception);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _rpc.Dispose();
        await _pipe.DisposeAsync().ConfigureAwait(false);
    }
}
