using GitHub.Copilot;

namespace BimIt.Revit.Assistant.Providers.GitHubCopilot;

internal static class GitHubCopilotPermissions
{
    public static bool IsAllowed(PermissionRequest request)
    {
        return request is PermissionRequestCustomTool tool && BimItAgentReadTools.IsExposed(tool.ToolName);
    }
}
