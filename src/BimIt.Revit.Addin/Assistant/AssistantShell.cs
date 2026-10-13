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

    public AssistantShell(
        Func<string> instanceId,
        Func<bool> bridgeReady,
        Func<(string? Version, string? Build)> revitIdentity,
        IAssistantProcessFactory processes,
        IAssistantLink link,
        AssistantIpcTimeouts? timeouts = null)
    {
        _instanceId = instanceId;
        _bridgeReady = bridgeReady;
        _revitIdentity = revitIdentity;
        _processes = new AssistantProcessSupervisor(processes);
        _link = link;
        _timeouts = timeouts ?? AssistantIpcTimeouts.Default;
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
                _ = RunWorkflowAsync(parsed.Action!, DisplayCommand(parsed.Action!));
                break;
            case AssistantPageIntent.SendCommand:
                _ = HandleCommandAsync(parsed.Text!);
                break;
            case AssistantPageIntent.NewConversation:
                _conversation.Reset();
                PostSnapshot();
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

    public async Task ShutdownAsync()
    {
        StopProcessWatch();
        try
        {
            await _link.ShutdownAsync().WaitAsync(_timeouts.Shutdown).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Assistant shutdown must not fail Revit shutdown. The process kill below is the fallback.
        }

        _processes.Shutdown();
        _pane?.DisposeSurface();
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
        if (workflow is null)
        {
            _conversation.Add(
                "assistant",
                "BIM-IT Assistant",
                "No AI provider is configured yet. Use Model Context, Analyze Warnings, Active View Elements, or the commands /context, /warnings, and /active-view.");
            PostSnapshot();
            return;
        }

        await RunWorkflowAsync(workflow, text).ConfigureAwait(false);
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
            if (_mcpState == "ready")
            {
                _processes.MarkReady();
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

    private string SerializeSnapshot()
    {
        var messages = _conversation.Entries.Select(entry => new Dictionary<string, string>
        {
            ["role"] = entry.Role,
            ["title"] = entry.Title,
            ["body"] = entry.Body,
        }).ToArray();
        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["type"] = "snapshot",
            ["status"] = new Dictionary<string, object?>
            {
                ["revitBridge"] = _bridgeReady() ? "ready" : "unavailable",
                ["assistant"] = _assistantState,
                ["mcp"] = _mcpState,
                ["aiProvider"] = "Not configured",
                ["toolCount"] = _toolCount,
                ["toolNames"] = _toolNames,
                ["error"] = _statusError,
                ["busy"] = _busy,
                ["diagnostics"] = _diagnostics,
            },
            ["messages"] = messages,
            ["empty"] = messages.Length == 0,
        });
    }

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
