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

    Task<AssistantIpcMessage?> ChatAsync(string requestId, string text, CancellationToken cancellationToken);

    Task<AssistantIpcMessage?> ResetConversationAsync(CancellationToken cancellationToken);

    Task<AssistantIpcMessage?> AttachDocumentAsync(string path, CancellationToken cancellationToken);

    Task<AssistantIpcMessage?> RemoveDocumentAsync(CancellationToken cancellationToken);

    Task<AssistantIpcMessage?> ConfirmProjectAxesPreviewAsync(CancellationToken cancellationToken);

    Task<AssistantIpcMessage?> ReviewInRevitAsync(CancellationToken cancellationToken);

    Task<AssistantIpcMessage?> ApplyApprovedPlacementsAsync(CancellationToken cancellationToken);

    Task<AssistantIpcMessage?> PreparePatternPreviewAsync(string configuration, CancellationToken cancellationToken);

    Task<AssistantIpcMessage?> SearchPatternTypesAsync(string role, string query, CancellationToken cancellationToken);

    Task<AssistantIpcMessage?> ReviewPatternInRevitAsync(CancellationToken cancellationToken);

    Task<AssistantIpcMessage?> ApplyApprovedPatternAsync(CancellationToken cancellationToken);

    Task<AssistantIpcMessage?> QueryStatusAsync(CancellationToken cancellationToken);

    Task<AssistantIpcMessage?> SkillsAsync(AssistantSkillsIpc.Request request, CancellationToken cancellationToken);

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

    public Task<AssistantIpcMessage?> ChatAsync(string requestId, string text, CancellationToken cancellationToken)
    {
        return ExchangeAsync(
            new AssistantIpcMessage(
                AssistantIpcKind.Chat,
                null,
                null,
                null,
                false,
                requestId,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                false,
                Text: text),
            cancellationToken);
    }

    public Task<AssistantIpcMessage?> ResetConversationAsync(CancellationToken cancellationToken)
    {
        return ExchangeAsync(Signal(AssistantIpcKind.ResetConversation), cancellationToken);
    }

    public Task<AssistantIpcMessage?> AttachDocumentAsync(string path, CancellationToken cancellationToken)
    {
        return ExchangeAsync(
            new AssistantIpcMessage(
                AssistantIpcKind.AttachDocument,
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
                false,
                DocumentPath: path),
            cancellationToken);
    }

    public Task<AssistantIpcMessage?> RemoveDocumentAsync(CancellationToken cancellationToken)
    {
        return ExchangeAsync(Signal(AssistantIpcKind.RemoveDocument), cancellationToken);
    }

    public Task<AssistantIpcMessage?> ConfirmProjectAxesPreviewAsync(CancellationToken cancellationToken)
    {
        return ExchangeAsync(Signal(AssistantIpcKind.ConfirmProjectAxesPreview), cancellationToken);
    }

    public Task<AssistantIpcMessage?> ReviewInRevitAsync(CancellationToken cancellationToken)
    {
        return ExchangeAsync(Signal(AssistantIpcKind.ReviewInRevit), cancellationToken);
    }

    public Task<AssistantIpcMessage?> ApplyApprovedPlacementsAsync(CancellationToken cancellationToken)
    {
        return ExchangeAsync(Signal(AssistantIpcKind.ApplyApprovedPlacements), cancellationToken);
    }

    public Task<AssistantIpcMessage?> PreparePatternPreviewAsync(string configuration, CancellationToken cancellationToken)
    {
        return ExchangeAsync(new AssistantIpcMessage(
            AssistantIpcKind.PreparePatternPreview,
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
            false,
            Text: configuration), cancellationToken);
    }

    public Task<AssistantIpcMessage?> SearchPatternTypesAsync(string role, string query, CancellationToken cancellationToken)
    {
        return ExchangeAsync(new AssistantIpcMessage(
            AssistantIpcKind.SearchPatternTypes,
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
            false,
            Text: RoomPatternTypeSearch.SerializeRequest(role, query)), cancellationToken);
    }

    public Task<AssistantIpcMessage?> SkillsAsync(AssistantSkillsIpc.Request request, CancellationToken cancellationToken)
    {
        return ExchangeAsync(new AssistantIpcMessage(
            AssistantIpcKind.Skills,
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
            false,
            Text: AssistantSkillsIpc.SerializeRequest(request)), cancellationToken);
    }

    public Task<AssistantIpcMessage?> ReviewPatternInRevitAsync(CancellationToken cancellationToken)
    {
        return ExchangeAsync(Signal(AssistantIpcKind.ReviewPatternInRevit), cancellationToken);
    }

    public Task<AssistantIpcMessage?> ApplyApprovedPatternAsync(CancellationToken cancellationToken)
    {
        return ExchangeAsync(Signal(AssistantIpcKind.ApplyApprovedPattern), cancellationToken);
    }

    public Task<AssistantIpcMessage?> QueryStatusAsync(CancellationToken cancellationToken)
    {
        return ExchangeAsync(Signal(AssistantIpcKind.QueryStatus), cancellationToken);
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

    private async Task<AssistantIpcMessage?> ExchangeAsync(AssistantIpcMessage message, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_stream is null)
            {
                return null;
            }

            await AssistantPipeFrames.WriteAsync(_stream, AssistantIpc.Serialize(message), cancellationToken).ConfigureAwait(false);
            return await ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static AssistantIpcMessage Signal(AssistantIpcKind kind)
    {
        return new AssistantIpcMessage(kind, null, null, null, false, null, null, null, null, null, null, null, null, null, false);
    }

    private async Task<AssistantIpcMessage?> ReadAsync(CancellationToken cancellationToken)
    {
        var json = await AssistantPipeFrames.ReadAsync(_stream!, cancellationToken).ConfigureAwait(false);
        var parsed = AssistantIpc.Parse(json);
        return parsed.Accepted ? parsed.Message : null;
    }
}
