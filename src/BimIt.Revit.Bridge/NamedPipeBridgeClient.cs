using System.IO.Pipes;
using BimIt.Revit.Bridge.Implementation;
using BimIt.Revit.Contracts;
using StreamJsonRpc;

namespace BimIt.Revit.Bridge;

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

        return await InvokeCapabilityAsync<GetContextResult>(
                "revit.get_context",
                request,
                timeout,
                cancellationToken,
                EnsureGetContextAllowed,
                "The Revit context request timed out.",
                "The Revit context could not be collected.")
            .ConfigureAwait(false);
    }

    public async Task<QueryElementsResult> QueryElementsAsync(
        QueryElementsRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "A positive capability timeout is required.");
        }

        return await InvokeCapabilityAsync<QueryElementsResult>(
                "revit.query_elements",
                request,
                timeout,
                cancellationToken,
                EnsureQueryElementsAllowed,
                "The Revit query request timed out.",
                "The Revit query could not be executed.")
            .ConfigureAwait(false);
    }

    public async Task<GetElementsResult> GetElementsAsync(
        GetElementsRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "A positive capability timeout is required.");
        }

        return await InvokeCapabilityAsync<GetElementsResult>(
                "revit.get_elements",
                request,
                timeout,
                cancellationToken,
                EnsureGetElementsAllowed,
                "The Revit inspection request timed out.",
                "The Revit inspection could not be executed.")
            .ConfigureAwait(false);
    }

    public async Task<DescribeParametersResult> DescribeParametersAsync(
        DescribeParametersRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "A positive capability timeout is required.");
        }

        return await InvokeCapabilityAsync<DescribeParametersResult>(
                "revit.describe_parameters",
                request,
                timeout,
                cancellationToken,
                EnsureDescribeParametersAllowed,
                "The Revit parameter discovery request timed out.",
                "The Revit parameter discovery could not be executed.")
            .ConfigureAwait(false);
    }

    public async Task<GetParameterValuesResult> GetParameterValuesAsync(
        GetParameterValuesRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "A positive capability timeout is required.");
        }

        return await InvokeCapabilityAsync<GetParameterValuesResult>(
                "revit.get_parameter_values",
                request,
                timeout,
                cancellationToken,
                EnsureGetParameterValuesAllowed,
                "The Revit parameter value request timed out.",
                "The Revit parameter value read could not be executed.")
            .ConfigureAwait(false);
    }

    public async Task<GetMepTopologyResult> GetMepTopologyAsync(
        GetMepTopologyRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "A positive capability timeout is required.");
        }

        return await InvokeCapabilityAsync<GetMepTopologyResult>(
                "revit.get_mep_topology",
                request,
                timeout,
                cancellationToken,
                EnsureGetMepTopologyAllowed,
                "The Revit MEP topology request timed out.",
                "The Revit MEP topology request could not be executed.")
            .ConfigureAwait(false);
    }

    public async Task<PreviewParameterUpdatesResult> PreviewParameterUpdatesAsync(
        PreviewParameterUpdatesRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "A positive capability timeout is required.");
        }

        return await InvokeCapabilityAsync<PreviewParameterUpdatesResult>(
                "revit.preview_parameter_updates",
                request,
                timeout,
                cancellationToken,
                EnsurePreviewParameterUpdatesAllowed,
                "The Revit parameter update preview request timed out.",
                "The Revit parameter update preview could not be executed.")
            .ConfigureAwait(false);
    }

    public async Task<RequestParameterUpdateReviewResult> RequestParameterUpdateReviewAsync(
        RequestParameterUpdateReviewRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "A positive capability timeout is required.");
        }

        RequestParameterUpdateReviewRequests.Validate(request);
        return await InvokeCapabilityAsync<RequestParameterUpdateReviewResult>(
                "revit.request_parameter_update_review",
                request,
                timeout,
                cancellationToken,
                EnsureRequestParameterUpdateReviewAllowed,
                "The Revit parameter update review request timed out.",
                "The Revit parameter update review request could not be executed.")
            .ConfigureAwait(false);
    }

    public async Task<ApplyParameterUpdatesResult> ApplyParameterUpdatesAsync(
        ApplyParameterUpdatesRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "A positive capability timeout is required.");
        }

        ApplyParameterUpdatesRequests.Validate(request);
        return await InvokeCapabilityAsync<ApplyParameterUpdatesResult>(
                "revit.apply_parameter_updates",
                request,
                timeout,
                cancellationToken,
                EnsureApplyParameterUpdatesAllowed,
                "The Revit parameter update apply request timed out.",
                "The Revit parameter update apply request could not be executed.")
            .ConfigureAwait(false);
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

    private async Task<T> InvokeCapabilityAsync<T>(
        string method,
        object request,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        Action ensureAllowed,
        string timeoutMessage,
        string failedMessage)
    {
        ensureAllowed();

        // Local WaitAsync bounds the caller. StreamJsonRpc 2.25.29 has no
        // OutboundRequestTimeout; cancelling the invoke token tells the remote
        // EXEC queue to skip still-queued work, but we must not await the
        // cancellation acknowledgement or a silent peer can hang the caller.
        using var invokeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var invoke = _rpc.InvokeWithCancellationAsync<T>(method, [request], invokeCts.Token);
        try
        {
            return await invoke.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            CancelUnusableCapabilityRequest(invokeCts, invoke);
            throw new BridgeException(CapabilityErrorCodes.ExecutionTimeout, timeoutMessage, exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            CancelUnusableCapabilityRequest(invokeCts, invoke);
            throw new BridgeException(CapabilityErrorCodes.ExecutionTimeout, timeoutMessage, exception);
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
            throw new BridgeException(CapabilityErrorCodes.ExecutionFailed, failedMessage, exception);
        }
        catch (BridgeException)
        {
            throw;
        }
        catch (IOException exception)
        {
            throw new BridgeException(CapabilityErrorCodes.ExecutionFailed, failedMessage, exception);
        }
    }

    private void EnsureGetContextAllowed()
    {
        EnsureCapabilityReady();
        if (_selectedProtocolVersion is not int version || !BridgeProtocol.SupportsGetContext(version))
        {
            throw new BridgeException(
                BridgeErrorCodes.ProtocolIncompatible,
                "revit.get_context requires a negotiated bridge protocol version that explicitly supports it.");
        }
    }

    private void EnsureQueryElementsAllowed()
    {
        EnsureCapabilityReady();
        if (_selectedProtocolVersion is not int version || !BridgeProtocol.SupportsQueryElements(version))
        {
            throw new BridgeException(
                BridgeErrorCodes.ProtocolIncompatible,
                "revit.query_elements requires a negotiated bridge protocol version that explicitly supports it.");
        }
    }

    private void EnsureGetElementsAllowed()
    {
        EnsureCapabilityReady();
        if (_selectedProtocolVersion is not int version || !BridgeProtocol.SupportsGetElements(version))
        {
            throw new BridgeException(
                BridgeErrorCodes.ProtocolIncompatible,
                "revit.get_elements requires a negotiated bridge protocol version that explicitly supports it.");
        }
    }

    private void EnsureDescribeParametersAllowed()
    {
        EnsureCapabilityReady();
        if (_selectedProtocolVersion is not int version || !BridgeProtocol.SupportsDescribeParameters(version))
        {
            throw new BridgeException(
                BridgeErrorCodes.ProtocolIncompatible,
                "revit.describe_parameters requires a negotiated bridge protocol version that explicitly supports it.");
        }
    }

    private void EnsureGetParameterValuesAllowed()
    {
        EnsureCapabilityReady();
        if (_selectedProtocolVersion is not int version || !BridgeProtocol.SupportsGetParameterValues(version))
        {
            throw new BridgeException(
                BridgeErrorCodes.ProtocolIncompatible,
                "revit.get_parameter_values requires a negotiated bridge protocol version that explicitly supports it.");
        }
    }

    public async Task<GetWarningsResult> GetWarningsAsync(
        GetWarningsRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "A positive capability timeout is required.");
        }

        return await InvokeCapabilityAsync<GetWarningsResult>(
                "revit.get_warnings",
                request,
                timeout,
                cancellationToken,
                EnsureGetWarningsAllowed,
                "The Revit warnings request timed out.",
                "The Revit warnings request could not be executed.")
            .ConfigureAwait(false);
    }

    private void EnsureGetWarningsAllowed()
    {
        EnsureCapabilityReady();
        if (_selectedProtocolVersion is not int version || !BridgeProtocol.SupportsGetWarnings(version))
        {
            throw new BridgeException(
                BridgeErrorCodes.ProtocolIncompatible,
                "revit.get_warnings requires a negotiated bridge protocol version that explicitly supports it.");
        }
    }

    public async Task<GetSelectionResult> GetSelectionAsync(
        GetSelectionRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "A positive capability timeout is required.");
        }

        return await InvokeCapabilityAsync<GetSelectionResult>(
                "revit.get_selection",
                request,
                timeout,
                cancellationToken,
                EnsureGetSelectionAllowed,
                "The Revit selection request timed out.",
                "The Revit selection request could not be executed.")
            .ConfigureAwait(false);
    }

    public async Task<PreviewMepDevicePlacementsResult> PreviewMepDevicePlacementsAsync(
        PreviewMepDevicePlacementsRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "A positive capability timeout is required.");
        }

        return await InvokeCapabilityAsync<PreviewMepDevicePlacementsResult>(
                "revit.preview_mep_device_placements",
                request,
                timeout,
                cancellationToken,
                EnsurePreviewMepDevicePlacementsAllowed,
                "The MEP device placement preview timed out.",
                "The MEP device placement preview could not be executed.")
            .ConfigureAwait(false);
    }

    public async Task<RequestMepDevicePlacementReviewResult> RequestMepDevicePlacementReviewAsync(
        RequestMepDevicePlacementReviewRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "A positive capability timeout is required.");
        }

        return await InvokeCapabilityAsync<RequestMepDevicePlacementReviewResult>(
                "revit.request_mep_device_placement_review",
                request,
                timeout,
                cancellationToken,
                EnsureRequestMepDevicePlacementReviewAllowed,
                "The MEP device placement review request timed out.",
                "The MEP device placement review request could not be executed.")
            .ConfigureAwait(false);
    }

    public async Task<ApplyMepDevicePlacementsResult> ApplyMepDevicePlacementsAsync(
        ApplyMepDevicePlacementsRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "A positive capability timeout is required.");
        }

        return await InvokeCapabilityAsync<ApplyMepDevicePlacementsResult>(
                "revit.apply_mep_device_placements",
                request,
                timeout,
                cancellationToken,
                EnsureApplyMepDevicePlacementsAllowed,
                "The MEP device placement apply timed out.",
                "The MEP device placement apply could not be executed.")
            .ConfigureAwait(false);
    }

    public async Task<GetRoomLayoutsResult> GetRoomLayoutsAsync(
        GetRoomLayoutsRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "A positive capability timeout is required.");
        }

        return await InvokeCapabilityAsync<GetRoomLayoutsResult>(
                "revit.get_room_layouts",
                request,
                timeout,
                cancellationToken,
                EnsureGetRoomLayoutsAllowed,
                "The Revit room layout request timed out.",
                "The Revit room layout request could not be executed.")
            .ConfigureAwait(false);
    }

    private void EnsurePreviewMepDevicePlacementsAllowed()
    {
        EnsureCapabilityReady();
        if (_selectedProtocolVersion is not int version || !BridgeProtocol.SupportsPreviewMepDevicePlacements(version))
        {
            throw new BridgeException(
                BridgeErrorCodes.ProtocolIncompatible,
                "revit.preview_mep_device_placements requires a negotiated bridge protocol version that explicitly supports it.");
        }
    }

    private void EnsureRequestMepDevicePlacementReviewAllowed()
    {
        EnsureCapabilityReady();
        if (_selectedProtocolVersion is not int version || !BridgeProtocol.SupportsRequestMepDevicePlacementReview(version))
        {
            throw new BridgeException(
                BridgeErrorCodes.ProtocolIncompatible,
                "revit.request_mep_device_placement_review requires a negotiated bridge protocol version that explicitly supports it.");
        }
    }

    private void EnsureApplyMepDevicePlacementsAllowed()
    {
        EnsureCapabilityReady();
        if (_selectedProtocolVersion is not int version || !BridgeProtocol.SupportsApplyMepDevicePlacements(version))
        {
            throw new BridgeException(
                BridgeErrorCodes.ProtocolIncompatible,
                "revit.apply_mep_device_placements requires a negotiated bridge protocol version that explicitly supports it.");
        }
    }

    private void EnsureGetSelectionAllowed()
    {
        EnsureCapabilityReady();
        if (_selectedProtocolVersion is not int version || !BridgeProtocol.SupportsGetSelection(version))
        {
            throw new BridgeException(
                BridgeErrorCodes.ProtocolIncompatible,
                "revit.get_selection requires a negotiated bridge protocol version that explicitly supports it.");
        }
    }

    public async Task<GetMepDeviceTypesResult> GetMepDeviceTypesAsync(
        GetMepDeviceTypesRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "A positive capability timeout is required.");
        }

        return await InvokeCapabilityAsync<GetMepDeviceTypesResult>(
                "revit.get_mep_device_types",
                request,
                timeout,
                cancellationToken,
                EnsureGetMepDeviceTypesAllowed,
                "The MEP device type catalog request timed out.",
                "The MEP device type catalog could not be read.")
            .ConfigureAwait(false);
    }

    public async Task<PreviewRoomPatternPlacementsResult> PreviewRoomPatternPlacementsAsync(
        PreviewRoomPatternPlacementsRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "A positive capability timeout is required.");
        }

        return await InvokeCapabilityAsync<PreviewRoomPatternPlacementsResult>(
                "revit.preview_room_pattern_placements",
                request,
                timeout,
                cancellationToken,
                EnsurePreviewRoomPatternPlacementsAllowed,
                "The room pattern placement preview timed out.",
                "The room pattern placement preview could not be created.")
            .ConfigureAwait(false);
    }

    public async Task<ReadSpatialRoomsResult> ReadSpatialRoomsAsync(
        ReadSpatialRoomsRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "A positive capability timeout is required.");
        }

        return await InvokeCapabilityAsync<ReadSpatialRoomsResult>(
                "revit.read_spatial_rooms",
                request,
                timeout,
                cancellationToken,
                EnsureReadSpatialRoomsAllowed,
                "The spatial room request timed out.",
                "The spatial room request could not be executed.")
            .ConfigureAwait(false);
    }

    private void EnsureGetRoomLayoutsAllowed()
    {
        EnsureCapabilityReady();
        if (_selectedProtocolVersion is not int version || !BridgeProtocol.SupportsGetRoomLayouts(version))
        {
            throw new BridgeException(
                BridgeErrorCodes.ProtocolIncompatible,
                "revit.get_room_layouts requires a negotiated bridge protocol version that explicitly supports it.");
        }
    }

    private void EnsureGetMepDeviceTypesAllowed()
    {
        EnsureCapabilityReady();
        if (_selectedProtocolVersion is not int version || !BridgeProtocol.SupportsGetMepDeviceTypes(version))
        {
            throw new BridgeException(
                BridgeErrorCodes.ProtocolIncompatible,
                "revit.get_mep_device_types requires a negotiated bridge protocol version that explicitly supports it.");
        }
    }

    private void EnsurePreviewRoomPatternPlacementsAllowed()
    {
        EnsureCapabilityReady();
        if (_selectedProtocolVersion is not int version || !BridgeProtocol.SupportsPreviewRoomPatternPlacements(version))
        {
            throw new BridgeException(
                BridgeErrorCodes.ProtocolIncompatible,
                "revit.preview_room_pattern_placements requires a negotiated bridge protocol version that explicitly supports it.");
        }
    }

    private void EnsureReadSpatialRoomsAllowed()
    {
        EnsureCapabilityReady();
        if (_selectedProtocolVersion is not int version || !BridgeProtocol.SupportsReadSpatialRooms(version))
        {
            throw new BridgeException(
                BridgeErrorCodes.ProtocolIncompatible,
                "revit.read_spatial_rooms requires a negotiated bridge protocol version that explicitly supports it.");
        }
    }

    private void EnsureGetMepTopologyAllowed()
    {
        EnsureCapabilityReady();
        if (_selectedProtocolVersion is not int version || !BridgeProtocol.SupportsGetMepTopology(version))
        {
            throw new BridgeException(
                BridgeErrorCodes.ProtocolIncompatible,
                "revit.get_mep_topology requires a negotiated bridge protocol version that explicitly supports it.");
        }
    }

    private void EnsurePreviewParameterUpdatesAllowed()
    {
        EnsureCapabilityReady();
        if (_selectedProtocolVersion is not int version || !BridgeProtocol.SupportsPreviewParameterUpdates(version))
        {
            throw new BridgeException(
                BridgeErrorCodes.ProtocolIncompatible,
                "revit.preview_parameter_updates requires a negotiated bridge protocol version that explicitly supports it.");
        }
    }

    private void EnsureRequestParameterUpdateReviewAllowed()
    {
        EnsureCapabilityReady();
        if (_selectedProtocolVersion is not int version || !BridgeProtocol.SupportsRequestParameterUpdateReview(version))
        {
            throw new BridgeException(
                BridgeErrorCodes.ProtocolIncompatible,
                "revit.request_parameter_update_review requires a negotiated bridge protocol version that explicitly supports it.");
        }
    }

    private void EnsureApplyParameterUpdatesAllowed()
    {
        EnsureCapabilityReady();
        if (_selectedProtocolVersion is not int version || !BridgeProtocol.SupportsApplyParameterUpdates(version))
        {
            throw new BridgeException(
                BridgeErrorCodes.ProtocolIncompatible,
                "revit.apply_parameter_updates requires a negotiated bridge protocol version that explicitly supports it.");
        }
    }

    private void EnsureCapabilityReady()
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
