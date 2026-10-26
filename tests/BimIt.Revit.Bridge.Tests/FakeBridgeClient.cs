using BimIt.Revit.Contracts;

namespace BimIt.Revit.Bridge.Tests;

internal sealed class FakeBridgeClient : IRevitBridgeClient
{
    private readonly Func<BridgeHandshakeRequest, CancellationToken, Task<BridgeHandshakeResult>> _handshake;

    public FakeBridgeClient(Func<BridgeHandshakeRequest, CancellationToken, Task<BridgeHandshakeResult>> handshake)
    {
        _handshake = handshake;
    }

    public Task<BridgeHandshakeResult> HandshakeAsync(BridgeHandshakeRequest request, CancellationToken cancellationToken)
    {
        return _handshake(request, cancellationToken);
    }

    public Task<GetContextResult> GetContextAsync(GetContextRequest request, TimeSpan timeout, CancellationToken cancellationToken)
    {
        _ = request;
        _ = timeout;
        _ = cancellationToken;
        throw new NotSupportedException("This fake client does not implement revit.get_context.");
    }

    public Task<QueryElementsResult> QueryElementsAsync(QueryElementsRequest request, TimeSpan timeout, CancellationToken cancellationToken)
    {
        _ = request;
        _ = timeout;
        _ = cancellationToken;
        throw new NotSupportedException("This fake client does not implement revit.query_elements.");
    }

    public Task<GetElementsResult> GetElementsAsync(GetElementsRequest request, TimeSpan timeout, CancellationToken cancellationToken)
    {
        _ = request;
        _ = timeout;
        _ = cancellationToken;
        throw new NotSupportedException("This fake client does not implement revit.get_elements.");
    }

    public Task<DescribeParametersResult> DescribeParametersAsync(
        DescribeParametersRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        _ = request;
        _ = timeout;
        _ = cancellationToken;
        throw new NotSupportedException("This fake client does not implement revit.describe_parameters.");
    }

    public Task<GetParameterValuesResult> GetParameterValuesAsync(
        GetParameterValuesRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        _ = request;
        _ = timeout;
        _ = cancellationToken;
        throw new NotSupportedException("This fake client does not implement revit.get_parameter_values.");
    }

    public Task<PreviewParameterUpdatesResult> PreviewParameterUpdatesAsync(
        PreviewParameterUpdatesRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        _ = request;
        _ = timeout;
        _ = cancellationToken;
        throw new NotSupportedException("This fake client does not implement revit.preview_parameter_updates.");
    }

    public Task<RequestParameterUpdateReviewResult> RequestParameterUpdateReviewAsync(
        RequestParameterUpdateReviewRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        _ = request;
        _ = timeout;
        _ = cancellationToken;
        throw new NotSupportedException("This fake client does not implement revit.request_parameter_update_review.");
    }

    public Task<GetSelectionResult> GetSelectionAsync(
        GetSelectionRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        _ = request;
        _ = timeout;
        _ = cancellationToken;
        throw new NotSupportedException("This fake client does not implement revit.get_selection.");
    }

    public Task<GetWarningsResult> GetWarningsAsync(
        GetWarningsRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        _ = request;
        _ = timeout;
        _ = cancellationToken;
        throw new NotSupportedException("This fake client does not implement revit.get_warnings.");
    }

    public Task<ApplyParameterUpdatesResult> ApplyParameterUpdatesAsync(
        ApplyParameterUpdatesRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        _ = request;
        _ = timeout;
        _ = cancellationToken;
        throw new NotSupportedException("This fake client does not implement revit.apply_parameter_updates.");
    }

    public Task<GetMepTopologyResult> GetMepTopologyAsync(
        GetMepTopologyRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        _ = request;
        _ = timeout;
        _ = cancellationToken;
        throw new NotSupportedException("This fake client does not implement revit.get_mep_topology.");
    }

    public Task<PreviewMepDevicePlacementsResult> PreviewMepDevicePlacementsAsync(
        PreviewMepDevicePlacementsRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        _ = request;
        _ = timeout;
        _ = cancellationToken;
        throw new NotSupportedException("This fake client does not implement revit.preview_mep_device_placements.");
    }

    public Task<RequestMepDevicePlacementReviewResult> RequestMepDevicePlacementReviewAsync(
        RequestMepDevicePlacementReviewRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        _ = request;
        _ = timeout;
        _ = cancellationToken;
        throw new NotSupportedException("This fake client does not implement revit.request_mep_device_placement_review.");
    }

    public Task<ApplyMepDevicePlacementsResult> ApplyMepDevicePlacementsAsync(
        ApplyMepDevicePlacementsRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        _ = request;
        _ = timeout;
        _ = cancellationToken;
        throw new NotSupportedException("This fake client does not implement revit.apply_mep_device_placements.");
    }

    public Task<GetRoomLayoutsResult> GetRoomLayoutsAsync(
        GetRoomLayoutsRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        _ = request;
        _ = timeout;
        _ = cancellationToken;
        throw new NotSupportedException("This fake client does not implement revit.get_room_layouts.");
    }

    public Task<GetMepDeviceTypesResult> GetMepDeviceTypesAsync(
        GetMepDeviceTypesRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        _ = request;
        _ = timeout;
        _ = cancellationToken;
        throw new NotSupportedException("This fake client does not implement revit.get_mep_device_types.");
    }

    public Task<PreviewRoomPatternPlacementsResult> PreviewRoomPatternPlacementsAsync(
        PreviewRoomPatternPlacementsRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        _ = request;
        _ = timeout;
        _ = cancellationToken;
        throw new NotSupportedException("This fake client does not implement revit.preview_room_pattern_placements.");
    }

    public Task<ReadSpatialRoomsResult> ReadSpatialRoomsAsync(
        ReadSpatialRoomsRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        _ = request;
        _ = timeout;
        _ = cancellationToken;
        throw new NotSupportedException("This fake client does not implement revit.read_spatial_rooms.");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
