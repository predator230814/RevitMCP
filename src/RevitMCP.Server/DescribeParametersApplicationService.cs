using RevitMCP.Bridge;
using RevitMCP.Contracts;

namespace RevitMCP.Server;

internal sealed class DescribeParametersApplicationService
{
    private static readonly HashSet<string> AcceptedCapabilityErrors = new(StringComparer.Ordinal)
    {
        CapabilityErrorCodes.NoActiveDocument,
        CapabilityErrorCodes.DocumentContextChanged,
        CapabilityErrorCodes.InvalidParameterDiscovery,
        CapabilityErrorCodes.ExecutionTimeout,
        CapabilityErrorCodes.ExecutionFailed
    };

    private readonly IRevitInstanceDiscovery _discovery;
    private readonly IBridgeClientFactory _clients;
    private readonly ServerTimeouts _timeouts;

    public DescribeParametersApplicationService(
        IRevitInstanceDiscovery discovery,
        IBridgeClientFactory clients,
        ServerTimeouts timeouts)
    {
        _discovery = discovery;
        _clients = clients;
        _timeouts = timeouts;
    }

    public async Task<DescribeParametersOutcome> ExecuteAsync(
        string? instanceId,
        DescribeParametersRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var discovered = await _discovery.DiscoverAsync(cancellationToken).ConfigureAwait(false);
        var resolution = InstanceTargetResolver.ResolveForDescribeParameters(discovered, instanceId);
        if (resolution.Target is null)
        {
            return DescribeParametersOutcome.Failure(
                resolution.ErrorCode!,
                resolution.ErrorMessage!,
                resolution.Candidates);
        }

        return await InvokeSelectedAsync(resolution.Target, request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<DescribeParametersOutcome> InvokeSelectedAsync(
        DiscoveredInstance selected,
        DescribeParametersRequest request,
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
                || !BridgeProtocol.SupportsDescribeParameters(handshake.SelectedProtocolVersion))
            {
                return DescribeParametersOutcome.Failure(
                    McpToolErrorCodes.InstanceUnavailable,
                    ToolErrorMessages.InstanceUnavailable);
            }

            var result = await client
                .DescribeParametersAsync(request, _timeouts.CapabilityTimeout, cancellationToken)
                .ConfigureAwait(false);
            return DescribeParametersOutcome.Success(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (BridgeException exception) when (AcceptedCapabilityErrors.Contains(exception.ErrorCode))
        {
            return DescribeParametersOutcome.Failure(exception.ErrorCode, exception.Message);
        }
        catch (Exception exception) when (exception is BridgeException or IOException or TimeoutException or OperationCanceledException)
        {
            return DescribeParametersOutcome.Failure(
                McpToolErrorCodes.InstanceUnavailable,
                ToolErrorMessages.InstanceUnavailable);
        }
    }
}
