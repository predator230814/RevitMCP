using GitHub.Copilot;
using Microsoft.Extensions.AI;

namespace BimIt.Revit.Assistant.Providers.GitHubCopilot;

internal static class GitHubCopilotSessionBlueprint
{
    public const string ProviderName = "GitHub Copilot";

    public const string AutoModel = "auto";

    public const string SystemPrompt =
        """
        You are BIM-IT Assistant operating on the current Revit session.
        Use the BIM-IT tools when model state is needed.
        Do not claim to know the current model state without using the tools when the question depends on it.
        Treat tool results as authoritative Revit observations.
        Do not invent unavailable model information.
        You currently have read-only BIM-IT capabilities.
        You cannot modify the Revit model.
        Never imply that a write was performed.
        """;

    public const string SignInRequired =
        "GitHub Copilot sign-in is required. Sign in to GitHub Copilot for this Windows user, then try again. Model Context, Analyze Warnings, and Active View Elements still work.";

    public static CopilotClientOptions CreateClientOptions(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        return new CopilotClientOptions
        {
            Mode = CopilotClientMode.Empty,
            BaseDirectory = baseDirectory,
            WorkingDirectory = baseDirectory,
            UseLoggedInUser = true,
            GitHubToken = null,
            LogLevel = CopilotLogLevel.None,
            EnableRemoteSessions = false,
            BuiltinPluginDirectories = [],
            Telemetry = null,
        };
    }

    public static IReadOnlyList<AIFunction> CreateTools(BimItAgentReadTools tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        return
        [
            Define(tools, BimItAgentReadTools.ModelContext, "Read the current Revit session: version, active document, active view, and selection count. Takes no arguments."),
            Define(tools, BimItAgentReadTools.Warnings, "Read warnings for the active Revit document. Takes no arguments. An empty warning list is a real result."),
            Define(tools, BimItAgentReadTools.ActiveViewElements, "Read up to 25 elements in the active Revit view. Takes no arguments."),
        ];
    }

    public static SessionConfig CreateSessionConfig(string workingDirectory, IReadOnlyList<AIFunction> tools)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(tools);
        if (tools.Count != BimItAgentReadTools.Names.Length
            || tools.Select(tool => tool.Name).Except(BimItAgentReadTools.Names, StringComparer.Ordinal).Any())
        {
            throw new ArgumentException("The Copilot session can expose only the three BIM-IT read tools.", nameof(tools));
        }

        var allowed = new ToolSet();
        foreach (var name in BimItAgentReadTools.Names)
        {
            allowed.AddCustom(name);
        }

        return new SessionConfig
        {
            Model = AutoModel,
            Streaming = false,
            WorkingDirectory = workingDirectory,
            EnableSessionStore = false,
            EnableSkills = false,
            Memory = new MemoryConfiguration { Enabled = false },
            InfiniteSessions = new InfiniteSessionConfig { Enabled = false },
            McpServers = new Dictionary<string, McpServerConfig>(),
            AvailableTools = allowed,
            ExcludedTools = [],
            Tools = tools.Cast<AIFunctionDeclaration>().ToList(),
            SystemMessage = new SystemMessageConfig
            {
                Mode = SystemMessageMode.Append,
                Content = SystemPrompt,
            },
            OnPermissionRequest = Decide,
#pragma warning disable GHCP001 // Empty mode keeps MCP Apps off. The property is experimental in SDK 1.0.21.
            EnableMcpApps = false,
#pragma warning restore GHCP001
            EnableHostGitOperations = false,
            EnableFileHooks = false,
            EnableConfigDiscovery = false,
            SkipCustomInstructions = true,
            EnableOnDemandInstructionDiscovery = false,
            CoauthorEnabled = false,
            ManageScheduleEnabled = false,
            EnableExperimentalMode = false,
            SkillDirectories = [],
            PluginDirectories = [],
            HookDirectories = [],
            AgentDirectories = [],
            InstructionDirectories = [],
            IncludedBuiltinSkills = [],
            CustomAgents = [],
            Commands = [],
            GitHubToken = null,
        };
    }

#pragma warning disable GHCP001 // PermissionDecision is the current SDK permission contract.
    private static Task<GitHub.Copilot.Rpc.PermissionDecision> Decide(PermissionRequest request, PermissionInvocation invocation)
    {
        if (GitHubCopilotPermissions.IsAllowed(request))
        {
            return Task.FromResult(GitHub.Copilot.Rpc.PermissionDecision.ApproveOnce());
        }

        return Task.FromResult(GitHub.Copilot.Rpc.PermissionDecision.Reject("BIM-IT Assistant cannot grant that capability."));
    }
#pragma warning restore GHCP001

    private static AIFunction Define(BimItAgentReadTools tools, string name, string description)
    {
        return CopilotTool.DefineTool(
            (CancellationToken cancellationToken) => tools.InvokeAsync(name, cancellationToken),
            new CopilotToolOptions
            {
                SkipPermission = true,
                Defer = CopilotToolDefer.Never,
            },
            new AIFunctionFactoryOptions
            {
                Name = name,
                Description = description,
            });
    }
}
