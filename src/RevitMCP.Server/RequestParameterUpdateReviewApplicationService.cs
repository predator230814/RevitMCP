using RevitMCP.Bridge;
using RevitMCP.Contracts;

namespace RevitMCP.Server;

internal sealed class RequestParameterUpdateReviewApplicationService
{
    private static readonly HashSet<string> AcceptedCapabilityErrors = new(StringComparer.Ordinal)
    {
        CapabilityErrorCodes.InvalidApprovalReviewRequest,
        CapabilityErrorCodes.ExecutionTimeout,
        CapabilityErrorCodes.ExecutionFailed
    };

    private readonly IRevitInstanceDiscovery _discovery;
    private readonly IBridgeClientFactory _clients;
    private readonly ServerTimeouts _timeouts;

    public RequestParameterUpdateReviewApplicationService(
        IRevitInstanceDiscovery discovery,
        IBridgeClientFactory clients,
        ServerTimeouts timeouts)
    {
        _discovery = discovery;
        _clients = clients;
        _timeouts = timeouts;
    }

    public async Task<RequestParameterUpdateReviewOutcome> ExecuteAsync(
        string instanceId,
        RequestParameterUpdateReviewRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var discovered = await _discovery.DiscoverAsync(cancellationToken).ConfigureAwait(false);
        var resolution = InstanceTargetResolver.ResolveForRequestParameterUpdateReview(discovered, instanceId);
        if (resolution.Target is null)
        {
            return RequestParameterUpdateReviewOutcome.Failure(
                resolution.ErrorCode!,
                resolution.ErrorMessage!);
        }

        return await InvokeSelectedAsync(resolution.Target, request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<RequestParameterUpdateReviewOutcome> InvokeSelectedAsync(
        DiscoveredInstance selected,
        RequestParameterUpdateReviewRequest request,
        CancellationToken cancellationToken)
    {
        var registration = selected.Registration!;
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
                || !BridgeProtocol.SupportsRequestParameterUpdateReview(handshake.SelectedProtocolVersion))
            {
                return RequestParameterUpdateReviewOutcome.Failure(
                    McpToolErrorCodes.InstanceUnavailable,
                    ToolErrorMessages.InstanceUnavailable);
            }

            var result = await client
                .RequestParameterUpdateReviewAsync(request, _timeouts.CapabilityTimeout, cancellationToken)
                .ConfigureAwait(false);
            return RequestParameterUpdateReviewOutcome.Success(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (BridgeException exception) when (AcceptedCapabilityErrors.Contains(exception.ErrorCode))
        {
            return RequestParameterUpdateReviewOutcome.Failure(exception.ErrorCode, exception.Message);
        }
        catch (Exception exception) when (exception is BridgeException or IOException or TimeoutException or OperationCanceledException)
        {
            return RequestParameterUpdateReviewOutcome.Failure(
                McpToolErrorCodes.InstanceUnavailable,
                ToolErrorMessages.InstanceUnavailable);
        }
    }
}
