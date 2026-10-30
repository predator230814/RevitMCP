using System.Text.Json;
using GitHub.Copilot;
using Microsoft.Extensions.AI;

namespace BimIt.Revit.Assistant.Providers.GitHubCopilot;

/// <summary>
/// A Copilot tool for one generic Revit MCP read. Its name, description, and input schema come from the Server through
/// <see cref="AssistantReadTool"/>, so no schema is duplicated in the Assistant. Calls go only through the read facade.
/// </summary>
internal sealed class CopilotMcpReadFunction : AIFunction
{
    private static readonly IReadOnlyDictionary<string, object?> CopilotProperties = CopilotTool.DefineTool(
        (CancellationToken _) => Task.FromResult(0),
        new CopilotToolOptions
        {
            SkipPermission = true,
            Defer = CopilotToolDefer.Never,
        },
        new AIFunctionFactoryOptions { Name = "bimit_template" }).AdditionalProperties;

    private readonly AssistantReadTool _tool;
    private readonly AssistantMcpReadFacade _facade;

    public CopilotMcpReadFunction(AssistantReadTool tool, AssistantMcpReadFacade facade)
    {
        _tool = tool ?? throw new ArgumentNullException(nameof(tool));
        _facade = facade ?? throw new ArgumentNullException(nameof(facade));
    }

    public override string Name => _tool.Name;

    public override string Description => _tool.Description;

    public override JsonElement JsonSchema => _tool.ProviderSchema;

    public override IReadOnlyDictionary<string, object?> AdditionalProperties => CopilotProperties;

    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var supplied = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in arguments)
        {
            // Model arguments arrive as JSON. Anything else is runtime context, not a tool argument.
            if (value is JsonElement or null or string or bool or int or long or double or decimal)
            {
                supplied[key] = value;
            }
        }

        var result = await _facade.InvokeAsync(_tool.Name, supplied, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.SerializeToElement(result);
    }
}
