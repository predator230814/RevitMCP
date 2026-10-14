namespace BimIt.Revit.Assistant.Providers.GitHubCopilot;

internal static class CopilotRuntimeDirectory
{
    public static string Create()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BIM-IT",
            "Assistant",
            "CopilotRuntime");
        var path = Path.Combine(root, Environment.ProcessId + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static void TryDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
