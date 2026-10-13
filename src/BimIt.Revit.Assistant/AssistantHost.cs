using BimIt.Revit.Assistant.Ipc;

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
    public static async Task<int> RunAsync(AssistantHostOptions options, CancellationToken cancellationToken = default)
    {
        var serverPath = AssistantExecutableLocator.ResolveServer(options.ServerPath, AppContext.BaseDirectory);
        await using var session = serverPath is null ? null : new McpAssistantToolSession(serverPath);
        await using var server = AssistantPipeStreams.CreateServer(options.PipeName);
        using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectTimeout.CancelAfter(TimeSpan.FromSeconds(30));
        await server.WaitForConnectionAsync(connectTimeout.Token).ConfigureAwait(false);

        var helloJson = await AssistantPipeFrames.ReadAsync(server, cancellationToken).ConfigureAwait(false);
        var hello = AssistantIpc.Parse(helloJson);
        if (!hello.Accepted || hello.Message?.Kind != AssistantIpcKind.Hello || !string.Equals(hello.Message.InstanceId, options.InstanceId, StringComparison.Ordinal))
        {
            return 3;
        }

        var readiness = session is null
            ? new AssistantMcpReadiness(false, 0, [], "BimIt.Revit.Server executable was not found.")
            : await session.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await WriteAsync(server, Status(readiness), cancellationToken).ConfigureAwait(false);

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

    private static AssistantIpcMessage Status(AssistantMcpReadiness readiness)
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
            false);
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
