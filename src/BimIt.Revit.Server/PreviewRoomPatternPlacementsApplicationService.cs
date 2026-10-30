using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Server;

internal sealed class PreviewRoomPatternPlacementsApplicationService
{
    private static readonly HashSet<string> AcceptedCapabilityErrors = new(StringComparer.Ordinal)
    {
        CapabilityErrorCodes.NoActiveDocument,
        CapabilityErrorCodes.DocumentContextChanged,
        CapabilityErrorCodes.UnsupportedDocumentKind,
        CapabilityErrorCodes.DocumentNotWritable,
        CapabilityErrorCodes.InvalidRoomPatternPlacement,
        CapabilityErrorCodes.IntentCapacityReached,
        CapabilityErrorCodes.ExecutionTimeout,
        CapabilityErrorCodes.ExecutionFailed
    };

    private readonly IRevitInstanceDiscovery _discovery;
    private readonly IBridgeClientFactory _clients;
    private readonly ServerTimeouts _timeouts;

    public PreviewRoomPatternPlacementsApplicationService(
        IRevitInstanceDiscovery discovery,
        IBridgeClientFactory clients,
        ServerTimeouts timeouts)
    {
        _discovery = discovery;
        _clients = clients;
        _timeouts = timeouts;
    }

    public async Task<PreviewRoomPatternPlacementsOutcome> ExecuteAsync(
        string? instanceId,
        PreviewRoomPatternPlacementsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var discovered = await _discovery.DiscoverAsync(cancellationToken).ConfigureAwait(false);
        var resolution = InstanceTargetResolver.ResolveForPreviewRoomPatternPlacements(discovered, instanceId);
        if (resolution.Target is null)
        {
            return PreviewRoomPatternPlacementsOutcome.Failure(resolution.ErrorCode!, resolution.ErrorMessage!, resolution.Candidates);
        }

        var registration = resolution.Target.Registration!;
        try
        {
            await using var client = await _clients
                .ConnectAsync(registration.PipeName, _timeouts.BootstrapTimeout, cancellationToken)
                .ConfigureAwait(false);
            var handshake = await client.HandshakeAsync(
                    new BridgeHandshakeRequest
                    {
                        ExpectedInstanceId = registration.InstanceId,
                        SupportedProtocolVersions = BridgeProtocol.SupportedVersions,
                        ClientName = "RevitMCP.Server",
                        ClientVersion = "0.1.0"
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(handshake.InstanceId, registration.InstanceId, StringComparison.Ordinal)
                || !BridgeProtocol.SupportsPreviewRoomPatternPlacements(handshake.SelectedProtocolVersion))
            {
                return PreviewRoomPatternPlacementsOutcome.Failure(McpToolErrorCodes.InstanceUnavailable, ToolErrorMessages.InstanceUnavailable);
            }

            if (!BridgeProtocol.SupportsLinkedRoomPatternPreview(handshake.SelectedProtocolVersion)
                && request.Placements?.Any(placement => SpatialRoomRefCodec.IsLink(placement?.RoomRef)) == true)
            {
                return PreviewRoomPatternPlacementsOutcome.Failure(
                    CapabilityErrorCodes.InvalidRoomPatternPlacement,
                    "This Revit session cannot preview placements in a linked room.");
            }

            var result = await client.PreviewRoomPatternPlacementsAsync(request, _timeouts.CapabilityTimeout, cancellationToken).ConfigureAwait(false);
            return PreviewRoomPatternPlacementsOutcome.Success(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (BridgeException exception) when (AcceptedCapabilityErrors.Contains(exception.ErrorCode))
        {
            return PreviewRoomPatternPlacementsOutcome.Failure(exception.ErrorCode, exception.Message);
        }
        catch (Exception exception) when (exception is BridgeException or IOException or TimeoutException or OperationCanceledException)
        {
            return PreviewRoomPatternPlacementsOutcome.Failure(McpToolErrorCodes.InstanceUnavailable, ToolErrorMessages.InstanceUnavailable);
        }
    }
}
