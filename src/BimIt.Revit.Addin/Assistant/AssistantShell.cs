using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using Autodesk.Revit.UI;
using BimIt.Revit.Assistant.Ipc;

namespace BimIt.Revit.Addin.Assistant;

internal sealed class AssistantShell
{
    private readonly Func<string> _instanceId;
    private readonly Func<bool> _bridgeReady;
    private readonly Func<(string? Version, string? Build)> _revitIdentity;
    private readonly AssistantProcessSupervisor _processes;
    private readonly IAssistantLink _link;
    private readonly AssistantConversation _conversation = new();
    private readonly IAssistantDocumentPicker _documents;
    private readonly AssistantIpcTimeouts _timeouts;
    private readonly object _gate = new();
    private Task? _startup;
    private Timer? _processWatch;
    private AssistantPaneHost? _pane;
    private string? _pipeName;
    private bool _openRequested;
    private bool _diagnostics;
    private bool _busy;
    private string _assistantState = "unavailable";
    private string _mcpState = "unavailable";
    private int? _toolCount;
    private IReadOnlyList<string> _toolNames = [];
    private string? _statusError;
    private string _providerState = "not_configured";
    private string _providerLabel = "Not configured";
    private string? _providerError;
    private AssistantAttachmentChip? _attachment;
    private JsonElement? _patternReview;
    private CancellationTokenSource? _providerPoll;
    private Task _sequence = Task.CompletedTask;

    public AssistantShell(
        Func<string> instanceId,
        Func<bool> bridgeReady,
        Func<(string? Version, string? Build)> revitIdentity,
        IAssistantProcessFactory processes,
        IAssistantLink link,
        AssistantIpcTimeouts? timeouts = null,
        IAssistantDocumentPicker? documentPicker = null)
    {
        _instanceId = instanceId;
        _bridgeReady = bridgeReady;
        _revitIdentity = revitIdentity;
        _processes = new AssistantProcessSupervisor(processes);
        _link = link;
        _timeouts = timeouts ?? AssistantIpcTimeouts.Default;
        _documents = documentPicker ?? new AssistantOpenFilePicker();
    }

    public AssistantConversation Conversation => _conversation;

    public AssistantProcessSupervisor Processes => _processes;

    public bool OpenRequested => _openRequested;

    public void AttachPane(AssistantPaneHost pane)
    {
        _pane = pane;
    }

    public void OnRevitStartup()
    {
        _processes.OnRevitStartup();
    }

    public void Show(UIApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);
        _openRequested = true;
        try
        {
            var pane = application.GetDockablePane(new DockablePaneId(AssistantPaneIds.PaneId));
            pane.Show();
        }
        catch (Exception)
        {
            _statusError = "The Assistant pane could not be shown.";
        }

        _pane?.NotifyOpened();
        _ = OpenSessionAsync();
    }

    internal Task OpenSessionAsync() => EnsureAssistantAsync();

    public void ReceivePageMessage(string json)
    {
        var parsed = AssistantPageMessages.Parse(json);
        if (!parsed.Accepted)
        {
            return;
        }

        switch (parsed.Intent)
        {
            case AssistantPageIntent.PageReady:
                PostSnapshot();
                break;
            case AssistantPageIntent.RunQuickAction:
                Sequence(() => RunWorkflowAsync(parsed.Action!, DisplayCommand(parsed.Action!)));
                break;
            case AssistantPageIntent.SendCommand:
                Sequence(() => HandleCommandAsync(parsed.Text!));
                break;
            case AssistantPageIntent.NewConversation:
                _conversation.Reset();
                _attachment = null;
                _patternReview = null;
                PostSnapshot();
                Sequence(ResetProviderAsync);
                break;
            case AssistantPageIntent.AttachDocument:
                AttachFromPicker();
                break;
            case AssistantPageIntent.RemoveDocument:
                Sequence(RemoveDocumentAsync);
                break;
            case AssistantPageIntent.ConfirmProjectAxesPreview:
                Sequence(ConfirmProjectAxesPreviewAsync);
                break;
            case AssistantPageIntent.ReviewInRevit:
                Sequence(() => LocalPlacementActionAsync(
                    "Review in Revit",
                    "The Revit review could not be opened.",
                    token => _link.ReviewInRevitAsync(token)));
                break;
            case AssistantPageIntent.ApplyApprovedPlacements:
                Sequence(() => LocalPlacementActionAsync(
                    "Apply approved placements",
                    "The placement apply could not be completed.",
                    token => _link.ApplyApprovedPlacementsAsync(token)));
                break;
            case AssistantPageIntent.OpenDiagnostics:
                _diagnostics = !_diagnostics;
                PostSnapshot();
                break;
            case AssistantPageIntent.RetryConnection:
                _ = RestartAsync();
                break;
        }
    }

    public Task ShutdownAsync()
    {
        ShutdownForHost();
        return Task.CompletedTask;
    }

    internal void ShutdownForHost()
    {
        StopProviderPoll();
        StopProcessWatch();
        try
        {
            _link.ShutdownAsync().Wait(_timeouts.Shutdown);
        }
        catch (Exception)
        {
            // A hung or failed pipe must not skip the process-kill fallback.
        }

        _processes.Shutdown();
        try
        {
            _pane?.DisposeSurface();
        }
        catch (Exception)
        {
            // Pane disposal cannot block the process-kill fallback or Revit shutdown.
        }
    }

    internal void ObserveAssistantProcess()
    {
        if (!_processes.ObserveExit())
        {
            return;
        }

        lock (_gate)
        {
            _assistantState = "unavailable";
            _mcpState = "unavailable";
            _providerState = "unavailable";
            _providerLabel = "GitHub Copilot · unavailable";
            _statusError = _processes.Failure;
        }

        PostSnapshot();
    }

    private Task EnsureAssistantAsync()
    {
        lock (_gate)
        {
            if (IsSessionReady())
            {
                PostSnapshot();
                return Task.CompletedTask;
            }

            if (_startup is { IsCompleted: false })
            {
                return _startup;
            }

            _startup = StartAssistantCoreAsync();
            return _startup;
        }
    }

    private bool IsSessionReady()
    {
        return _processes.Phase == AssistantProcessPhase.Ready && _mcpState == "ready" && _processes.IsAlive;
    }

    private async Task StartAssistantCoreAsync()
    {
        var executable = AssistantDeployment.FindAssistant(typeof(AssistantShell).Assembly.Location);
        if (executable is null)
        {
            _assistantState = "unavailable";
            _statusError = "BimIt.Revit.Assistant executable was not found.";
            _processes.MarkFailed(_statusError);
            PostSnapshot();
            return;
        }

        _pipeName = AssistantPipeNames.Create(Environment.ProcessId, Guid.NewGuid().ToString("N"));
        var server = AssistantDeployment.FindServer(executable);
        var identity = _revitIdentity();
        var arguments = $"--pipe \"{_pipeName}\" --instance-id \"{_instanceId()}\""
            + (server is null ? "" : $" --server \"{server}\"")
            + (identity.Version is null ? "" : $" --revit-version \"{identity.Version}\"")
            + (identity.Build is null ? "" : $" --revit-build \"{identity.Build}\"");
        _assistantState = "starting";
        _mcpState = "starting";
        PostSnapshot();
        if (!_processes.EnsureStarted(executable, arguments))
        {
            _assistantState = "unavailable";
            _mcpState = "unavailable";
            _statusError = _processes.Failure;
            PostSnapshot();
            return;
        }

        try
        {
            using var connect = new CancellationTokenSource(_timeouts.Connect);
            var status = await _link.ConnectAsync(
                _pipeName,
                _instanceId(),
                identity.Version,
                identity.Build,
                _bridgeReady(),
                connect.Token).ConfigureAwait(false);
            ApplyStatus(status);
            if (_mcpState == "ready")
            {
                StartProcessWatch();
            }
        }
        catch (Exception exception)
        {
            _processes.MarkFailed(exception.Message);
            _assistantState = "unavailable";
            _mcpState = "unavailable";
            _statusError = exception is OperationCanceledException
                ? "The Assistant connection timed out."
                : exception.Message;
            PostSnapshot();
        }
    }

    private Task RestartAsync()
    {
        Task previous;
        lock (_gate)
        {
            previous = _startup ?? Task.CompletedTask;
            _startup = RestartCoreAsync(previous);
            return _startup;
        }
    }

    private async Task RestartCoreAsync(Task previous)
    {
        try
        {
            await previous.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The failed session is replaced by this retry.
        }

        StopProviderPoll();
        StopProcessWatch();
        try
        {
            await _link.ShutdownAsync().WaitAsync(_timeouts.Shutdown).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Process shutdown below is the fallback when the pipe does not close in time.
        }

        _processes.Shutdown();
        lock (_gate)
        {
            _assistantState = "unavailable";
            _mcpState = "unavailable";
            _providerState = "not_configured";
            _providerLabel = "Not configured";
            _providerError = null;
            _statusError = null;
        }

        await StartAssistantCoreAsync().ConfigureAwait(false);
    }

    private void StartProcessWatch()
    {
        StopProcessWatch();
        _processWatch = new Timer(_ => ObserveAssistantProcess(), null, _timeouts.ProcessWatch, _timeouts.ProcessWatch);
    }

    private void StopProcessWatch()
    {
        _processWatch?.Dispose();
        _processWatch = null;
    }

    private async Task HandleCommandAsync(string text)
    {
        _conversation.Add("user", "You", text);
        var workflow = text.Trim() switch
        {
            "/context" => "context",
            "/warnings" => "warnings",
            "/active-view" => "activeView",
            _ => null,
        };
        if (workflow is not null)
        {
            await RunWorkflowAsync(workflow, text).ConfigureAwait(false);
            return;
        }

        await RunChatAsync(text).ConfigureAwait(false);
    }

    private async Task RunChatAsync(string text)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        PostSnapshot();
        try
        {
            if (_mcpState != "ready")
            {
                await EnsureAssistantAsync().ConfigureAwait(false);
            }

            if (_mcpState != "ready")
            {
                _conversation.Add("error", "Assistant", _statusError ?? "The Assistant is not ready.");
                return;
            }

            using var chatTimeout = new CancellationTokenSource(_timeouts.Chat);
            var result = await _link.ChatAsync(Guid.NewGuid().ToString("N"), text, chatTimeout.Token).ConfigureAwait(false);
            if (result is null || result.Kind != AssistantIpcKind.ChatResult)
            {
                _conversation.Add("error", "Assistant", "The Assistant did not return an answer.");
                _processes.MarkFailed("missing chat result");
                _assistantState = "unavailable";
                _mcpState = "unavailable";
                return;
            }

            ApplyProvider(result);
            if (result.PatternReview is not null && RoomPatternReview.TryParse(result.PatternReview, out var review))
            {
                _patternReview = review;
            }

            _conversation.Add(
                result.IsError ? "error" : "assistant",
                result.Title ?? "BIM-IT Assistant",
                result.Body ?? "",
                UsedLabel(result.UsedTools));
        }
        catch (Exception exception)
        {
            var timedOut = exception is OperationCanceledException;
            var message = timedOut
                ? "The AI provider did not answer in time. The Assistant connection was closed and was not sent again."
                : exception.Message;
            _processes.MarkFailed(message);
            _assistantState = "unavailable";
            _mcpState = "unavailable";
            _providerState = "unavailable";
            _providerLabel = "GitHub Copilot · unavailable";
            _statusError = message;
            _conversation.Add("error", "Assistant", message);
        }
        finally
        {
            _busy = false;
            PostSnapshot();
        }
    }

    private void AttachFromPicker()
    {
        string? selected;
        try
        {
            selected = _documents.TryPickSupportedDocument();
        }
        catch (Exception)
        {
            _conversation.Add("error", "Assistant", "The document could not be chosen.");
            PostSnapshot();
            return;
        }

        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        Sequence(() => AttachDocumentAsync(selected));
    }

    private async Task AttachDocumentAsync(string path)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        PostSnapshot();
        try
        {
            if (_mcpState != "ready")
            {
                await EnsureAssistantAsync().ConfigureAwait(false);
            }

            if (_mcpState != "ready")
            {
                _conversation.Add("error", "Assistant", _statusError ?? "The Assistant is not ready.");
                return;
            }

            using var timeout = new CancellationTokenSource(_timeouts.Chat);
            var result = await _link.AttachDocumentAsync(path, timeout.Token).ConfigureAwait(false);
            ApplyDocumentResult(result);
        }
        catch (Exception exception)
        {
            var message = exception is OperationCanceledException
                ? "The document could not be attached."
                : "The document could not be attached.";
            _conversation.Add("error", "Assistant", message);
        }
        finally
        {
            _busy = false;
            PostSnapshot();
        }
    }

    private Task LocalPlacementActionAsync(
        string userLine,
        string failure,
        Func<CancellationToken, Task<AssistantIpcMessage?>> call)
    {
        return RunLocalPlacementActionAsync(userLine, failure, call);
    }

    private async Task RunLocalPlacementActionAsync(
        string userLine,
        string failure,
        Func<CancellationToken, Task<AssistantIpcMessage?>> call)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        _conversation.Add("user", "You", userLine);
        PostSnapshot();
        try
        {
            if (_mcpState != "ready")
            {
                await EnsureAssistantAsync().ConfigureAwait(false);
            }

            if (_mcpState != "ready")
            {
                _conversation.Add("error", "Assistant", _statusError ?? "The Assistant is not ready.");
                return;
            }

            using var timeout = new CancellationTokenSource(_timeouts.Workflow);
            var result = await call(timeout.Token).ConfigureAwait(false);
            if (result?.Kind != AssistantIpcKind.PreviewResult)
            {
                _conversation.Add("error", "Assistant", failure);
                return;
            }

            _conversation.Add(result.IsError ? "error" : "assistant", result.Title ?? "BIM-IT Assistant", result.Body ?? "");
        }
        catch (Exception)
        {
            _conversation.Add("error", "Assistant", failure);
        }
        finally
        {
            _busy = false;
            PostSnapshot();
        }
    }

    private async Task ConfirmProjectAxesPreviewAsync()
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        _conversation.Add("user", "You", "Confirm project axes & preview");
        PostSnapshot();
        try
        {
            if (_mcpState != "ready")
            {
                await EnsureAssistantAsync().ConfigureAwait(false);
            }

            if (_mcpState != "ready")
            {
                _conversation.Add("error", "Assistant", _statusError ?? "The Assistant is not ready.");
                return;
            }

            using var timeout = new CancellationTokenSource(_timeouts.Workflow);
            var result = await _link.ConfirmProjectAxesPreviewAsync(timeout.Token).ConfigureAwait(false);
            if (result?.Kind != AssistantIpcKind.PreviewResult)
            {
                _conversation.Add("error", "Assistant", "The Assistant did not return a preview.");
                return;
            }

            _conversation.Add(result.IsError ? "error" : "assistant", result.Title ?? "Placement preview", result.Body ?? "");
        }
        catch (Exception)
        {
            _conversation.Add("error", "Assistant", "The placement preview could not be created.");
        }
        finally
        {
            _busy = false;
            PostSnapshot();
        }
    }

    private async Task RemoveDocumentAsync()
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        PostSnapshot();
        try
        {
            if (_mcpState != "ready")
            {
                _attachment = null;
                _patternReview = null;
                return;
            }

            using var timeout = new CancellationTokenSource(_timeouts.Workflow);
            var result = await _link.RemoveDocumentAsync(timeout.Token).ConfigureAwait(false);
            if (result?.Kind == AssistantIpcKind.DocumentResult && result.Attached != true)
            {
                _attachment = null;
                _patternReview = null;
            }
            else
            {
                _conversation.Add("error", "Assistant", SafeDocumentMessage(result?.DocumentMessage) ?? "The document could not be removed.");
            }
        }
        catch (Exception)
        {
            _conversation.Add("error", "Assistant", "The document could not be removed.");
        }
        finally
        {
            _busy = false;
            PostSnapshot();
        }
    }

    private void ApplyDocumentResult(AssistantIpcMessage? result)
    {
        if (result?.Kind != AssistantIpcKind.DocumentResult)
        {
            _conversation.Add("error", "Assistant", "The document could not be attached.");
            return;
        }

        if (result.Attached == true && !string.IsNullOrWhiteSpace(result.DisplayName) && result.DocumentKind is not null)
        {
            _attachment = new AssistantAttachmentChip(
                result.DisplayName,
                AssistantAttachmentSummary.Describe(result.DocumentKind, result.PageCount, result.VisualCount, result.Truncated == true));
            _patternReview = null;
            return;
        }

        _conversation.Add("error", "Assistant", SafeDocumentMessage(result.DocumentMessage) ?? "The document could not be attached.");
    }

    private static string? SafeDocumentMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Contains(":\\", StringComparison.Ordinal) || message.Contains("\\\\", StringComparison.Ordinal))
        {
            return null;
        }

        return message;
    }

    private async Task ResetProviderAsync()
    {
        _conversation.Reset();
        _attachment = null;
        _patternReview = null;
        PostSnapshot();
        if (_mcpState != "ready")
        {
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(_timeouts.Workflow);
            var status = await _link.ResetConversationAsync(timeout.Token).ConfigureAwait(false);
            if (status?.Kind == AssistantIpcKind.Status)
            {
                ApplyProvider(status);
                PostSnapshot();
            }
        }
        catch (Exception exception)
        {
            var message = exception is OperationCanceledException
                ? "The conversation reset timed out."
                : exception.Message;
            _processes.MarkFailed(message);
            _assistantState = "unavailable";
            _mcpState = "unavailable";
            _statusError = message;
            _conversation.Add("error", "Assistant", message);
            PostSnapshot();
        }
    }

    private async Task RunWorkflowAsync(string action, string userTitle)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        if (!_conversation.Entries.Any(entry => entry.Body == userTitle))
        {
            _conversation.Add("user", "You", DisplayCommand(action));
        }

        PostSnapshot();
        try
        {
            if (_mcpState != "ready")
            {
                await EnsureAssistantAsync().ConfigureAwait(false);
            }

            if (_mcpState != "ready")
            {
                _conversation.Add("error", "Assistant", _statusError ?? "The Assistant is not ready.");
                return;
            }

            var workflow = action switch
            {
                "context" => AssistantWorkflowName.Context,
                "warnings" => AssistantWorkflowName.Warnings,
                "activeView" => AssistantWorkflowName.ActiveView,
                _ => (AssistantWorkflowName?)null,
            };
            if (workflow is null)
            {
                return;
            }

            using var workflowTimeout = new CancellationTokenSource(_timeouts.Workflow);
            var result = await _link.RunAsync(Guid.NewGuid().ToString("N"), workflow.Value, workflowTimeout.Token).ConfigureAwait(false);
            if (result is null || result.Kind != AssistantIpcKind.WorkflowResult)
            {
                _conversation.Add("error", "Assistant", "The Assistant did not return a result.");
                _processes.MarkFailed("missing result");
                _assistantState = "unavailable";
                _mcpState = "unavailable";
                return;
            }

            _conversation.Add(result.IsError ? "error" : "assistant", result.Title ?? "Assistant", result.Body ?? "");
        }
        catch (Exception exception)
        {
            var timedOut = exception is OperationCanceledException;
            var message = timedOut ? "The Assistant workflow timed out. It was not sent again." : exception.Message;
            _processes.MarkFailed(message);
            _assistantState = "unavailable";
            _mcpState = "unavailable";
            _statusError = message;
            _conversation.Add("error", "Assistant", message);
        }
        finally
        {
            _busy = false;
            PostSnapshot();
        }
    }

    private void ApplyStatus(AssistantIpcMessage? status)
    {
        if (status is null || status.Kind != AssistantIpcKind.Status)
        {
            _assistantState = "unavailable";
            _mcpState = "unavailable";
            _statusError = "The Assistant did not report status.";
            _processes.MarkFailed(_statusError);
        }
        else
        {
            _assistantState = status.AssistantState ?? "unavailable";
            _mcpState = status.McpState ?? "unavailable";
            _toolCount = status.ToolCount;
            _toolNames = status.ToolNames ?? [];
            _statusError = status.Error;
            ApplyProvider(status);
            if (_mcpState == "ready")
            {
                _processes.MarkReady();
                StartProviderPoll();
            }
            else
            {
                _processes.MarkFailed(status.Error ?? "MCP unavailable");
            }
        }

        PostSnapshot();
    }

    private void PostSnapshot()
    {
        _pane?.Post(SerializeSnapshot());
    }

    internal string SerializeSnapshot()
    {
        var messages = _conversation.Entries.Select(entry =>
        {
            var message = new Dictionary<string, string>
            {
                ["role"] = entry.Role,
                ["title"] = entry.Title,
                ["body"] = entry.Body,
            };
            if (!string.IsNullOrWhiteSpace(entry.Used))
            {
                message["used"] = entry.Used;
            }

            return message;
        }).ToArray();
        var snapshot = new Dictionary<string, object?>
        {
            ["type"] = "snapshot",
            ["status"] = new Dictionary<string, object?>
            {
                ["revitBridge"] = _bridgeReady() ? "ready" : "unavailable",
                ["assistant"] = _assistantState,
                ["mcp"] = _mcpState,
                ["aiProvider"] = _providerLabel,
                ["aiProviderState"] = _providerState,
                ["providerError"] = _providerError,
                ["toolCount"] = _toolCount,
                ["toolNames"] = _toolNames,
                ["error"] = _statusError,
                ["busy"] = _busy,
                ["diagnostics"] = _diagnostics,
            },
            ["messages"] = messages,
            ["empty"] = messages.Length == 0,
        };
        if (_attachment is not null)
        {
            snapshot["attachment"] = new Dictionary<string, string>
            {
                ["displayName"] = _attachment.DisplayName,
                ["summary"] = _attachment.Summary,
            };
        }

        if (_patternReview is JsonElement review)
        {
            snapshot["patternReview"] = review;
        }

        return JsonSerializer.Serialize(snapshot);
    }

    private static string? UsedLabel(IReadOnlyList<string>? names)
    {
        if (names is null || names.Count == 0)
        {
            return null;
        }

        var labels = new List<string>();
        foreach (var name in names)
        {
            var label = name switch
            {
                "bimit_get_model_context" => "Model context",
                "bimit_get_warnings" => "Analyze warnings",
                "bimit_get_active_view_elements" => "Active view",
                "bimit_inspect_selection" => "Inspect selection",
                "bimit_explore_selected_mep" => "Explore selected MEP",
                "bimit_propose_mep_device_placements" => "Propose MEP placements",
                "bimit_propose_room_pattern" => "Propose room pattern",
                _ => null,
            };
            if (label is not null && !labels.Contains(label, StringComparer.Ordinal))
            {
                labels.Add(label);
            }
        }

        return labels.Count == 0 ? null : "Used: " + string.Join(", ", labels);
    }

    private void ApplyProvider(AssistantIpcMessage status)
    {
        if (status.ProviderState is null)
        {
            return;
        }

        _providerState = status.ProviderState;
        if (!string.IsNullOrWhiteSpace(status.ProviderLabel))
        {
            _providerLabel = status.ProviderLabel;
        }

        _providerError = status.ProviderError;
    }

    private void StartProviderPoll()
    {
        if (_providerState != "starting" || _providerPoll is not null)
        {
            return;
        }

        var poll = new CancellationTokenSource();
        _providerPoll = poll;
        _ = PollProviderAsync(poll.Token);
    }

    private void StopProviderPoll()
    {
        _providerPoll?.Cancel();
        _providerPoll?.Dispose();
        _providerPoll = null;
    }

    private async Task PollProviderAsync(CancellationToken cancellationToken)
    {
        try
        {
            for (var attempt = 0; attempt < 60 && !cancellationToken.IsCancellationRequested; attempt++)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
                if (_providerState != "starting")
                {
                    return;
                }

                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(_timeouts.Connect);
                    var status = await _link.QueryStatusAsync(timeout.Token).ConfigureAwait(false);
                    if (status?.Kind == AssistantIpcKind.Status)
                    {
                        ApplyProvider(status);
                        PostSnapshot();
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                }

                if (_providerState != "starting")
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
        }
    }

    private void Sequence(Func<Task> action)
    {
        lock (_gate)
        {
            var previous = _sequence;
            _sequence = InvokeAfter(previous, action);
        }
    }

    private static async Task InvokeAfter(Task previous, Func<Task> action)
    {
        try
        {
            await previous.ConfigureAwait(false);
        }
        catch (Exception)
        {
        }

        await action().ConfigureAwait(false);
    }

    private sealed record AssistantAttachmentChip(string DisplayName, string Summary);

    private static string DisplayCommand(string action)
    {
        return action switch
        {
            "context" => "Model context",
            "warnings" => "Analyze warnings",
            "activeView" => "Active view elements",
            _ => action,
        };
    }
}

internal static class AssistantDeployment
{
    public static string? FindAssistant(string? addinAssemblyPath)
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("BIMIT_REVIT_ASSISTANT");
        if (File.Exists(fromEnvironment))
        {
            return Path.GetFullPath(fromEnvironment);
        }

        var start = string.IsNullOrEmpty(addinAssemblyPath)
            ? AppContext.BaseDirectory
            : Path.GetDirectoryName(addinAssemblyPath) ?? AppContext.BaseDirectory;
        return Find(start, "BimIt.Revit.Assistant.exe",
            Path.Combine("src", "BimIt.Revit.Assistant", "bin", "Release", "net10.0", "BimIt.Revit.Assistant.exe"),
            Path.Combine("src", "BimIt.Revit.Assistant", "bin", "Debug", "net10.0", "BimIt.Revit.Assistant.exe"));
    }

    public static string? FindServer(string assistantExecutable)
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("BIMIT_REVIT_SERVER");
        if (File.Exists(fromEnvironment))
        {
            return Path.GetFullPath(fromEnvironment);
        }

        var start = Path.GetDirectoryName(assistantExecutable) ?? AppContext.BaseDirectory;
        return Find(start, "BimIt.Revit.Server.exe",
            Path.Combine("src", "BimIt.Revit.Server", "bin", "Release", "net10.0", "BimIt.Revit.Server.exe"),
            Path.Combine("src", "BimIt.Revit.Server", "bin", "Debug", "net10.0", "BimIt.Revit.Server.exe"));
    }

    private static string? Find(string startDirectory, params string[] tails)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null)
        {
            foreach (var tail in tails)
            {
                var candidate = Path.Combine(directory.FullName, tail);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            directory = directory.Parent;
        }

        return null;
    }
}

internal sealed class ProcessAssistantFactory : IAssistantProcessFactory
{
    public IAssistantProcessHandle Start(string fileName, string arguments)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        if (!process.Start())
        {
            throw new InvalidOperationException("The Assistant process did not start.");
        }

        return new ProcessHandle(process);
    }

    private sealed class ProcessHandle(Process process) : IAssistantProcessHandle
    {
        public bool HasExited => process.HasExited;

        public void Kill() => process.Kill(entireProcessTree: true);

        public void Dispose() => process.Dispose();
    }
}
