using System.IO;
using BimIt.Revit.Assistant.Ipc;

namespace BimIt.Revit.Addin.Assistant;

internal interface IAssistantLink
{
    Task<AssistantIpcMessage?> ConnectAsync(
        string pipeName,
        string instanceId,
        string? revitVersion,
        string? revitBuild,
        bool bridgeReady,
        CancellationToken cancellationToken);

    Task<AssistantIpcMessage?> RunAsync(string requestId, AssistantWorkflowName workflow, CancellationToken cancellationToken);

    Task ShutdownAsync();
}

internal sealed class NamedPipeAssistantLink : IAssistantLink, IAsyncDisposable
{
    private Stream? _stream;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<AssistantIpcMessage?> ConnectAsync(
        string pipeName,
        string instanceId,
        string? revitVersion,
        string? revitBuild,
        bool bridgeReady,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_stream is not null)
            {
                return null;
            }

            var client = AssistantPipeStreams.CreateClient(pipeName);
            var connectMilliseconds = (int)Math.Clamp(AssistantIpcTimeouts.Default.Connect.TotalMilliseconds, 1, int.MaxValue);
            await client.ConnectAsync(connectMilliseconds, cancellationToken).ConfigureAwait(false);
            _stream = client;
            var hello = new AssistantIpcMessage(
                AssistantIpcKind.Hello,
                instanceId,
                revitVersion,
                revitBuild,
                bridgeReady,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                false);
            await AssistantPipeFrames.WriteAsync(_stream, AssistantIpc.Serialize(hello), cancellationToken).ConfigureAwait(false);
            return await ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AssistantIpcMessage?> RunAsync(string requestId, AssistantWorkflowName workflow, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_stream is null)
            {
                return null;
            }

            var message = new AssistantIpcMessage(
                AssistantIpcKind.RunWorkflow,
                null,
                null,
                null,
                false,
                requestId,
                workflow,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                false);
            await AssistantPipeFrames.WriteAsync(_stream, AssistantIpc.Serialize(message), cancellationToken).ConfigureAwait(false);
            return await ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task ShutdownAsync() => ShutdownAsync(AssistantIpcTimeouts.Default.Shutdown);

    public async Task ShutdownAsync(TimeSpan timeout)
    {
        if (!await _gate.WaitAsync(timeout).ConfigureAwait(false))
        {
            return;
        }
        try
        {
            if (_stream is null)
            {
                return;
            }

            try
            {
                var message = new AssistantIpcMessage(
                    AssistantIpcKind.Shutdown,
                    null,
                    null,
                    null,
                    false,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    false);
                using var shutdown = new CancellationTokenSource(timeout);
                await AssistantPipeFrames.WriteAsync(_stream, AssistantIpc.Serialize(message), shutdown.Token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The process supervisor still stops the Assistant if the pipe is already gone.
            }

            await _stream.DisposeAsync().ConfigureAwait(false);
            _stream = null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await ShutdownAsync().ConfigureAwait(false);
        _gate.Dispose();
    }

    private async Task<AssistantIpcMessage?> ReadAsync(CancellationToken cancellationToken)
    {
        var json = await AssistantPipeFrames.ReadAsync(_stream!, cancellationToken).ConfigureAwait(false);
        var parsed = AssistantIpc.Parse(json);
        return parsed.Accepted ? parsed.Message : null;
    }
}
