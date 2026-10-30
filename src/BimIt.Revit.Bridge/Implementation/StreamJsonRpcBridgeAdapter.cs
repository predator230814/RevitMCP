using BimIt.Revit.Contracts;
using StreamJsonRpc;

namespace BimIt.Revit.Bridge.Implementation;

internal sealed class StreamJsonRpcBridgeAdapter
{
    private readonly IRevitBridgeService _handshake;
    private readonly IRevitCapabilityService? _capability;
    private readonly IRevitQueryElementsService? _query;
    private readonly IRevitGetElementsService? _getElements;
    private readonly IRevitDescribeParametersService? _describeParameters;
    private readonly IRevitGetParameterValuesService? _getParameterValues;
    private readonly IRevitGetMepTopologyService? _getMepTopology;
    private readonly IRevitPreviewParameterUpdatesService? _previewParameterUpdates;
    private readonly IRevitRequestParameterUpdateReviewService? _requestParameterUpdateReview;
    private readonly IRevitApplyParameterUpdatesService? _applyParameterUpdates;
    private readonly IRevitGetWarningsService? _getWarnings;
    private readonly IRevitGetSelectionService? _getSelection;
    private readonly IRevitPreviewMepDevicePlacementsService? _previewMepDevicePlacements;
    private readonly IRevitRequestMepDevicePlacementReviewService? _requestMepDevicePlacementReview;
    private readonly IRevitApplyMepDevicePlacementsService? _applyMepDevicePlacements;
    private readonly IRevitGetRoomLayoutsService? _getRoomLayouts;
    private readonly IRevitGetMepDeviceTypesService? _getMepDeviceTypes;
    private readonly IRevitPreviewRoomPatternPlacementsService? _previewRoomPatternPlacements;
    private readonly IRevitReadSpatialRoomsService? _readSpatialRooms;
    private int _selectedProtocolVersion;

    public StreamJsonRpcBridgeAdapter(
        IRevitBridgeService handshake,
        IRevitCapabilityService? capability,
        IRevitQueryElementsService? query = null,
        IRevitGetElementsService? getElements = null,
        IRevitDescribeParametersService? describeParameters = null,
        IRevitGetParameterValuesService? getParameterValues = null,
        IRevitGetMepTopologyService? getMepTopology = null,
        IRevitPreviewParameterUpdatesService? previewParameterUpdates = null,
        IRevitRequestParameterUpdateReviewService? requestParameterUpdateReview = null,
        IRevitApplyParameterUpdatesService? applyParameterUpdates = null,
        IRevitGetWarningsService? getWarnings = null,
        IRevitGetSelectionService? getSelection = null,
        IRevitPreviewMepDevicePlacementsService? previewMepDevicePlacements = null,
        IRevitRequestMepDevicePlacementReviewService? requestMepDevicePlacementReview = null,
        IRevitApplyMepDevicePlacementsService? applyMepDevicePlacements = null,
        IRevitGetRoomLayoutsService? getRoomLayouts = null,
        IRevitGetMepDeviceTypesService? getMepDeviceTypes = null,
        IRevitPreviewRoomPatternPlacementsService? previewRoomPatternPlacements = null,
        IRevitReadSpatialRoomsService? readSpatialRooms = null)
    {
        _handshake = handshake;
        _capability = capability;
        _query = query;
        _getElements = getElements;
        _describeParameters = describeParameters;
        _getParameterValues = getParameterValues;
        _getMepTopology = getMepTopology;
        _previewParameterUpdates = previewParameterUpdates;
        _requestParameterUpdateReview = requestParameterUpdateReview;
        _applyParameterUpdates = applyParameterUpdates;
        _getWarnings = getWarnings;
        _getSelection = getSelection;
        _previewMepDevicePlacements = previewMepDevicePlacements;
        _requestMepDevicePlacementReview = requestMepDevicePlacementReview;
        _applyMepDevicePlacements = applyMepDevicePlacements;
        _getRoomLayouts = getRoomLayouts;
        _getMepDeviceTypes = getMepDeviceTypes;
        _previewRoomPatternPlacements = previewRoomPatternPlacements;
        _readSpatialRooms = readSpatialRooms;
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
            var result = await _capability.GetContextAsync(request, cancellationToken).ConfigureAwait(false);
            return BridgeContextProtocol.ForSelectedVersion(result, Volatile.Read(ref _selectedProtocolVersion));
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
            EnsureQueryFiltersMatchSelectedProtocol(request);
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

    [JsonRpcMethod("revit.describe_parameters")]
    public async Task<DescribeParametersResult> DescribeParametersAsync(
        DescribeParametersRequest request,
        CancellationToken cancellationToken)
    {
        EnsureCapabilityAllowed(
            BridgeProtocol.SupportsDescribeParameters,
            "revit.describe_parameters is not available on this connection.");
        if (_describeParameters is null)
        {
            throw StreamJsonRpcExceptionMapper.ToLocalRpc(
                new BridgeException(
                    BridgeErrorCodes.ProtocolIncompatible,
                    "revit.describe_parameters is not available on this endpoint."));
        }

        try
        {
            return await _describeParameters.DescribeParametersAsync(request, cancellationToken).ConfigureAwait(false);
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
                    "The Revit parameter discovery could not be executed."));
        }
    }

    [JsonRpcMethod("revit.get_parameter_values")]
    public async Task<GetParameterValuesResult> GetParameterValuesAsync(
        GetParameterValuesRequest request,
        CancellationToken cancellationToken)
    {
        EnsureCapabilityAllowed(
            BridgeProtocol.SupportsGetParameterValues,
            "revit.get_parameter_values is not available on this connection.");
        if (_getParameterValues is null)
        {
            throw StreamJsonRpcExceptionMapper.ToLocalRpc(
                new BridgeException(
                    BridgeErrorCodes.ProtocolIncompatible,
                    "revit.get_parameter_values is not available on this endpoint."));
        }

        try
        {
            return await _getParameterValues.GetParameterValuesAsync(request, cancellationToken).ConfigureAwait(false);
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
                    "The Revit parameter value read could not be executed."));
        }
    }

    [JsonRpcMethod("revit.get_mep_topology")]
    public async Task<GetMepTopologyResult> GetMepTopologyAsync(
        GetMepTopologyRequest request,
        CancellationToken cancellationToken)
    {
        EnsureCapabilityAllowed(
            BridgeProtocol.SupportsGetMepTopology,
            "revit.get_mep_topology is not available on this connection.");
        if (_getMepTopology is null)
        {
            throw StreamJsonRpcExceptionMapper.ToLocalRpc(
                new BridgeException(
                    BridgeErrorCodes.ProtocolIncompatible,
                    "revit.get_mep_topology is not available on this endpoint."));
        }

        try
        {
            return await _getMepTopology.GetMepTopologyAsync(request, cancellationToken).ConfigureAwait(false);
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
                    "The Revit MEP topology request could not be executed."));
        }
    }

    [JsonRpcMethod("revit.preview_parameter_updates")]
    public async Task<PreviewParameterUpdatesResult> PreviewParameterUpdatesAsync(
        PreviewParameterUpdatesRequest request,
        CancellationToken cancellationToken)
    {
        EnsureCapabilityAllowed(
            BridgeProtocol.SupportsPreviewParameterUpdates,
            "revit.preview_parameter_updates is not available on this connection.");
        if (_previewParameterUpdates is null)
        {
            throw StreamJsonRpcExceptionMapper.ToLocalRpc(
                new BridgeException(
                    BridgeErrorCodes.ProtocolIncompatible,
                    "revit.preview_parameter_updates is not available on this endpoint."));
        }

        try
        {
            return await _previewParameterUpdates.PreviewParameterUpdatesAsync(request, cancellationToken).ConfigureAwait(false);
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
                    "The Revit parameter update preview could not be executed."));
        }
    }

    [JsonRpcMethod("revit.request_parameter_update_review")]
    public async Task<RequestParameterUpdateReviewResult> RequestParameterUpdateReviewAsync(
        RequestParameterUpdateReviewRequest request,
        CancellationToken cancellationToken)
    {
        EnsureCapabilityAllowed(
            BridgeProtocol.SupportsRequestParameterUpdateReview,
            "revit.request_parameter_update_review is not available on this connection.");
        if (_requestParameterUpdateReview is null)
        {
            throw StreamJsonRpcExceptionMapper.ToLocalRpc(
                new BridgeException(
                    BridgeErrorCodes.ProtocolIncompatible,
                    "revit.request_parameter_update_review is not available on this endpoint."));
        }

        try
        {
            RequestParameterUpdateReviewRequests.Validate(request);
            return await _requestParameterUpdateReview.RequestParameterUpdateReviewAsync(request, cancellationToken).ConfigureAwait(false);
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
                    "The Revit parameter update review request could not be executed."));
        }
    }

    [JsonRpcMethod("revit.apply_parameter_updates")]
    public async Task<ApplyParameterUpdatesResult> ApplyParameterUpdatesAsync(
        ApplyParameterUpdatesRequest request,
        CancellationToken cancellationToken)
    {
        EnsureCapabilityAllowed(
            BridgeProtocol.SupportsApplyParameterUpdates,
            "revit.apply_parameter_updates is not available on this connection.");
        if (_applyParameterUpdates is null)
        {
            throw StreamJsonRpcExceptionMapper.ToLocalRpc(
                new BridgeException(
                    BridgeErrorCodes.ProtocolIncompatible,
                    "revit.apply_parameter_updates is not available on this endpoint."));
        }

        try
        {
            ApplyParameterUpdatesRequests.Validate(request);
            return await _applyParameterUpdates.ApplyParameterUpdatesAsync(request, cancellationToken).ConfigureAwait(false);
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
                    "The Revit parameter update apply request could not be executed."));
        }
    }

    [JsonRpcMethod("revit.get_warnings")]
    public async Task<GetWarningsResult> GetWarningsAsync(
        GetWarningsRequest request,
        CancellationToken cancellationToken)
    {
        EnsureCapabilityAllowed(
            BridgeProtocol.SupportsGetWarnings,
            "revit.get_warnings is not available on this connection.");
        if (_getWarnings is null)
        {
            throw StreamJsonRpcExceptionMapper.ToLocalRpc(
                new BridgeException(
                    BridgeErrorCodes.ProtocolIncompatible,
                    "revit.get_warnings is not available on this endpoint."));
        }

        try
        {
            return await _getWarnings.GetWarningsAsync(request, cancellationToken).ConfigureAwait(false);
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
                    "The Revit warnings request could not be executed."));
        }
    }

    [JsonRpcMethod("revit.get_selection")]
    public async Task<GetSelectionResult> GetSelectionAsync(
        GetSelectionRequest request,
        CancellationToken cancellationToken)
    {
        EnsureCapabilityAllowed(
            BridgeProtocol.SupportsGetSelection,
            "revit.get_selection is not available on this connection.");
        if (_getSelection is null)
        {
            throw StreamJsonRpcExceptionMapper.ToLocalRpc(
                new BridgeException(
                    BridgeErrorCodes.ProtocolIncompatible,
                    "revit.get_selection is not available on this endpoint."));
        }

        try
        {
            return await _getSelection.GetSelectionAsync(request, cancellationToken).ConfigureAwait(false);
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
                    "The Revit selection request could not be executed."));
        }
    }

    [JsonRpcMethod("revit.preview_mep_device_placements")]
    public async Task<PreviewMepDevicePlacementsResult> PreviewMepDevicePlacementsAsync(
        PreviewMepDevicePlacementsRequest request,
        CancellationToken cancellationToken)
    {
        EnsureCapabilityAllowed(
            BridgeProtocol.SupportsPreviewMepDevicePlacements,
            "revit.preview_mep_device_placements is not available on this connection.");
        if (_previewMepDevicePlacements is null)
        {
            throw StreamJsonRpcExceptionMapper.ToLocalRpc(
                new BridgeException(
                    BridgeErrorCodes.ProtocolIncompatible,
                    "revit.preview_mep_device_placements is not available on this endpoint."));
        }

        try
        {
            return await _previewMepDevicePlacements.PreviewMepDevicePlacementsAsync(request, cancellationToken).ConfigureAwait(false);
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
                    "The MEP device placement preview could not be executed."));
        }
    }

    [JsonRpcMethod("revit.request_mep_device_placement_review")]
    public async Task<RequestMepDevicePlacementReviewResult> RequestMepDevicePlacementReviewAsync(
        RequestMepDevicePlacementReviewRequest request,
        CancellationToken cancellationToken)
    {
        EnsureCapabilityAllowed(
            BridgeProtocol.SupportsRequestMepDevicePlacementReview,
            "revit.request_mep_device_placement_review is not available on this connection.");
        if (_requestMepDevicePlacementReview is null)
        {
            throw StreamJsonRpcExceptionMapper.ToLocalRpc(
                new BridgeException(
                    BridgeErrorCodes.ProtocolIncompatible,
                    "revit.request_mep_device_placement_review is not available on this endpoint."));
        }

        try
        {
            RequestMepDevicePlacementReviewRequests.Validate(request);
            return await _requestMepDevicePlacementReview.RequestMepDevicePlacementReviewAsync(request, cancellationToken).ConfigureAwait(false);
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
                    "The MEP device placement review request could not be executed."));
        }
    }

    [JsonRpcMethod("revit.apply_mep_device_placements")]
    public async Task<ApplyMepDevicePlacementsResult> ApplyMepDevicePlacementsAsync(
        ApplyMepDevicePlacementsRequest request,
        CancellationToken cancellationToken)
    {
        EnsureCapabilityAllowed(
            BridgeProtocol.SupportsApplyMepDevicePlacements,
            "revit.apply_mep_device_placements is not available on this connection.");
        if (_applyMepDevicePlacements is null)
        {
            throw StreamJsonRpcExceptionMapper.ToLocalRpc(
                new BridgeException(
                    BridgeErrorCodes.ProtocolIncompatible,
                    "revit.apply_mep_device_placements is not available on this endpoint."));
        }

        try
        {
            ApplyMepDevicePlacementsRequests.Validate(request);
            return await _applyMepDevicePlacements.ApplyMepDevicePlacementsAsync(request, cancellationToken).ConfigureAwait(false);
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
                    "The MEP device placement apply could not be executed."));
        }
    }

    [JsonRpcMethod("revit.get_room_layouts")]
    public async Task<GetRoomLayoutsResult> GetRoomLayoutsAsync(
        GetRoomLayoutsRequest request,
        CancellationToken cancellationToken)
    {
        EnsureCapabilityAllowed(
            BridgeProtocol.SupportsGetRoomLayouts,
            "revit.get_room_layouts is not available on this connection.");
        if (_getRoomLayouts is null)
        {
            throw StreamJsonRpcExceptionMapper.ToLocalRpc(
                new BridgeException(
                    BridgeErrorCodes.ProtocolIncompatible,
                    "revit.get_room_layouts is not available on this endpoint."));
        }

        try
        {
            ArgumentNullException.ThrowIfNull(request);
            return await _getRoomLayouts.GetRoomLayoutsAsync(request, cancellationToken).ConfigureAwait(false);
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
                    "The Revit room layout request could not be executed."));
        }
    }

    [JsonRpcMethod("revit.get_mep_device_types")]
    public async Task<GetMepDeviceTypesResult> GetMepDeviceTypesAsync(
        GetMepDeviceTypesRequest request,
        CancellationToken cancellationToken)
    {
        EnsureCapabilityAllowed(
            BridgeProtocol.SupportsGetMepDeviceTypes,
            "revit.get_mep_device_types is not available on this connection.");
        if (_getMepDeviceTypes is null)
        {
            throw StreamJsonRpcExceptionMapper.ToLocalRpc(
                new BridgeException(
                    BridgeErrorCodes.ProtocolIncompatible,
                    "revit.get_mep_device_types is not available on this endpoint."));
        }

        try
        {
            ArgumentNullException.ThrowIfNull(request);
            return await _getMepDeviceTypes.GetMepDeviceTypesAsync(request, cancellationToken).ConfigureAwait(false);
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
                    "The MEP device type catalog could not be read."));
        }
    }

    [JsonRpcMethod("revit.preview_room_pattern_placements")]
    public async Task<PreviewRoomPatternPlacementsResult> PreviewRoomPatternPlacementsAsync(
        PreviewRoomPatternPlacementsRequest request,
        CancellationToken cancellationToken)
    {
        EnsureCapabilityAllowed(
            BridgeProtocol.SupportsPreviewRoomPatternPlacements,
            "revit.preview_room_pattern_placements is not available on this connection.");
        if (_previewRoomPatternPlacements is null)
        {
            throw StreamJsonRpcExceptionMapper.ToLocalRpc(
                new BridgeException(
                    BridgeErrorCodes.ProtocolIncompatible,
                    "revit.preview_room_pattern_placements is not available on this endpoint."));
        }

        try
        {
            ArgumentNullException.ThrowIfNull(request);
            EnsureLinkedRoomsMatchSelectedProtocol(request);
            return await _previewRoomPatternPlacements.PreviewRoomPatternPlacementsAsync(request, cancellationToken).ConfigureAwait(false);
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
                    "The room pattern placement preview could not be created."));
        }
    }

    [JsonRpcMethod("revit.read_spatial_rooms")]
    public async Task<ReadSpatialRoomsResult> ReadSpatialRoomsAsync(
        ReadSpatialRoomsRequest request,
        CancellationToken cancellationToken)
    {
        EnsureCapabilityAllowed(
            BridgeProtocol.SupportsReadSpatialRooms,
            "revit.read_spatial_rooms is not available on this connection.");
        if (_readSpatialRooms is null)
        {
            throw StreamJsonRpcExceptionMapper.ToLocalRpc(
                new BridgeException(
                    BridgeErrorCodes.ProtocolIncompatible,
                    "revit.read_spatial_rooms is not available on this endpoint."));
        }

        try
        {
            ArgumentNullException.ThrowIfNull(request);
            return await _readSpatialRooms.ReadSpatialRoomsAsync(request, cancellationToken).ConfigureAwait(false);
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
                    "The spatial room request could not be executed."));
        }
    }

    private void EnsureQueryFiltersMatchSelectedProtocol(QueryElementsRequest request)
    {
        var selected = Volatile.Read(ref _selectedProtocolVersion);
        if (request.Filters is null
            || BridgeProtocol.SupportsActiveViewUnfilteredQuery(selected)
            || request.Filters.HasAny())
        {
            return;
        }

        throw new BridgeException(
            CapabilityErrorCodes.InvalidQuery,
            "The query request is invalid.");
    }

    private void EnsureLinkedRoomsMatchSelectedProtocol(PreviewRoomPatternPlacementsRequest request)
    {
        var selected = Volatile.Read(ref _selectedProtocolVersion);
        if (BridgeProtocol.SupportsLinkedRoomPatternPreview(selected)
            || request.Placements is null
            || !request.Placements.Any(placement => SpatialRoomRefCodec.IsLink(placement?.RoomRef)))
        {
            return;
        }

        throw new BridgeException(
            CapabilityErrorCodes.InvalidRoomPatternPlacement,
            "The room pattern placement preview is invalid.");
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
