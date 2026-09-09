using System.IO.Pipes;
using RevitMCP.Bridge.Implementation;
using RevitMCP.Contracts;
using StreamJsonRpc;

namespace RevitMCP.Bridge;

public sealed class NamedPipeBridgeHost : IAsyncDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly IRevitBridgeService _service;
    private readonly TaskCompletionSource _listenerReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _acceptLoop;
    private IRegistrationLease? _lease;
    private int _disposed;

    private NamedPipeBridgeHost(BridgeInstanceMetadata metadata, string pipeName)
    {
        Metadata = metadata;
        PipeName = pipeName;
        _service = new BridgeHandshakeService(metadata);
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_lifetime.Token));
    }

    public BridgeInstanceMetadata Metadata { get; }

    public string PipeName { get; }

    public RevitInstanceRegistration? Registration => _lease?.Registration;

    public static async Task<NamedPipeBridgeHost> StartAsync(
        BridgeInstanceMetadata metadata,
        IRegistrationStore store,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(store);

        var pipeName = BridgePipeNames.Create(metadata.WindowsSessionId, metadata.InstanceId);
        var host = new NamedPipeBridgeHost(metadata, pipeName);
        await host._listenerReady.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

        var selected = ProtocolVersionSelector.SelectHighestCommon(metadata.SupportedProtocolVersions, metadata.SupportedProtocolVersions)
            ?? BridgeProtocol.CurrentVersion;

        var registration = new RevitInstanceRegistration
        {
            InstanceId = metadata.InstanceId,
            ProcessId = metadata.ProcessId,
            ProcessStartTimeUtc = metadata.ProcessStartTimeUtc,
            WindowsSessionId = metadata.WindowsSessionId,
            RevitVersion = metadata.RevitVersion,
            RevitBuild = metadata.RevitBuild,
            AddinVersion = metadata.AddinVersion,
            BridgeProtocolVersion = selected,
            PipeName = pipeName,
            RegistrationCreatedUtc = DateTimeOffset.UtcNow
        };

        host._lease = await store.PublishAsync(registration, cancellationToken).ConfigureAwait(false);
        return host;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _lifetime.CancelAsync().ConfigureAwait(false);
        try
        {
            await _acceptLoop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        if (_lease is not null)
        {
            await _lease.DisposeAsync().ConfigureAwait(false);
        }

        _lifetime.Dispose();
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
            rpc = JsonRpcFactory.Create(server, new StreamJsonRpcHandshakeAdapter(_service));
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
