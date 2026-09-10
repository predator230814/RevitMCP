using RevitMCP.Bridge;
using RevitMCP.Contracts;

namespace RevitMCP.Server;

internal sealed class GetContextApplicationService
{
    private readonly IRevitInstanceDiscovery _discovery;
    private readonly IBridgeClientFactory _clients;
    private readonly ServerTimeouts _timeouts;

    public GetContextApplicationService(
        IRevitInstanceDiscovery discovery,
        IBridgeClientFactory clients,
        ServerTimeouts timeouts)
    {
        _discovery = discovery;
        _clients = clients;
        _timeouts = timeouts;
    }

    public async Task<GetContextOutcome> ExecuteAsync(string? instanceId, CancellationToken cancellationToken)
    {
        var discovered = await _discovery.DiscoverAsync(cancellationToken).ConfigureAwait(false);
        var resolution = InstanceTargetResolver.Resolve(discovered, instanceId);
        if (resolution.Target is null)
        {
            return GetContextOutcome.Failure(
                resolution.ErrorCode!,
                resolution.ErrorMessage!,
                resolution.Candidates);
        }

        return await InvokeSelectedAsync(resolution.Target, cancellationToken).ConfigureAwait(false);
    }

    private async Task<GetContextOutcome> InvokeSelectedAsync(
        DiscoveredInstance selected,
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
                || handshake.SelectedProtocolVersion != BridgeProtocol.GetContextVersion)
            {
                return GetContextOutcome.Failure(
                    McpToolErrorCodes.InstanceUnavailable,
                    ToolErrorMessages.InstanceUnavailable);
            }

            var result = await client
                .GetContextAsync(new GetContextRequest(), _timeouts.CapabilityTimeout, cancellationToken)
                .ConfigureAwait(false);
            return GetContextOutcome.Success(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (BridgeException exception) when (exception.ErrorCode == CapabilityErrorCodes.ExecutionTimeout)
        {
            return GetContextOutcome.Failure(
                McpToolErrorCodes.ExecutionTimeout,
                ToolErrorMessages.ExecutionTimeout);
        }
        catch (BridgeException exception) when (exception.ErrorCode == CapabilityErrorCodes.ExecutionFailed)
        {
            return GetContextOutcome.Failure(
                McpToolErrorCodes.ExecutionFailed,
                ToolErrorMessages.ExecutionFailed);
        }
        catch (Exception exception) when (exception is BridgeException or IOException or TimeoutException or OperationCanceledException)
        {
            return GetContextOutcome.Failure(
                McpToolErrorCodes.InstanceUnavailable,
                ToolErrorMessages.InstanceUnavailable);
        }
    }
}
