using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Server;

internal sealed class ApplyMepDevicePlacementsApplicationService
{
    private static readonly HashSet<string> AcceptedCapabilityErrors = new(StringComparer.Ordinal)
    {
        CapabilityErrorCodes.InvalidApplyRequest,
        CapabilityErrorCodes.ExecutionTimeout,
        CapabilityErrorCodes.ExecutionFailed
    };

    private readonly IRevitInstanceDiscovery _discovery;
    private readonly IBridgeClientFactory _clients;
    private readonly ServerTimeouts _timeouts;

    public ApplyMepDevicePlacementsApplicationService(
        IRevitInstanceDiscovery discovery,
        IBridgeClientFactory clients,
        ServerTimeouts timeouts)
    {
        _discovery = discovery;
        _clients = clients;
        _timeouts = timeouts;
    }

    public async Task<ApplyMepDevicePlacementsOutcome> ExecuteAsync(
        string instanceId,
        ApplyMepDevicePlacementsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var discovered = await _discovery.DiscoverAsync(cancellationToken).ConfigureAwait(false);
        var resolution = InstanceTargetResolver.ResolveForApplyMepDevicePlacements(discovered, instanceId);
        if (resolution.Target is null)
        {
            return ApplyMepDevicePlacementsOutcome.Failure(resolution.ErrorCode!, resolution.ErrorMessage!);
        }

        var registration = resolution.Target.Registration!;
        try
        {
            await using var client = await _clients.ConnectAsync(registration.PipeName, _timeouts.BootstrapTimeout, cancellationToken).ConfigureAwait(false);
            var handshake = await client.HandshakeAsync(
                new BridgeHandshakeRequest
                {
                    ExpectedInstanceId = registration.InstanceId,
                    SupportedProtocolVersions = BridgeProtocol.SupportedVersions,
                    ClientName = "RevitMCP.Server",
                    ClientVersion = "0.1.0"
                },
                cancellationToken).ConfigureAwait(false);
            if (!string.Equals(handshake.InstanceId, registration.InstanceId, StringComparison.Ordinal)
                || !BridgeProtocol.SupportsApplyMepDevicePlacements(handshake.SelectedProtocolVersion))
            {
                return ApplyMepDevicePlacementsOutcome.Failure(McpToolErrorCodes.InstanceUnavailable, ToolErrorMessages.InstanceUnavailable);
            }

            var result = await client.ApplyMepDevicePlacementsAsync(request, _timeouts.CapabilityTimeout, cancellationToken).ConfigureAwait(false);
            return ApplyMepDevicePlacementsOutcome.Success(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (BridgeException exception) when (AcceptedCapabilityErrors.Contains(exception.ErrorCode))
        {
            return ApplyMepDevicePlacementsOutcome.Failure(exception.ErrorCode, exception.Message);
        }
        catch (Exception exception) when (exception is BridgeException or IOException or TimeoutException or OperationCanceledException)
        {
            return ApplyMepDevicePlacementsOutcome.Failure(McpToolErrorCodes.InstanceUnavailable, ToolErrorMessages.InstanceUnavailable);
        }
    }
}
