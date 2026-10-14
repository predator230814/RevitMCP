using BimIt.Revit.Assistant.Ipc;
using BimIt.Revit.Assistant.Providers.GitHubCopilot;

namespace BimIt.Revit.Assistant;

internal static class AssistantExecutableLocator
{
    public static string? ResolveServer(string? explicitPath, string startDirectory)
    {
        if (File.Exists(explicitPath))
        {
            return Path.GetFullPath(explicitPath);
        }

        var fromEnvironment = Environment.GetEnvironmentVariable("BIMIT_REVIT_SERVER");
        if (File.Exists(fromEnvironment))
        {
            return Path.GetFullPath(fromEnvironment);
        }

        return FindWalkingUp(
            startDirectory,
            "BimIt.Revit.Server.exe",
            Path.Combine("src", "BimIt.Revit.Server", "bin", "Release", "net10.0", "BimIt.Revit.Server.exe"),
            Path.Combine("src", "BimIt.Revit.Server", "bin", "Debug", "net10.0", "BimIt.Revit.Server.exe"));
    }

    public static string? FindWalkingUp(string startDirectory, params string[] relativeTails)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null)
        {
            foreach (var tail in relativeTails)
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

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        ChildProcessLifetime.BindToThisProcess();
        var options = AssistantHostOptions.Parse(args);
        if (options is null)
        {
            Console.Error.WriteLine("Usage: BimIt.Revit.Assistant --pipe <name> --instance-id <id> --server <path>");
            return 2;
        }

        try
        {
            return await AssistantHost.RunAsync(options).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }
}

internal sealed record AssistantHostOptions(string PipeName, string InstanceId, string? ServerPath, string? RevitVersion, string? RevitBuild)
{
    public static AssistantHostOptions? Parse(string[] args)
    {
        string? pipe = null;
        string? instanceId = null;
        string? server = null;
        string? version = null;
        string? build = null;
        for (var index = 0; index < args.Length; index++)
        {
            var value = index + 1 < args.Length ? args[index + 1] : null;
            switch (args[index])
            {
                case "--pipe":
                    pipe = value;
                    index++;
                    break;
                case "--instance-id":
                    instanceId = value;
                    index++;
                    break;
                case "--server":
                    server = value;
                    index++;
                    break;
                case "--revit-version":
                    version = value;
                    index++;
                    break;
                case "--revit-build":
                    build = value;
                    index++;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(pipe) || string.IsNullOrWhiteSpace(instanceId))
        {
            return null;
        }

        return new AssistantHostOptions(pipe, instanceId, server, version, build);
    }
}

internal static class AssistantHost
{
    public static Task<int> RunAsync(AssistantHostOptions options, CancellationToken cancellationToken = default)
    {
        return RunAsync(options, session: null, runtime: null, cancellationToken);
    }

    internal static async Task<int> RunAsync(
        AssistantHostOptions options,
        IAssistantToolSession? session,
        IAssistantAgentRuntime? runtime,
        CancellationToken cancellationToken = default)
    {
        McpAssistantToolSession? ownedSession = null;
        if (session is null)
        {
            var serverPath = AssistantExecutableLocator.ResolveServer(options.ServerPath, AppContext.BaseDirectory);
            if (serverPath is not null)
            {
                ownedSession = new McpAssistantToolSession(serverPath);
                session = ownedSession;
            }
        }

        await using var server = AssistantPipeStreams.CreateServer(options.PipeName);
        using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectTimeout.CancelAfter(TimeSpan.FromSeconds(30));
        await server.WaitForConnectionAsync(connectTimeout.Token).ConfigureAwait(false);

        var helloJson = await AssistantPipeFrames.ReadAsync(server, cancellationToken).ConfigureAwait(false);
        var hello = AssistantIpc.Parse(helloJson);
        if (!hello.Accepted || hello.Message?.Kind != AssistantIpcKind.Hello || !string.Equals(hello.Message.InstanceId, options.InstanceId, StringComparison.Ordinal))
        {
            if (ownedSession is not null)
            {
                await ownedSession.DisposeAsync().ConfigureAwait(false);
            }

            return 3;
        }

        var readiness = session is null
            ? new AssistantMcpReadiness(false, 0, [], "BimIt.Revit.Server executable was not found.")
            : await session.InitializeAsync(cancellationToken).ConfigureAwait(false);
        if (runtime is null)
        {
            runtime = readiness.Ready && session is not null
                ? new GitHubCopilotAgentRuntime(session, options.InstanceId)
                : new UnavailableAssistantAgentRuntime(readiness.Error);
        }

        var providerStart = StartProviderAsync(runtime, cancellationToken);
        try
        {
            await WriteAsync(server, Status(readiness, runtime.Status), cancellationToken).ConfigureAwait(false);

            while (!cancellationToken.IsCancellationRequested)
            {
                var json = await AssistantPipeFrames.ReadAsync(server, cancellationToken).ConfigureAwait(false);
                if (json is null)
                {
                    break;
                }

                var parsed = AssistantIpc.Parse(json);
                if (!parsed.Accepted || parsed.Message is null)
                {
                    continue;
                }

                if (parsed.Message.Kind == AssistantIpcKind.Shutdown)
                {
                    break;
                }

                if (parsed.Message.Kind == AssistantIpcKind.QueryStatus)
                {
                    await WriteAsync(server, Status(readiness, runtime.Status), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (parsed.Message.Kind == AssistantIpcKind.ResetConversation)
                {
                    try
                    {
                        await runtime.ResetConversationAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        // The runtime records provider failure. MCP readiness stays as it was.
                    }

                    await WriteAsync(server, Status(readiness, runtime.Status), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (parsed.Message.Kind == AssistantIpcKind.Chat)
                {
                    await WriteAsync(server, await ChatAsync(runtime, parsed.Message, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (parsed.Message.Kind != AssistantIpcKind.RunWorkflow || session is null || !readiness.Ready)
                {
                    if (parsed.Message.RequestId is not null)
                    {
                        await WriteAsync(
                            server,
                            Result(parsed.Message.RequestId, new AssistantWorkflowResult(
                                "Assistant",
                                readiness.Error ?? "The Assistant MCP client is not ready.",
                                true,
                                "mcp_unavailable")),
                            cancellationToken).ConfigureAwait(false);
                    }

                    continue;
                }

                var workflow = await AssistantWorkflows.RunAsync(
                    session,
                    options.InstanceId,
                    parsed.Message.Workflow!.Value,
                    cancellationToken).ConfigureAwait(false);
                await WriteAsync(server, Result(parsed.Message.RequestId!, workflow), cancellationToken).ConfigureAwait(false);
            }

            return 0;
        }
        finally
        {
            try
            {
                await runtime.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(4)).ConfigureAwait(false);
            }
            catch (Exception)
            {
            }

            try
            {
                await providerStart.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            }
            catch (Exception)
            {
            }

            if (ownedSession is not null)
            {
                try
                {
                    await ownedSession.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                }
                catch (Exception)
                {
                }
            }
        }
    }

    private static async Task StartProviderAsync(IAssistantAgentRuntime runtime, CancellationToken cancellationToken)
    {
        try
        {
            await runtime.InitializeAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Provider startup must not end the Assistant process.
        }
    }

    private static async Task<AssistantIpcMessage> ChatAsync(
        IAssistantAgentRuntime runtime,
        AssistantIpcMessage message,
        CancellationToken cancellationToken)
    {
        AssistantAgentTurn turn;
        try
        {
            turn = await runtime.SendAsync(message.Text ?? "", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            turn = new AssistantAgentTurn("BIM-IT Assistant", AssistantSafeText.Clean(exception.Message, 500), true);
        }

        var body = turn.Body.Length > AssistantIpc.MaxTextLength ? turn.Body[..AssistantIpc.MaxTextLength] : turn.Body;
        var status = runtime.Status;
        return new AssistantIpcMessage(
            AssistantIpcKind.ChatResult,
            null,
            null,
            null,
            false,
            message.RequestId,
            null,
            null,
            null,
            null,
            null,
            null,
            turn.Title,
            body,
            turn.IsError,
            string.IsNullOrWhiteSpace(status.Name) ? null : status.Name,
            status.StateToken,
            AssistantProviderStatus.IsModelToken(status.Model) ? status.Model : null,
            string.IsNullOrWhiteSpace(status.SafeError) ? null : AssistantSafeText.Clean(status.SafeError, 500),
            status.Label);
    }

    private static AssistantIpcMessage Status(AssistantMcpReadiness readiness, AssistantProviderStatus provider)
    {
        return new AssistantIpcMessage(
            AssistantIpcKind.Status,
            null,
            null,
            null,
            false,
            null,
            null,
            "ready",
            readiness.Ready ? "ready" : "unavailable",
            readiness.ToolCount,
            readiness.ToolNames,
            readiness.Error,
            null,
            null,
            false,
            string.IsNullOrWhiteSpace(provider.Name) ? null : provider.Name,
            provider.StateToken,
            AssistantProviderStatus.IsModelToken(provider.Model) ? provider.Model : null,
            string.IsNullOrWhiteSpace(provider.SafeError) ? null : AssistantSafeText.Clean(provider.SafeError, 500),
            provider.Label);
    }

    private static AssistantIpcMessage Result(string requestId, AssistantWorkflowResult workflow)
    {
        return new AssistantIpcMessage(
            AssistantIpcKind.WorkflowResult,
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
            workflow.Title,
            workflow.Body,
            workflow.IsError);
    }

    private static Task WriteAsync(Stream stream, AssistantIpcMessage message, CancellationToken cancellationToken)
    {
        return AssistantPipeFrames.WriteAsync(stream, AssistantIpc.Serialize(message), cancellationToken);
    }
}
