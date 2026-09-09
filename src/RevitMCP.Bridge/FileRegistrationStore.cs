using System.Text.Json;
using RevitMCP.Contracts;

namespace RevitMCP.Bridge;

public sealed class FileRegistrationStore : IRegistrationStore
{
    public FileRegistrationStore(string? rootPath = null)
    {
        RootPath = rootPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RevitMCP",
            "instances");
    }

    public string RootPath { get; }

    public string GetRegistrationPath(int windowsSessionId, string instanceId)
    {
        return Path.Combine(RootPath, windowsSessionId.ToString(), $"{instanceId}.json");
    }

    public async Task<IRegistrationLease> PublishAsync(RevitInstanceRegistration registration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registration);

        var path = GetRegistrationPath(registration.WindowsSessionId, registration.InstanceId);
        var directory = Path.GetDirectoryName(path);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(registration, ContractJson.Options);
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

        await File.WriteAllTextAsync(temporaryPath, json, cancellationToken).ConfigureAwait(false);
        File.Move(temporaryPath, path, overwrite: true);

        return new RegistrationLease(this, registration);
    }

    public async Task<IReadOnlyList<RegistrationReadResult>> ReadSessionAsync(int windowsSessionId, CancellationToken cancellationToken)
    {
        var sessionDirectory = Path.Combine(RootPath, windowsSessionId.ToString());
        if (!Directory.Exists(sessionDirectory))
        {
            return [];
        }

        var results = new List<RegistrationReadResult>();
        foreach (var filePath in Directory.EnumerateFiles(sessionDirectory, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await ReadOneAsync(filePath, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    public Task RemoveAsync(int windowsSessionId, string instanceId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetRegistrationPath(windowsSessionId, instanceId);
        TryDelete(path);
        return Task.CompletedTask;
    }

    internal static bool TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static async Task<RegistrationReadResult> ReadOneAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(filePath);
            var registration = await JsonSerializer.DeserializeAsync<RevitInstanceRegistration>(stream, ContractJson.Options, cancellationToken).ConfigureAwait(false);
            if (registration is null || !IsValid(registration))
            {
                return new RegistrationReadResult { FilePath = filePath };
            }

            return new RegistrationReadResult { FilePath = filePath, Registration = registration };
        }
        catch (JsonException)
        {
            return new RegistrationReadResult { FilePath = filePath };
        }
        catch (IOException)
        {
            return new RegistrationReadResult { FilePath = filePath };
        }
    }

    private static bool IsValid(RevitInstanceRegistration registration)
    {
        return !string.IsNullOrWhiteSpace(registration.InstanceId)
            && registration.ProcessId > 0
            && registration.WindowsSessionId >= 0
            && !string.IsNullOrWhiteSpace(registration.RevitVersion)
            && !string.IsNullOrWhiteSpace(registration.RevitBuild)
            && !string.IsNullOrWhiteSpace(registration.AddinVersion)
            && registration.BridgeProtocolVersion > 0
            && !string.IsNullOrWhiteSpace(registration.PipeName);
    }
}
