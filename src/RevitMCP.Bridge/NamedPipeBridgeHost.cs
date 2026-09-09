using System.IO.Pipes;
using RevitMCP.Bridge.Implementation;
using RevitMCP.Contracts;
using StreamJsonRpc;

namespace RevitMCP.Bridge;

public sealed class NamedPipeBridgeHost : IAsyncDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly IRevitBridgeService _handshake;
    private readonly IRevitCapabilityService? _capability;
    private readonly TaskCompletionSource _listenerReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _acceptLoop;
    private IRegistrationLease? _lease;
    private int _disposed;

    private NamedPipeBridgeHost(BridgeInstanceMetadata metadata, string pipeName, IRevitCapabilityService? capability)
    {
        Metadata = metadata;
        PipeName = pipeName;
        _handshake = new BridgeHandshakeService(metadata);
        _capability = capability;
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_lifetime.Token));
    }

    public BridgeInstanceMetadata Metadata { get; }

    public string PipeName { get; }

    public RevitInstanceRegistration? Registration => _lease?.Registration;

    public static Task<NamedPipeBridgeHost> StartAsync(
        BridgeInstanceMetadata metadata,
        IRegistrationStore store,
        CancellationToken cancellationToken)
    {
        return StartAsync(metadata, store, capability: null, cancellationToken);
    }

    public static async Task<NamedPipeBridgeHost> StartAsync(
        BridgeInstanceMetadata metadata,
        IRegistrationStore store,
        IRevitCapabilityService? capability,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(store);

        var advertised = WithAdvertisedProtocol(metadata, capability);
        var pipeName = BridgePipeNames.Create(advertised.WindowsSessionId, advertised.InstanceId);
        var host = new NamedPipeBridgeHost(advertised, pipeName, capability);
        try
        {
            await host._listenerReady.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

            var advertisedVersion = advertised.SupportedProtocolVersions.Max();

            var registration = new RevitInstanceRegistration
            {
                InstanceId = advertised.InstanceId,
                ProcessId = advertised.ProcessId,
                ProcessStartTimeUtc = advertised.ProcessStartTimeUtc,
                WindowsSessionId = advertised.WindowsSessionId,
                RevitVersion = advertised.RevitVersion,
                RevitBuild = advertised.RevitBuild,
                AddinVersion = advertised.AddinVersion,
                BridgeProtocolVersion = advertisedVersion,
                PipeName = pipeName,
                RegistrationCreatedUtc = DateTimeOffset.UtcNow
            };

            host._lease = await store.PublishAsync(registration, cancellationToken).ConfigureAwait(false);
            return host;
        }
        catch
        {
            try
            {
                await host.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
            }

            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Exception? cleanupError = null;

        try
        {
            if (_lease is not null)
            {
                await _lease.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            cleanupError = exception;
        }
        finally
        {
            _lease = null;
        }

        try
        {
            await _lifetime.CancelAsync().ConfigureAwait(false);
            try
            {
                await _acceptLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
        catch (Exception exception)
        {
            cleanupError ??= exception;
        }
        finally
        {
            _lifetime.Dispose();
        }

        if (cleanupError is not null)
        {
            throw cleanupError;
        }
    }

    internal static BridgeInstanceMetadata WithAdvertisedProtocol(
        BridgeInstanceMetadata metadata,
        IRevitCapabilityService? capability)
    {
        var versions = capability is null
            ? BridgeProtocol.HandshakeOnlyVersions
            : BridgeProtocol.SupportedVersions;

        return new BridgeInstanceMetadata
        {
            InstanceId = metadata.InstanceId,
            ProcessId = metadata.ProcessId,
            ProcessStartTimeUtc = metadata.ProcessStartTimeUtc,
            WindowsSessionId = metadata.WindowsSessionId,
            RevitVersion = metadata.RevitVersion,
            RevitBuild = metadata.RevitBuild,
            AddinVersion = metadata.AddinVersion,
            SupportedProtocolVersions = versions
        };
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        NamedPipeServerStream? listening = null;
        try
        {
            listening = JsonRpcFactory.CreateServer(PipeName);
            _listenerReady.TrySetResult();

            while (!cancellationToken.IsCancellationRequested)
            {
                await listening.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                var connected = listening;
                listening = JsonRpcFactory.CreateServer(PipeName);
                _ = Task.Run(() => HandleClientAsync(connected, cancellationToken), CancellationToken.None);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _listenerReady.TrySetCanceled(cancellationToken);
        }
        catch (IOException exception)
        {
            _listenerReady.TrySetException(exception);
        }
        finally
        {
            if (listening is not null)
            {
                await listening.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream server, CancellationToken cancellationToken)
    {
        JsonRpc? rpc = null;
        try
        {
            rpc = JsonRpcFactory.Create(server, new StreamJsonRpcBridgeAdapter(_handshake, _capability));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            await rpc.Completion.WaitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ConnectionLostException)
        {
        }
        finally
        {
            rpc?.Dispose();

            await server.DisposeAsync().ConfigureAwait(false);
        }
    }
}
