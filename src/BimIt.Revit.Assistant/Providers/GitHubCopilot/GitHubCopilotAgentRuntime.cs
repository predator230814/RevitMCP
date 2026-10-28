using BimIt.Revit.Assistant.Ipc;
using BimIt.Revit.Assistant.Patterns;
using GitHub.Copilot;

namespace BimIt.Revit.Assistant.Providers.GitHubCopilot;

internal sealed class GitHubCopilotAgentRuntime : IAssistantAgentRuntime
{
    private readonly BimItAgentReadTools _tools;
    private readonly MepDevicePlacementProposalTool _proposals;
    private readonly RoomPatternProposalTool _roomPatterns;
    private readonly MepDevicePlacementProposalStore _proposalStore;
    private readonly RoomPatternStore _patternStore;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly string _directory;
    private readonly CopilotAttachmentDelivery _delivery = new();
    private CopilotClient? _client;
    private CopilotSession? _session;
    private IDisposable? _modelEvents;

    public GitHubCopilotAgentRuntime(
        IAssistantToolSession session,
        string instanceId,
        MepDevicePlacementProposalStore proposalStore,
        Func<string?> currentAttachmentId,
        RoomPatternStore patternStore,
        Func<AssistantDocumentArtifact?> currentAttachment,
        string? runtimeDirectory = null)
    {
        _proposalStore = proposalStore ?? throw new ArgumentNullException(nameof(proposalStore));
        _patternStore = patternStore ?? throw new ArgumentNullException(nameof(patternStore));
        ArgumentNullException.ThrowIfNull(currentAttachmentId);
        ArgumentNullException.ThrowIfNull(currentAttachment);
        _tools = new BimItAgentReadTools(session, instanceId);
        _proposals = new MepDevicePlacementProposalTool(session, instanceId, proposalStore, currentAttachmentId);
        _roomPatterns = new RoomPatternProposalTool(session, instanceId, patternStore, currentAttachment);
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
        using var budget = Budget(AssistantProviderTimeouts.Default.Initialization, cancellationToken);
        var acquired = false;
        try
        {
            await _gate.WaitAsync(budget.Token).ConfigureAwait(false);
            acquired = true;
            if (_stop.IsCancellationRequested || Status.State == AssistantProviderState.Ready)
            {
                return;
            }

            Status = new AssistantProviderStatus(GitHubCopilotSessionBlueprint.ProviderName, AssistantProviderState.Starting, null, null);
            await StartSessionAsync(budget.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException)
        {
            if (Status.State != AssistantProviderState.Ready)
            {
                await StopQuietlyAsync().ConfigureAwait(false);
                Status = Unavailable(AssistantProviderTimeouts.InitializationTimedOut);
            }
        }
        catch (Exception exception)
        {
            await StopQuietlyAsync().ConfigureAwait(false);
            Status = Unavailable(AssistantSafeText.Clean(exception.Message, 400));
        }
        finally
        {
            if (acquired)
            {
                _gate.Release();
            }
        }
    }

    public async Task<AssistantAgentTurn> SendAsync(AssistantAgentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var userText = request.Text ?? "";
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

        using var budget = Budget(AssistantProviderTimeouts.Default.Turn, cancellationToken);
        var acquired = false;
        try
        {
            await _gate.WaitAsync(budget.Token).ConfigureAwait(false);
            acquired = true;
            _tools.ConsumeUsedTools();
            _proposals.ConsumeUsedTools();
            _roomPatterns.ConsumeUsedTools();
            if (_session is null)
            {
                return new AssistantAgentTurn("BIM-IT Assistant", Status.SafeError ?? "The AI provider is unavailable.", true, ConsumeTurnTools());
            }

            var attachment = _delivery.ShouldDeliver(request.Attachment) ? request.Attachment : null;
            var message = await _session.SendAndWaitAsync(
                CopilotTurnComposer.Compose(userText.Trim(), attachment),
                AssistantProviderTimeouts.Default.Turn,
                budget.Token).ConfigureAwait(false);
            if (attachment is not null)
            {
                _delivery.MarkDelivered(attachment.ArtifactId);
            }
            var data = message?.Data;
            RememberModel(data?.Model);
            var content = AssistantSafeText.Clean(data?.Content, 16_000);
            if (string.IsNullOrWhiteSpace(content))
            {
                content = "The assistant did not return an answer.";
            }

            if (_client is not null)
            {
                try
                {
                    var completion = await PatternTargetOrchestrator.CompleteAsync(
                        _patternStore,
                        new GitHubCopilotPatternTargetReasoner(_client, _directory, Status.Model),
                        budget.Token).ConfigureAwait(false);
                    if (completion is not null)
                    {
                        content = PatternTargetCompletionText.Compose(completion);
                    }
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                }
            }

            return new AssistantAgentTurn("BIM-IT Assistant", content, false, ConsumeTurnTools(), _patternStore.PeekReviewJson());
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

            var timedOut = exception is OperationCanceledException;
            var safe = timedOut
                ? AssistantProviderTimeouts.TurnTimedOut
                : request.Attachment?.VisualPages.Count > 0 && IsVisionRejection(exception.Message)
                    ? "The AI provider could not read the attached pages."
                    : AssistantSafeText.Clean(exception.Message, 400);
            Status = Unavailable(string.IsNullOrWhiteSpace(safe) ? AssistantProviderTimeouts.TurnTimedOut : safe);
            return new AssistantAgentTurn("BIM-IT Assistant", Status.SafeError!, true, ConsumeTurnTools());
        }
        finally
        {
            if (acquired)
            {
                _gate.Release();
            }
        }

        IReadOnlyList<string> ConsumeTurnTools()
        {
            var names = new List<string>();
            foreach (var name in _tools.ConsumeUsedTools())
            {
                AddUsed(names, name);
            }

            foreach (var name in _roomPatterns.ConsumeUsedTools())
            {
                AddUsed(names, name);
            }

            return names;
        }
    }

    public async Task ResetConversationAsync(CancellationToken cancellationToken)
    {
        using var budget = Budget(AssistantProviderTimeouts.Default.Reset, cancellationToken);
        var acquired = false;
        try
        {
            await _gate.WaitAsync(budget.Token).ConfigureAwait(false);
            acquired = true;
            _proposalStore.Clear();
            _patternStore.Clear();
            await DisposeSessionAsync().ConfigureAwait(false);
            if (_client is null || Status.State == AssistantProviderState.Unavailable)
            {
                return;
            }

            _session = await CreateSessionAsync(budget.Token).ConfigureAwait(false);
            Status = ReadyStatus();
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException)
        {
            Status = Unavailable(AssistantProviderTimeouts.ResetTimedOut);
        }
        catch (Exception exception)
        {
            Status = Unavailable(AssistantSafeText.Clean(exception.Message, 400));
        }
        finally
        {
            if (acquired)
            {
                _gate.Release();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _proposalStore.Clear();
        _patternStore.Clear();
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
            GitHubCopilotSessionBlueprint.CreateSessionConfig(_directory, GitHubCopilotSessionBlueprint.CreateTools(_tools, _proposals, _roomPatterns)),
            cancellationToken).ConfigureAwait(false);
        _modelEvents?.Dispose();
        _modelEvents = session.On<SessionModelChangeEvent>(change => RememberModel(change.Data.NewModel));
        return session;
    }

    private static bool IsVisionRejection(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        return message.Contains("vision", StringComparison.OrdinalIgnoreCase)
            || message.Contains("image input", StringComparison.OrdinalIgnoreCase)
            || message.Contains("does not support image", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddUsed(List<string> names, string name)
    {
        if (names.Count >= AssistantIpc.MaxUsedToolCount || !AssistantIpc.IsModelToolName(name))
        {
            return;
        }

        names.Add(name);
    }

    private void RememberModel(string? model)
    {
        if (!AssistantProviderStatus.IsConcreteModel(model) || Status.State != AssistantProviderState.Ready)
        {
            return;
        }

        Status = Status with { Model = model };
    }

    private CancellationTokenSource Budget(TimeSpan budget, CancellationToken cancellationToken)
    {
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        linked.CancelAfter(budget);
        return linked;
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
        _delivery.Reset();
        try
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }
}
