using GitHub.Copilot;

namespace BimIt.Revit.Assistant.Providers.GitHubCopilot;

internal sealed class GitHubCopilotAgentRuntime : IAssistantAgentRuntime
{
    private readonly BimItAgentReadTools _tools;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly string _directory;
    private CopilotClient? _client;
    private CopilotSession? _session;
    private IDisposable? _modelEvents;

    public GitHubCopilotAgentRuntime(IAssistantToolSession session, string instanceId, string? runtimeDirectory = null)
    {
        _tools = new BimItAgentReadTools(session, instanceId);
        _directory = string.IsNullOrWhiteSpace(runtimeDirectory) ? CopilotRuntimeDirectory.Create() : runtimeDirectory;
        Directory.CreateDirectory(_directory);
        Status = AssistantProviderStatus.NotConfigured;
    }

    public AssistantProviderStatus Status { get; private set; }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (Status.State == AssistantProviderState.Ready)
        {
            return;
        }

        Status = new AssistantProviderStatus(GitHubCopilotSessionBlueprint.ProviderName, AssistantProviderState.Starting, null, null);
        await Task.Yield();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            if (_stop.IsCancellationRequested || Status.State == AssistantProviderState.Ready)
            {
                return;
            }

            Status = new AssistantProviderStatus(GitHubCopilotSessionBlueprint.ProviderName, AssistantProviderState.Starting, null, null);
            await StartSessionAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await StopQuietlyAsync().ConfigureAwait(false);
            Status = Unavailable(AssistantSafeText.Clean(exception.Message, 400));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AssistantAgentTurn> SendAsync(string userText, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userText))
        {
            return new AssistantAgentTurn("BIM-IT Assistant", "Enter a question about this Revit session.", true);
        }

        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        if (Status.State != AssistantProviderState.Ready || _session is null)
        {
            var detail = Status.SafeError ?? GitHubCopilotSessionBlueprint.SignInRequired;
            var signIn = string.Equals(detail, GitHubCopilotSessionBlueprint.SignInRequired, StringComparison.Ordinal);
            return new AssistantAgentTurn("BIM-IT Assistant", detail, !signIn);
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            if (_session is null)
            {
                return new AssistantAgentTurn("BIM-IT Assistant", Status.SafeError ?? "The AI provider is unavailable.", true);
            }

            var message = await _session.SendAndWaitAsync(
                new MessageOptions { Prompt = userText.Trim() },
                TimeSpan.FromSeconds(90),
                linked.Token).ConfigureAwait(false);
            var data = message?.Data;
            RememberModel(data?.Model);
            var content = AssistantSafeText.Clean(data?.Content, 16_000);
            if (string.IsNullOrWhiteSpace(content))
            {
                content = "The assistant did not return an answer.";
            }

            return new AssistantAgentTurn("BIM-IT Assistant", content, false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !_stop.IsCancellationRequested)
        {
            if (_session is not null)
            {
                try
                {
                    await _session.AbortAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception)
                {
                }
            }

            var safe = AssistantSafeText.Clean(exception.Message, 400);
            Status = Unavailable(string.IsNullOrWhiteSpace(safe) ? "The AI provider did not answer." : safe);
            return new AssistantAgentTurn("BIM-IT Assistant", Status.SafeError!, true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ResetConversationAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            await DisposeSessionAsync().ConfigureAwait(false);
            if (_client is null || Status.State == AssistantProviderState.Unavailable)
            {
                return;
            }

            _session = await CreateSessionAsync(linked.Token).ConfigureAwait(false);
            Status = ReadyStatus();
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !_stop.IsCancellationRequested)
        {
            Status = Unavailable(AssistantSafeText.Clean(exception.Message, 400));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        if (!await _gate.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false))
        {
            CopilotRuntimeDirectory.TryDelete(_directory);
            return;
        }

        try
        {
            await StopQuietlyAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
            _stop.Dispose();
            CopilotRuntimeDirectory.TryDelete(_directory);
        }
    }

    private async Task StartSessionAsync(CancellationToken cancellationToken)
    {
        await StopQuietlyAsync().ConfigureAwait(false);
        _client = new CopilotClient(GitHubCopilotSessionBlueprint.CreateClientOptions(_directory));
        await _client.StartAsync(cancellationToken).ConfigureAwait(false);
        var auth = await _client.GetAuthStatusAsync(cancellationToken).ConfigureAwait(false);
        if (!auth.IsAuthenticated)
        {
            await StopQuietlyAsync().ConfigureAwait(false);
            Status = Unavailable(GitHubCopilotSessionBlueprint.SignInRequired);
            return;
        }

        try
        {
            _ = await _client.ListModelsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Model discovery is diagnostic. Selection stays on auto.
        }

        _session = await CreateSessionAsync(cancellationToken).ConfigureAwait(false);
        Status = ReadyStatus();
    }

    private async Task<CopilotSession> CreateSessionAsync(CancellationToken cancellationToken)
    {
        var session = await _client!.CreateSessionAsync(
            GitHubCopilotSessionBlueprint.CreateSessionConfig(_directory, GitHubCopilotSessionBlueprint.CreateTools(_tools)),
            cancellationToken).ConfigureAwait(false);
        _modelEvents?.Dispose();
        _modelEvents = session.On<SessionModelChangeEvent>(change => RememberModel(change.Data.NewModel));
        return session;
    }

    private void RememberModel(string? model)
    {
        if (!AssistantProviderStatus.IsConcreteModel(model) || Status.State != AssistantProviderState.Ready)
        {
            return;
        }

        Status = Status with { Model = model };
    }

    private AssistantProviderStatus ReadyStatus()
    {
        return new AssistantProviderStatus(GitHubCopilotSessionBlueprint.ProviderName, AssistantProviderState.Ready, GitHubCopilotSessionBlueprint.AutoModel, null);
    }

    private static AssistantProviderStatus Unavailable(string? error)
    {
        var safe = string.IsNullOrWhiteSpace(error) ? "The AI provider is unavailable." : error;
        return new AssistantProviderStatus(GitHubCopilotSessionBlueprint.ProviderName, AssistantProviderState.Unavailable, null, safe);
    }

    private async Task StopQuietlyAsync()
    {
        _modelEvents?.Dispose();
        _modelEvents = null;
        await DisposeSessionAsync().ConfigureAwait(false);
        if (_client is null)
        {
            return;
        }

        var client = _client;
        _client = null;
        try
        {
            await client.ForceStopAsync().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }

        try
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    private async Task DisposeSessionAsync()
    {
        if (_session is null)
        {
            return;
        }

        var session = _session;
        _session = null;
        try
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }
}
