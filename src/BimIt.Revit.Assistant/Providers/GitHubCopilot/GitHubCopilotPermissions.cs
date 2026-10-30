using GitHub.Copilot;

namespace BimIt.Revit.Assistant.Providers.GitHubCopilot;

internal static class GitHubCopilotPermissions
{
    /// <summary>Only a custom tool on this session's exposed surface may run. A trusted write never does.</summary>
    public static bool IsAllowed(PermissionRequest request, IReadOnlySet<string> exposed)
    {
        ArgumentNullException.ThrowIfNull(exposed);
        return request is PermissionRequestCustomTool tool
            && tool.ToolName is not null
            && exposed.Contains(tool.ToolName)
            && !AssistantTools.IsTrustedWriteTool(tool.ToolName);
    }
}
