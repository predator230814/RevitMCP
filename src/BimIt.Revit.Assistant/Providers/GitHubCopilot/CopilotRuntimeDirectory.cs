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

    public const int DeleteAttempts = 4;

    public static readonly TimeSpan DeleteDelay = TimeSpan.FromMilliseconds(50);

    public static void TryDelete(string? path)
    {
        TryDelete(path, DeleteAttempts, DeleteDelay, Thread.Sleep, Directory.Exists, (target, recursive) => Directory.Delete(target, recursive));
    }

    internal static void TryDelete(
        string? path,
        int attempts,
        TimeSpan delay,
        Action<TimeSpan> wait,
        Func<string, bool> exists,
        Action<string, bool> delete)
    {
        if (string.IsNullOrWhiteSpace(path) || attempts < 1)
        {
            return;
        }

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            if (!exists(path))
            {
                return;
            }

            try
            {
                delete(path, true);
                return;
            }
            catch (IOException) when (attempt < attempts)
            {
                wait(TimeSpan.FromMilliseconds(delay.TotalMilliseconds * attempt));
            }
            catch (UnauthorizedAccessException) when (attempt < attempts)
            {
                wait(TimeSpan.FromMilliseconds(delay.TotalMilliseconds * attempt));
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
