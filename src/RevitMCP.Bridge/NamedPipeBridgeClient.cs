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
    private int? _selectedProtocolVersion;
    private bool _handshakeCompleted;
    private bool _capabilityUnusable;

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
            catch (Exception exception) when (exception is IOException or TimeoutException)
            {
                throw new BridgeException(BridgeErrorCodes.HandshakeTimeout, "The Named Pipe endpoint could not be reached.", exception);
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
        try
        {
            // 2.25.29 has no OutboundRequestTimeout; a CancelAfter token on
            // Invoke waits for a silent peer to ack cancel. WaitAsync does not.
            var result = await _rpc.InvokeWithCancellationAsync<BridgeHandshakeResult>(
                    "bridge.handshake",
                    [request],
                    cancellationToken)
                .WaitAsync(_handshakeTimeout, cancellationToken)
                .ConfigureAwait(false);

            _handshakeCompleted = true;
            _selectedProtocolVersion = result.SelectedProtocolVersion;
            return result;
        }
        catch (TimeoutException exception)
        {
            throw new BridgeException(BridgeErrorCodes.HandshakeTimeout, "The bridge handshake timed out.", exception);
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
        catch (IOException exception)
        {
            throw new BridgeException(BridgeErrorCodes.HandshakeFailed, "The bridge handshake could not be completed.", exception);
        }
    }

    public async Task<GetContextResult> GetContextAsync(
        GetContextRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "A positive capability timeout is required.");
        }

        EnsureCapabilityAllowed();

        // Local WaitAsync bounds the caller. StreamJsonRpc 2.25.29 has no
        // OutboundRequestTimeout; cancelling the invoke token tells the remote
        // EXEC queue to skip still-queued work, but we must not await the
        // cancellation acknowledgement or a silent peer can hang the caller.
        using var invokeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var invoke = _rpc.InvokeWithCancellationAsync<GetContextResult>(
            "revit.get_context",
            [request],
            invokeCts.Token);
        try
        {
            return await invoke.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            CancelUnusableCapabilityRequest(invokeCts, invoke);
            throw new BridgeException(CapabilityErrorCodes.ExecutionTimeout, "The Revit context request timed out.", exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            CancelUnusableCapabilityRequest(invokeCts, invoke);
            throw new BridgeException(CapabilityErrorCodes.ExecutionTimeout, "The Revit context request timed out.", exception);
        }
        catch (OperationCanceledException)
        {
            ObserveIncomplete(invoke);
            throw;
        }
        catch (RemoteInvocationException exception)
        {
            throw StreamJsonRpcExceptionMapper.FromRemote(exception, CapabilityErrorCodes.ExecutionFailed);
        }
        catch (ConnectionLostException exception)
        {
            throw new BridgeException(CapabilityErrorCodes.ExecutionFailed, "The bridge connection was lost during the context request.", exception);
        }
        catch (BridgeException)
        {
            throw;
        }
        catch (IOException exception)
        {
            throw new BridgeException(CapabilityErrorCodes.ExecutionFailed, "The Revit context could not be collected.", exception);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _rpc.Dispose();
        await _pipe.DisposeAsync().ConfigureAwait(false);
    }

    private void CancelUnusableCapabilityRequest(CancellationTokenSource invokeCts, Task invoke)
    {
        _capabilityUnusable = true;
        invokeCts.Cancel();
        ObserveIncomplete(invoke);
    }

    private void EnsureCapabilityAllowed()
    {
        if (_capabilityUnusable)
        {
            throw new BridgeException(
                CapabilityErrorCodes.ExecutionFailed,
                "This bridge connection is no longer usable after a capability timeout.");
        }

        if (!_handshakeCompleted)
        {
            throw new BridgeException(
                BridgeErrorCodes.HandshakeFailed,
                "A successful handshake is required before capability requests.");
        }

        if (_selectedProtocolVersion is null || _selectedProtocolVersion.Value < BridgeProtocol.GetContextVersion)
        {
            throw new BridgeException(
                BridgeErrorCodes.ProtocolIncompatible,
                "revit.get_context requires negotiated bridge protocol version 2.");
        }
    }

    private static void ObserveIncomplete(Task task)
    {
        if (task.IsCompleted)
        {
            _ = task.Exception;
            return;
        }

        _ = task.ContinueWith(
            completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
